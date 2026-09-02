using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Adult-mode cosmetic appendage: a small collider-less Verlet pendulum hanging from the
    /// bottom-centre of the pelvis. A cylindrical shaft spans the chain, capped at the far end by a
    /// slightly WIDER hemisphere, and swings under gravity + body motion; two spheres ("berries")
    /// sit at the attachment. Modeled on HairSim (root pin, Verlet integrate, segment-length
    /// constraints), but with only a few nodes and rigid primitive pieces re-posed from the nodes
    /// each tick instead of a card mesh.
    ///
    /// The shaft was one stretched sphere before, which tapers along its whole length and reads as a
    /// capsule. A constant-radius cylinder plus an over-wide cap gives the silhouette a shoulder at
    /// the join, and that step is the part the eye actually recognises.
    ///
    /// Hanging, it is purely visual: its own pieces have NO colliders (never a hitbox on the ball).
    /// It DOES push out of player bodies (own + others') by testing the free nodes against nearby
    /// ragdoll colliders, but explicitly ignores the ball, so the ball's motion is never affected.
    /// Attached + sized by Cosmetics.AttachAdult when PlayerAppearance.Adult is true.
    ///
    /// STANDING TO ATTENTION (the ThirdLeg bind, held): the chain is eased onto a rigid line out of
    /// the pelvis (SimConfig.ThirdLegAngleDeg above its forward axis) and a capsule hitbox fixed
    /// along that line is enabled, so the ball CAN be struck with it - BallController routes such a
    /// contact through the header's goal-ward redirect as ShotType.ThirdLeg. The hitbox is a child
    /// of the pelvis Rigidbody (a compound shape), registered as one of the body's own colliders so
    /// it never fights the thighs or the ground probe, and it is disabled again as the piece drops.
    ///
    /// TWO BODY PLANS. A biped keeps the hand-tuned literals below verbatim. A QUADRUPED cannot: its
    /// pelvis is a different size per species and the barrel hangs a belly out in front of it, so a
    /// constant drop is wrong for every species but one. For a quadruped the anchor height is
    /// MEASURED off the built colliders - the lower of the pelvis and barrel undersides, i.e. whatever
    /// actually forms the undercarriage there - which puts a horse and an elephant in the right place
    /// with no per-species table and keeps working if either body is reshaped.
    /// </summary>
    public class AnatomySim : MonoBehaviour
    {
        Transform _pelvis;
        float _scale;                 // girth scale (matches the body)

        // Chain: node 0 = pinned root at the pelvis attach point; 1..N = free, hanging.
        // Four nodes (root + three free) give a longer, smoother curve so the piece reads as an
        // elongated limb rather than a stub.
        const int Nodes = 4;
        readonly Vector3[] _pos = new Vector3[Nodes];    // world
        readonly Vector3[] _prev = new Vector3[Nodes];
        Vector3 _rootLocal;           // shaft attach: front underside of the pelvis (pelvis-local)
        Vector3 _berryLocal;          // berry seat: tucked up under the torso, centred
        float _segLen;                // rest length between nodes

        Transform _member;            // the shaft: a cylinder spanning root -> tip node
        Transform _tip;               // cap on the far end: a sphere wider than the shaft, pink
        Transform _berryL, _berryR;   // the two spheres at the attachment
        float _memberRadius, _berryRadius;

        // ---- Third leg: stand to attention (SimConfig.ThirdLeg*) ----
        // Erect is the TARGET the Striker writes every tick (a networked puppet gets it from the
        // snapshot); _erect01 eases toward it. Hanging, there is still no collider anywhere.
        public bool Erect;
        float _erect01;
        Vector3 _erectLocalDir;      // pelvis-local unit direction of the erect line
        CapsuleCollider _hit;        // the hitbox: fixed in pelvis space, enabled only while erect
        ActiveRagdoll _rag;          // for the self-collision ignores each time the hitbox comes on

        /// <summary>0..1 how far toward attention the piece currently is.</summary>
        public float Erect01 => _erect01;
        /// <summary>Is the hitbox live right now (erect enough that the ball can be struck with it)?</summary>
        public bool IsErect => _hit != null && _hit.enabled;
        /// <summary>Is `c` this piece's hitbox? BallController asks on every body contact.</summary>
        public bool IsHitbox(Collider c) => c != null && _hit != null && c == _hit;

        /// <summary>
        /// The posed pieces, for the replay recorder (ReplaySystem.TrackBody): it records these like
        /// bones and pauses this component for the playback, so a replay shows the piece exactly as
        /// it was - standing to attention included - instead of a fresh hang re-simulated off the
        /// replayed pelvis. Position, rotation AND scale matter: the shaft's length is its scale.
        /// </summary>
        public Transform[] ReplayTransforms
        {
            get
            {
                var list = new System.Collections.Generic.List<Transform>(4);
                if (_member != null) list.Add(_member);
                if (_tip != null) list.Add(_tip);
                if (_berryL != null) list.Add(_berryL);
                if (_berryR != null) list.Add(_berryR);
                return list.ToArray();
            }
        }

        // Reused buffer for body-collision queries (avoids per-tick allocation).
        //
        // 8 was too small and OverlapSphereNonAlloc TRUNCATES silently. A body has 13 colliders
        // (Bone.Count); the node sits where the pelvis, the torso/barrel and BOTH thighs can reach
        // it, which is 4 from its own body, and a slide-tackle scrum adds as many from an opponent.
        // Worse, the turf and the ball fill slots BEFORE the ragdoll filter below runs, so the
        // collider we most needed could be the one dropped. 16 covers own body + one full opponent
        // + strays. Still a truncating query, just not at a count that happens in normal play.
        readonly Collider[] _hits = new Collider[16];

        // base dimensions in metres (scaled by girth). Small + tasteful-ish.
        //
        // IMPORTANT: the pelvis is a BOX of full size (0.32, 0.20, 0.20) - so it spans only
        // +-0.10 in Y and +-0.10 in Z from the bone origin - and the torso box sits directly on
        // top of it. Anything placed at or above y = -0.10 is INSIDE the pelvis/torso mesh and is
        // therefore invisible. (That was the bug: the berries were at y = +0.10, buried in the
        // body.) Both anchors must clear the pelvis BOTTOM face at y = -0.10.
        //
        // Layering: the shaft roots FLUSH with the pelvis and hangs down IN FRONT OF the berries,
        // so at rest it drapes over them and swinging aside reveals them.
        //  - berries tuck up under the bottom face (top just kissing y = -0.10) and sit BEHIND the
        //    shaft in Z, so the shaft occludes them at rest,
        //  - the shaft root sits just INSIDE the pelvis (above the bottom face) so its top end is
        //    buried in the hips - flush, with no floating gap - and further FORWARD in Z.
        const float BerryDrop  = 0.16f;   // berries: centre below the bottom face
        const float BerryFwd   = 0.065f;  // berries: forward, toward the pelvis underside's front
                                           // edge, but still short of MemberFwd (0.075) so the shaft
                                           // still roots further forward and the drapes-over-at-rest
                                           // occlusion described above still holds
        const float MemberDrop = 0.09f;   // member root: just inside the pelvis -> reads as attached
        const float MemberFwd  = 0.075f;  // member root: further forward, so it hangs OVER the berries
        // Long + slender to mirror a forearm (~0.60 tall at 0.09 dia ≈ 6.7:1). Three segments at
        // 0.10 span a ~0.30 chain; with the cap the piece ends up ~0.34 long at 0.056 dia ≈ 6:1.
        const float SegLen   = 0.10f;     // per-segment rest length
        const float MemberR  = 0.028f;    // shaft radius (thin, arm-like)
        // The cap is a sphere centred ON the shaft's end face. Wider than the shaft, which is the
        // point: the extra radius shows as a lip right around the join instead of a seamless rounded
        // end.
        //
        // NOT exactly a hemisphere, and the difference now matters because the cap has its own
        // colour. Being wider than the cylinder, the sphere bulges back out THROUGH the cylinder's
        // side wall for sqrt(CapWiden^2 - 1) = 0.831 shaft radii before it sinks inside, so the
        // visible cap is the protruding hemisphere PLUS a collar that far up the shaft: 2.131 shaft
        // radii of axial length, 17.7% of the piece at base Third Leg skills. That collar IS the lip
        // described above. It only becomes legible as one once it is painted differently.
        const float CapWiden = 1.30f;     // cap radius, x the shaft radius
        // Cap colour. FIXED rather than derived from the skin, for three reasons: a derivation
        // collapses on an already pink skin, which is the one case worth defending; it would make the
        // cap follow the coat wheel instead of being a feature you can name; and a constant is
        // identical on a networked puppet by construction, where a derivation depends on the received
        // appearance being final at build time.
        //
        // HSV(341.5, 0.553, 0.94). Checked against all 24 shipped swatches for the three AllowsAdult
        // species (SpeciesCosmetics): no swatch is close on more than one axis. Nearest in luminance
        // is dapple grey at 0.018, which is 0.521 away in saturation; nearest in saturation is
        // palomino at 0.011, which is 58 deg away in hue; nearest in hue is 38 deg (liver coat, dark
        // hide), both 0.25+ away in luminance. KNOWN LIMIT, not worth code: the colour wheel is free,
        // so a player who dials in this exact pink gets a cap they cannot pick out.
        static readonly Color CapPink = new Color(0.94f, 0.42f, 0.58f);
        const float BerryR   = 0.032f;    // berry radius
        const float BerryGap = 0.03f;     // half the spacing between the two berries

        // CONE LIMIT about the pelvis's own DOWN axis (-pelvis.up). Nothing used to constrain the
        // chain's direction, and since PoseMember draws the shaft as a straight rod from node 0 to
        // node Nodes-1, a folded chain could aim that rod straight UP through the torso - the
        // "clipping through the body" half of the bug report. The cone is taken in PELVIS space
        // because that is the only frame where "into the body" is a fixed direction: the anchor sits
        // on the underside, so everything above -pelvis.up is body. Cost of that choice: on a supine
        // body the piece lies over the hip rather than hanging vertically. Accepted. A world-down
        // cone would need a second half-space test against the pelvis underside, and that test on
        // its own IS the pelvis-down cone at 90 deg, which fails the clearance below.
        //
        // 65 deg, measured off the two constants above rather than picked. The deepest node has to
        // keep the CAP clear of the underside plane: cos(theta) * SegLen >= CapWiden * MemberR, so
        // theta <= acos(1.30 * 0.028 / 0.10) = 68.66 deg. That bound is scale-INVARIANT (both terms
        // carry _scale) so it is identical for all three adult species: human, horse and elephant
        // all give 0.3640. 65 keeps 3.66 deg of margin AT BASE SKILLS ONLY.
        //
        // LIMITATION, and it bites at ordinary builds, not just at the defensive floor: the bound is
        // acos(0.364 * girthMul / lenMul), so it moves with the Third Leg sliders and girth and length
        // are INDEPENDENT nodes. Spending on girth alone is a normal build and it eats the margin:
        //     base                     girth 1.00 len 1.00 -> 68.7 deg, ok
        //     tl0 + tl1b               girth 1.40 len 1.10 -> 62.4 deg, VIOLATED by 2.6
        //     tl0 + tl1b + tl2b        girth 1.70 len 1.10 -> 55.8 deg, VIOLATED by 9.2
        //     girth max + Anaconda     girth 2.10 len 1.70 -> 63.3 deg, VIOLATED by 1.7
        //     every node               girth 2.10 len 2.30 -> 70.6 deg, ok
        // So the reachable worst case is 55.8 deg, not the 68.7 the base numbers suggest, and at the
        // lenMul floor of 0.25 the ratio exceeds 1 and NO cone angle clears the cap at all. The cone
        // is therefore not a guarantee; the collision push below is what actually covers those
        // builds. Do not retune ConeDeg off the base figure alone.
        // DERIVED PER INSTANCE, not a constant, because a constant cannot be right. The clearance
        // bound is acos(CapWiden * memberRadius / segLen), and both terms move with the Third Leg
        // sliders AND with the biped rescale above, so the ceiling swings enormously across reachable
        // builds - measured at every corner of the new sizing:
        //     human base 66.5, human length-only 80.0, human GIRTH-ONLY 33.2, human maxed 68.7
        //     horse base 68.7, horse girth-only 60.9, horse maxed 69.6
        //     elephant base 68.7, elephant girth-only 60.3, elephant maxed 69.7
        // A girth-only human is an ordinary build (girth and length are independent nodes) and its
        // ceiling is 33.2 deg, half the 65 that used to be hard-coded. So compute it from the geometry
        // that was actually built, keep a little margin under it, and cap it so a very slender piece
        // does not get a cone so wide it stops constraining anything.
        const float ConeDegMax = 70f;     // ceiling on the derived angle
        const float ConeMargin = 0.94f;   // fraction of the clearance bound to actually allow
        float _coneCos = Mathf.Cos(65f * Mathf.Deg2Rad);
        float _coneSin = Mathf.Sin(65f * Mathf.Deg2Rad);

        // Recompute the cone from the built geometry. Called once from Build, after the radii are set.
        void DeriveCone()
        {
            float ratio = (CapWiden * _memberRadius) / Mathf.Max(1e-4f, _segLen);
            // ratio >= 1 means the cap is wider than a whole segment and NO angle clears it. Fall back
            // to a tight cone and let the collision push carry that build; it is degenerate anyway.
            float deg = ratio >= 0.999f ? 20f
                      : Mathf.Acos(ratio) * Mathf.Rad2Deg * ConeMargin;
            deg = Mathf.Clamp(deg, 15f, ConeDegMax);
            _coneCos = Mathf.Cos(deg * Mathf.Deg2Rad);
            _coneSin = Mathf.Sin(deg * Mathf.Deg2Rad);
        }

        // Hard ceiling on a free node's speed, only ever reached by pathology. The body's own top
        // speed is SimConfig.SprintSpeedCeiling = 19.7 m/s, so 25 leaves a full-pace sprint plus its
        // pendulum overshoot completely untouched. This is defence in depth and does almost nothing
        // in normal play: the re-seed in FixedUpdate catches real teleports, and this catches the
        // mid-sized injections nobody enumerated (a SnapFacing yaw flip moves a horse's anchor
        // ~0.37 m in one tick, which is ~19 m/s and well under the re-seed cut).
        const float MaxNodeSpeed = 25f;   // m/s

        // Per-collider, per-tick cap on a collision push, so a buried node WALKS out over a few
        // ticks instead of being flung. See the escape branch in FixedUpdate for what it replaces.
        const float MaxPush = 0.04f;      // metres

        // QUADRUPED fore/aft anchor, in unit-scale metres forward of the hip bone: just ahead of
        // the hip centre, between the hind legs. Y is measured, not constant, and the SHAFT's fore/aft
        // is derived from this plus the two radii (see Build) rather than being a second constant that
        // could drift away from it. Scaled by HEIGHT, following BodyLayoutDef.Off: a quadruped sets
        // LengthAlongHeight, so its fore/aft axis tracks the height scale.
        const float QuadBerryFwd  = 0.06f;

        // The BIPED piece is authored at 1/ladder, so a MAXED human lands exactly on the size the
        // unmaxed human used to be. That was the brief: the old base was the right size for ANACONDA,
        // not for a starting body. Length divides by its own full multiplier (2.30) and girth by its
        // own (2.10), separately, so the maxed human matches the old base in BOTH length and thickness
        // and today's silhouette proportions survive the rescale exactly.
        //
        // Quadrupeds get neither factor: their base was explicitly kept. They get a reduced
        // AdultGrowth instead (see SpeciesDef.AdultGrowth), which is what brings their ANACONDA down.
        const float BipedLenScale   = 1f / 2.30f;   // 0.4348, the full "length" ladder
        const float BipedGirthScale = 1f / 2.10f;   // 0.4762, the full "girth"/"ballsize" ladder

        // lenMul/girthMul/ballMul come from the adult "Third Leg" skill nodes (1 = base): they
        // stretch the member length, thicken it, and grow the berries respectively. sizeMul is
        // SpeciesDef.AdultScale, the per-species size on top of the build scale; growth is
        // SpeciesDef.AdultGrowth, the fraction of the skill ladder this species receives.
        public void Build(ActiveRagdoll rag, Transform pelvis, Color skin, float sizeMul, float growth,
                          float lenMul, float girthMul, float ballMul)
        {
            _pelvis = pelvis;
            _rag = rag;
            bool quad = rag != null && rag.Plan == BodyPlan.Quadruped;
            float gScale = rag != null ? rag.GirthScale : 1f;
            float hScale = rag != null ? rag.HeightScale : 1f;
            sizeMul = Mathf.Max(0.25f, sizeMul);
            // Human path is unchanged on purpose: girth alone, exactly as before. A quadruped takes
            // the geometric mean of girth and height so a long, deep animal grows the piece once
            // rather than twice, then the species multiplier on top.
            //
            // BEWARE sizeMul: it is a SECOND species term on a scale that already has one, and that
            // double count is what made the quadruped piece read as a fifth leg. sqrt(g*h) measures
            // 1.176 on a default horse and 1.383 on a default elephant against a human's 1.013, so an
            // AdultScale of 1.7 / 1.9 put the piece at 1.97x / 2.59x the human's on a body only 1.18x
            // / 1.34x as big. Both are 1 now (SpeciesDef.AdultScale carries the full measurement),
            // which lands the quadruped at 43% / 46% of its own attach height against the biped's 37%:
            // proportionally bigger, as the field intended, counted once.
            _scale = Mathf.Max(0.5f, quad ? Mathf.Sqrt(gScale * hScale) * sizeMul : gScale);
            // Defensive floors: a stray 0 (e.g. an un-initialised appearance) must not collapse it.
            lenMul   = Mathf.Max(0.25f, lenMul);
            girthMul = Mathf.Max(0.25f, girthMul);
            ballMul  = Mathf.Max(0.25f, ballMul);
            // Species growth: scale the skill ladder's GROWTH, not the base, so a species with a small
            // AdultGrowth still starts exactly where it started and simply climbs less far. Written on
            // the (mul - 1) term for that reason - scaling mul itself would shrink the base too.
            growth = Mathf.Clamp01(growth <= 0f ? 1f : growth);
            lenMul   = 1f + (lenMul   - 1f) * growth;
            girthMul = 1f + (girthMul - 1f) * growth;
            ballMul  = 1f + (ballMul  - 1f) * growth;
            float bLen   = quad ? 1f : BipedLenScale;
            float bGirth = quad ? 1f : BipedGirthScale;
            _segLen = SegLen * _scale * bLen * lenMul;         // longer segments -> longer member
            _memberRadius = MemberR * _scale * bGirth * girthMul;
            _berryRadius = BerryR * _scale * bGirth * ballMul;

            // Two anchors (pelvis-local, -Y = down). The berries hang just clear of the pelvis
            // bottom face and sit BEHIND the member in Z; the member roots flush inside the hips and
            // further FORWARD, so it drapes over the berries at rest and uncovers them as it swings.
            if (quad)
            {
                // MEASURED, because a quadruped's clearances are per species. BOTH anchors hang off
                // the SAME surface - the lower of the pelvis and barrel undersides, i.e. whichever
                // body part actually forms the undercarriage back there - so the shaft roots into the
                // belly the berries hang from instead of ending in mid air near it.
                //
                // It used to root off the BARREL underside 0.40 forward, out under the middle of the
                // belly, while the berries sat 0.06 forward off the PELVIS underside: two different
                // surfaces a third of a metre apart, so the shaft hung from the centre of the
                // undercarriage attached to nothing with the berries stranded behind it.
                //
                // Fore/aft is now DERIVED - one berry radius plus one shaft radius ahead of the
                // berries - so the two touch without intersecting at any body size or skill loadout,
                // which two hand-tuned constants could not promise.
                float under = Mathf.Min(Bottom(rag, Bone.Pelvis, pelvis.position.y),
                                        Bottom(rag, Bone.Torso,  pelvis.position.y));
                float berryFwd = QuadBerryFwd * hScale;
                _berryLocal = LocalUnder(pelvis, under, _berryRadius, berryFwd);
                _rootLocal  = LocalUnder(pelvis, under, _memberRadius * -0.35f,
                                         berryFwd + _berryRadius + _memberRadius);
            }
            else
            {
                _berryLocal = new Vector3(0f, -BerryDrop,  BerryFwd)  * _scale;
                _rootLocal  = new Vector3(0f, -MemberDrop, MemberFwd) * _scale;
            }

            DeriveCone();

            // Seed the chain hanging straight down in world space from the attach point.
            Vector3 root = _pelvis.TransformPoint(_rootLocal);
            for (int i = 0; i < Nodes; i++)
            {
                _pos[i] = root + Vector3.down * (_segLen * i);
                _prev[i] = _pos[i];
            }

            var mat = Make.Mat(skin, 0.15f);
            if (rag != null) rag.RegisterCosmeticMaterial(mat);
            // Second material, for the cap ONLY. Same shader and the same 0.15 smoothness as the skin
            // one, so the pair reads as one surface in two colours rather than two materials catching
            // the light differently. Registered the same way, and that is not optional:
            // ActiveRagdoll.OnDestroy is the only thing that frees these, destroying a GameObject
            // does NOT free its materials, and the species preview rebuilds this whole body on every
            // drag frame - so an unregistered material leaks once per frame of dragging.
            var capMat = Make.Mat(CapPink, 0.15f);
            if (rag != null) rag.RegisterCosmeticMaterial(capMat);

            _member = MakePiece("member", mat, cylinder: true).transform;
            _tip    = MakePiece("tip", capMat).transform;
            _berryL = MakePiece("berryL", mat).transform;
            _berryR = MakePiece("berryR", mat).transform;

            BuildHitbox(rag, quad);

            PoseBerries();
            PoseMember();
        }

        // The erect line and its hitbox. The line is fixed in PELVIS space (forward and up by the
        // species' angle), so the capsule can be a fixed child of the pelvis: no per-tick re-posing,
        // and PhysX sees a compound shape on the pelvis body rather than a moving static collider.
        // Sized to the DRAWN piece - shaft length plus the cap's protruding radius - at
        // ThirdLegHitboxMul x the cap radius, generous the way the head's collider is.
        void BuildHitbox(ActiveRagdoll rag, bool quad)
        {
            float a = (quad ? SimConfig.ThirdLegQuadAngleDeg : SimConfig.ThirdLegAngleDeg) * Mathf.Deg2Rad;
            _erectLocalDir = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
            float capR = _memberRadius * CapWiden;
            float len = _segLen * (Nodes - 1) + capR;
            float r = capR * SimConfig.ThirdLegHitboxMul;

            var go = new GameObject("hitbox");
            go.transform.SetParent(transform, false);   // this object sits at the pelvis origin, unrotated
            go.transform.localPosition = _rootLocal + _erectLocalDir * (len * 0.5f);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, _erectLocalDir);
            go.transform.localScale = Vector3.one;
            _hit = go.AddComponent<CapsuleCollider>();
            _hit.direction = 1;             // local Y, rotated onto the erect line above
            _hit.radius = r;
            _hit.height = len + 2f * r;     // capsule ends one radius past the root and past the cap
            _hit.enabled = false;           // hanging: no hitbox. Enabled by FixedUpdate as it rises.
            if (rag != null) rag.RegisterExtraCollider(_hit);
        }

        // Lowest world y of a bone's collider, i.e. the underside of that body part. Falls back to
        // the given y if the bone or its collider is missing.
        static float Bottom(ActiveRagdoll rag, Bone b, float fallback)
        {
            var t = rag != null ? rag.Phys(b) : null;
            if (t == null) return fallback;
            var col = t.GetComponent<Collider>();
            return col != null ? col.bounds.min.y : t.position.y;
        }

        // A pelvis-local anchor sitting `drop` metres BELOW the world height `surfaceY`, at `fwd`
        // metres along the pelvis's forward axis. Goes through InverseTransformPoint rather than
        // subtracting heights, so it stays correct whatever rotation the bone is built at.
        static Vector3 LocalUnder(Transform pelvis, float surfaceY, float drop, float fwd)
        {
            Vector3 w = new Vector3(pelvis.position.x, surfaceY - drop, pelvis.position.z);
            return new Vector3(0f, pelvis.InverseTransformPoint(w).y, fwd);
        }

        // A collider-less primitive child, sphere or cylinder. Built at unit size and re-scaled every
        // tick by Pose*, so the dimensions passed here are placeholders. Make.Cylinder is authored
        // along local +Y, which is the axis PoseMember rotates onto the chain. Parented under this
        // component's GameObject, which itself hangs under the pelvis.
        GameObject MakePiece(string name, Material mat, bool cylinder = false)
        {
            var go = cylinder ? Make.Cylinder(name, 0.5f, 1f, transform.position, 1, mat, transform)
                              : Make.Sphere(name, 1f, transform.position, mat, transform);
            // Make.Cylinder swaps the primitive's collider for a capsule; either way, drop it.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // never a hitbox
            return go;
        }

        void FixedUpdate()
        {
            if (_pelvis == null) return;
            float dt = Time.fixedDeltaTime;
            float g = SimConfig.HairGravity;
            float damp = SimConfig.HairDamping;
            float gStep = g * dt * dt;
            Vector3 down = -_pelvis.up;                 // cone axis; see DeriveCone
            Vector3 fwd  = _pelvis.forward;             // the half-space the piece may never leave; see ClampCone

            // 1) Pin the root to the pelvis attach point; Verlet-integrate the free nodes under
            //    gravity. The pinned root moving with the body drags the chain, so it swings.
            //
            //    _pos[0] still holds LAST tick's root at this point, so the root's own step is free
            //    to measure. A step longer than the whole chain means the pelvis did not move, it
            //    was TELEPORTED: ActiveRagdoll.SnapBone, SnapLayout, DisplayEmote and DisplayPose
            //    all write rb.transform.position directly, and the MP puppet paths do it EVERY
            //    FRAME. No swing state survives that. Left alone, the segment constraint below drags
            //    node 1 the entire distance in one tick - a 30 m respawn reads as 2100 m/s at this
            //    project's measured fixedDeltaTime of 0.014 (71.4 Hz, NOT Unity's default 50), and at
            //    HairDamping 0.92 the decay time constant is dt / -ln(0.92) = 0.17 s, so that orbits
            //    the hip for roughly half a second before it bleeds off. Re-seed instead, hanging
            //    along the body's own down so the fresh chain starts inside the cone by construction.
            Vector3 root = _pelvis.TransformPoint(_rootLocal);
            float rootStep = (root - _pos[0]).magnitude;
            _pos[0] = root; _prev[0] = root;
            // Threshold is a SPEED floor, not just the chain length, and that matters: chain length
            // alone is 0.30 m on a default human, while a sprint at SprintSpeedCeiling 19.7 m/s moves
            // the anchor 0.276 m in one tick at the live fixedDeltaTime of 0.014 - a 4% margin, and
            // pelvis ROTATION stacks on top of translation (a yaw flip alone moves a biped anchor up
            // to 2 * 0.075 * _scale = 0.15 m). At Unity's default 0.02 the sprint step is 0.394 m and
            // a bare chain-length test would trip EVERY TICK while sprinting, hard-resetting the chain
            // to a rigid vertical rod - the exact opposite of the swing this is meant to preserve.
            // MaxNodeSpeed * dt * 2 is 0.70 m at 0.014 and 1.00 m at 0.02, both clear of real motion.
            float teleport = Mathf.Max(_segLen * (Nodes - 1), MaxNodeSpeed * dt * 2f);
            if (rootStep > teleport)
            {
                for (int i = 1; i < Nodes; i++) { _pos[i] = root + down * (_segLen * i); _prev[i] = _pos[i]; }
                PoseBerries();
                PoseMember();
                return;
            }
            float maxStep = MaxNodeSpeed * dt;
            for (int i = 1; i < Nodes; i++)
            {
                Vector3 vel = (_pos[i] - _prev[i]) * damp;
                // Bound the implicit velocity. Nothing else in this loop limits it, and a pelvis
                // snap too small to trip the re-seed above still injects tens of m/s.
                float vm = vel.magnitude;
                if (vm > maxStep) vel *= maxStep / vm;
                _prev[i] = _pos[i];
                _pos[i] += vel;
                _pos[i] += new Vector3(0f, gStep, 0f);
            }

            // 2) Segment-length constraints (a few iterations), root fixed, with the cone limit
            //    re-applied after each pass so the two solve together instead of one undoing the
            //    other on the way out.
            for (int it = 0; it < SimConfig.HairConstraintIters; it++)
            {
                for (int i = 0; i < Nodes - 1; i++)
                {
                    int a = i, b = i + 1;
                    Vector3 d = _pos[b] - _pos[a];
                    float len = d.magnitude;
                    if (len < 1e-6f) continue;
                    float diff = (len - _segLen) / len;
                    if (a == 0) _pos[b] -= d * diff;                     // root fixed: move only b
                    else { _pos[a] += d * (0.5f * diff); _pos[b] -= d * (0.5f * diff); }
                }
                ClampCone(down, fwd);
            }

            // 3) Body collision: push each FREE node out of any player-body collider it sinks into,
            //    but NEVER the ball. No physics layers exist in this project, so filter by component:
            //    keep ragdoll colliders, skip anything under a BallController (or our own pieces).
            //
            //    Every push goes through Push(), which moves _prev with _pos. Verlet velocity is
            //    implicit - vel = pos - prev - so the old code, moving _pos alone, turned every
            //    correction into a velocity injection sized by the correction. That was the flail.
            for (int i = 1; i < Nodes; i++)
            {
                // The cap renders at CapWiden x the shaft radius, so the last node must be tested at
                // that radius or the cap permanently sinks 30% of its radius into anything it meets.
                float r = i == Nodes - 1 ? _memberRadius * CapWiden : _memberRadius;
                int n = Physics.OverlapSphereNonAlloc(_pos[i], r, _hits, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                {
                    var col = _hits[h];
                    if (col == null || col == _hit) continue;   // never push out of our own hitbox
                    // Cheapest reject first, which matters now the buffer is 16: every ragdoll bone
                    // carries its Rigidbody on the SAME GameObject as its collider (ActiveRagdoll
                    // builds both on the "P_<Bone>" object), so static scenery - the turf above all -
                    // fails this on one field read instead of two GetComponentInParent walks. The
                    // ball has a body, so it still goes through the walk below.
                    if (col.attachedRigidbody == null) continue;
                    if (col.GetComponentInParent<BallController>() != null) continue;   // never the ball
                    if (col.GetComponentInParent<ActiveRagdoll>() == null) continue;    // only player bodies
                    // Push the node to just outside the collider surface.
                    Vector3 cp = col.ClosestPoint(_pos[i]);
                    Vector3 away = _pos[i] - cp;
                    float m = away.magnitude;
                    if (m < 1e-4f)
                    {
                        // ClosestPoint returned the query point: the node's CENTRE is inside this
                        // collider, so there is no surface normal to push along. Exit along the
                        // body's own DOWN - the piece is anchored on the underside, so that is the
                        // only direction guaranteed to leave the body rather than enter the next part
                        // of it (the old escape routinely shoved the node sideways out of the pelvis
                        // and straight into a thigh capsule, which it had already visited and would
                        // not recheck). Capped, so a deep burial walks out over a few ticks.
                        //
                        // It used to teleport to col.bounds.center + horizontal * (extents.magnitude
                        // + r). Wrong three ways: away.y was zeroed so the exit was purely sideways;
                        // the result inherits bounds.center.y so the node SNAPS UP to the collider's
                        // mid height, i.e. chest or withers; and the distance is the collider's
                        // bounding-sphere radius. Measured: 0.24 m for a human pelvis box
                        // (0.32,0.20,0.20), 0.68 m for a horse barrel, 0.84 m for an elephant's -
                        // 12 / 34 / 42 m/s injected in one 20 ms tick. A biped tripped it on any
                        // hard forward swing, because the shaft roots INSIDE the pelvis by design
                        // (MemberDrop 0.09 against the box's 0.10 half-height) so node 1 sweeps up
                        // into the box the moment the chain passes horizontal.
                        Push(i, down * Mathf.Min(MaxPush, r));
                    }
                    else if (m < r)
                    {
                        // Shortest exit: straight out along the surface normal, exactly to the skin.
                        Push(i, away * ((r - m) / m));
                    }
                }
            }

            // 4) Re-solve length + cone after the pushes. Without this the pushed node stays wherever
            //    collision left it and PoseMember scales the shaft to the raw node0 -> nodeN
            //    distance, so the piece visibly STRETCHES on contact and is yanked back next tick: a
            //    two-tick buzz on top of the flail. Velocity-NEUTRAL, because this pass only cleans
            //    up a correction. The pass in step 2 stays velocity-carrying, and that is what makes
            //    the chain swing when the body moves.
            for (int i = 0; i < Nodes - 1; i++)
            {
                int a = i, b = i + 1;
                Vector3 d = _pos[b] - _pos[a];
                float len = d.magnitude;
                if (len < 1e-6f) continue;
                float diff = (len - _segLen) / len;
                if (a == 0) Push(b, -d * diff);                          // root fixed: move only b
                else { Push(a, d * (0.5f * diff)); Push(b, -d * (0.5f * diff)); }
            }
            ClampCone(down, fwd);

            // 5) Third leg. Ease toward attention and blend the chain onto the rigid erect line.
            //    Applied LAST so the hanging solve above still owns whatever fraction is not yet
            //    erect, and through Push (velocity-neutral) so letting go resumes the swing from
            //    where the line left it instead of flinging the piece. Fully erect, the nodes are
            //    pinned outright so no residual swing accumulates underneath the pose.
            float rate = Erect ? SimConfig.ThirdLegRiseRate : SimConfig.ThirdLegFallRate;
            _erect01 = Mathf.MoveTowards(_erect01, Erect ? 1f : 0f, rate * dt);
            if (_erect01 > 0.001f)
            {
                Vector3 dirW = _pelvis.TransformDirection(_erectLocalDir);
                bool pinned = _erect01 >= 0.999f;
                for (int i = 1; i < Nodes; i++)
                {
                    Vector3 want = root + dirW * (_segLen * i);
                    Push(i, (want - _pos[i]) * _erect01);
                    if (pinned) _prev[i] = _pos[i];
                }
            }
            // The hitbox follows the ease, not the flag, so a piece that is still visibly dropping
            // cannot strike, and one still rising cannot until it is nearly there. Re-ignore the
            // body's own colliders on every enable: PhysX drops the pairs when a collider disables.
            bool live = _erect01 >= SimConfig.ThirdLegHitboxOn;
            if (_hit != null && _hit.enabled != live)
            {
                _hit.enabled = live;
                if (live && _rag != null) _rag.IgnoreOwnCollisionsWith(_hit);
            }

            PoseBerries();
            PoseMember();
        }

        // Move a node WITHOUT injecting velocity. Verlet velocity is implicit (pos - prev), so a
        // correction that moves _pos alone silently becomes velocity next tick, sized by the
        // correction. Every collision and cone correction goes through here; only integration and
        // the main constraint pass are allowed to change velocity.
        void Push(int i, Vector3 delta) { _pos[i] += delta; _prev[i] += delta; }

        // Clamp every free node to within ConeDeg of `down`, measured FROM THE ROOT rather than from
        // its parent: PoseMember draws the shaft as a straight rod node0 -> nodeN, so bounding that
        // rod's own direction is what actually keeps the DRAWN piece out of the body. Velocity-
        // neutral, so a node pressed on the limit keeps its motion and SLIDES around the cone instead
        // of stopping dead - the piece still reads as alive at full swing.
        //
        // Built from an explicit orthonormal basis instead of Quaternion.AngleAxis: no handedness to
        // get wrong, and it keeps the node in its own swing plane.
        //
        // AND NEVER BEHIND THE HIPS. The cone alone still allowed a backward tilt of up to the cone
        // angle, which is straight through the striker's legs whenever the body leans into a run
        // (pelvis-down tips back, a world-vertical hang reads as backward in pelvis space). The
        // second clamp is a half-space in the pelvis frame: the node's offset from the root may
        // have no component along -forward. Straight down is the limit; it never crosses that plane.
        // Dropping the backward component and restoring the length moves the direction TOWARD the
        // down axis, so the cone clamp just applied still holds and the two never fight.
        void ClampCone(Vector3 down, Vector3 fwd)
        {
            for (int i = 1; i < Nodes; i++)
            {
                Vector3 d = _pos[i] - _pos[0];
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                float cos = Vector3.Dot(d, down) / len;
                if (cos < _coneCos)
                {
                    Vector3 side = d - down * (cos * len);
                    float sm = side.magnitude;
                    Vector3 perp = sm > 1e-6f ? side / sm : _pelvis.right;   // exactly anti-parallel: pick a side
                    Vector3 to = _pos[0] + (down * _coneCos + perp * _coneSin) * len;
                    Push(i, to - _pos[i]);
                    d = to - _pos[0];
                }
                float back = Vector3.Dot(d, fwd);
                if (back < 0f)
                {
                    Vector3 flat = d - fwd * back;                          // shed the backward part
                    float fm = flat.magnitude;
                    Vector3 to = _pos[0] + (fm > 1e-6f ? flat * (len / fm) : down * len);
                    Push(i, to - _pos[i]);
                }
            }
        }

        // The two berries sit tucked up under the torso, side by side, centred at their own fixed
        // anchor (above + behind the member root) so they don't overlap the member. They ride the
        // pelvis rigidly (no sway of their own).
        void PoseBerries()
        {
            if (_berryL == null) return;
            Vector3 seat = _pelvis.TransformPoint(_berryLocal);
            Vector3 right = _pelvis.right;
            float d = _berryRadius * 2f;
            _berryL.position = seat - right * (BerryGap * _scale);
            _berryR.position = seat + right * (BerryGap * _scale);
            _berryL.localScale = _berryR.localScale = new Vector3(d, d, d);
            _berryL.rotation = _berryR.rotation = _pelvis.rotation;
        }

        // A constant-radius cylinder spanning root -> tip node, plus a wider sphere on the far end
        // whose protruding half is the cap.
        void PoseMember()
        {
            if (_member == null) return;
            Vector3 a = _pos[0];
            Vector3 b = _pos[Nodes - 1];
            Vector3 axis = b - a;
            float len = axis.magnitude;
            Quaternion rot = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, axis / len)
                                         : _pelvis.rotation;

            // Unity's cylinder is 2 units tall at scale 1, hence the halved length on local Y.
            float dia = _memberRadius * 2f;
            _member.position = (a + b) * 0.5f;
            _member.rotation = rot;
            _member.localScale = new Vector3(dia, len * 0.5f, dia);

            // Cap centred exactly on the end face. Half of it sits on the body side of that plane,
            // but it is WIDER than the shaft, so 0.831 shaft radii of that half still show through
            // the cylinder wall as the lip. See CapWiden for the derivation.
            if (_tip == null) return;
            float capDia = dia * CapWiden;
            _tip.position = b;
            _tip.rotation = rot;
            _tip.localScale = new Vector3(capDia, capDia, capDia);
        }
    }
}
