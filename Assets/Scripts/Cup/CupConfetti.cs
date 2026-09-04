using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Podium / trophy-lift confetti (design 8.1): a couple of hundred paper quads in the
    /// nation's two kit colours, dropped from ConfettiHeight above the dais, falling under a
    /// light gravity with air drag and a flutter, recycled to the top when they land for the
    /// shower's lifetime, then left to fall out. Hand-rolled like <see cref="HairSim"/>: one
    /// position and one previous position per quad (Verlet), one mesh rebuilt every LateUpdate
    /// behind a dirty flag, one material - the project has no ParticleSystem yet.
    ///
    /// Each quad is drawn double-sided (both windings) so a fluttering card never vanishes when
    /// its back faces the camera. Colour comes from a 2 x 1 texture (the two kit colours) selected
    /// by UV, so the whole shower is one draw call with one material. The mesh, the texture and
    /// the material are freed in OnDestroy (the mesh and texture through the GeneratedMeshOwner
    /// the renderer carries, the material directly).
    ///
    /// Everything seeded (spawn spots, phases, sizes, colour split) comes from a SeededRng so every
    /// peer's shower looks the same at the start; the integration itself runs on Time.deltaTime
    /// (a Solo pause freezes it with the sim), which is cosmetic and need not match across peers.
    /// </summary>
    public sealed class CupConfetti : MonoBehaviour
    {
        // ---- tunables (feel) ----------------------------------------------------------------------
        /// <summary>Quad size (m): a small rectangle of paper.</summary>
        public const float QuadW = 0.13f, QuadH = 0.085f;
        /// <summary>The shower falls within this radius of the centre (m).</summary>
        public const float SpreadRadius = 3.2f;
        /// <summary>Spawn heights are staggered from ConfettiHeight up to ConfettiHeight + this (m), so arrivals spread.</summary>
        public const float SpawnBand = 5f;
        /// <summary>Gravity on a card (m/s^2, down) and the air drag (1/s): terminal speed = g / drag = 1.25 m/s.</summary>
        public const float Gravity = 9.81f;
        public const float Drag = 7.85f;
        /// <summary>Flutter: lateral acceleration amplitude (m/s^2) and its angular rate (rad/s).</summary>
        public const float FlutterAccel = 4.5f;
        public const float FlutterRate = 2.6f;
        /// <summary>A card's spin about the vertical (deg/s, +-) and its tilt swing (deg).</summary>
        public const float SpinMax = 240f;
        public const float TiltMax = 65f;
        /// <summary>How long the shower keeps recycling landed cards (s); after that they fall out.</summary>
        public const float DefaultLife = 20f;

        Vector3 _centre;
        float _life, _elapsed;
        int _n;
        Vector3[] _pos, _prev;
        float[] _phase, _spinRate, _spin, _tiltPhase;
        bool[] _second;   // colour: false = primary, true = secondary
        bool[] _dead;     // fallen out after the life ended
        Mesh _mesh;
        Vector3[] _vtx;
        Vector3[] _nrm;
        Material _mat;
        Texture2D _tex;
        bool _meshDirty;
        SeededRng _rng;
        int _alive;

        /// <summary>Cards still falling (0 once the shower has fallen out).</summary>
        public int Alive => _alive;
        /// <summary>The shower has ended and every card has landed.</summary>
        public bool Finished => _n > 0 && _alive == 0;

        /// <summary>
        /// Start a shower above `centre` (the dais) under `parent`, in two colours, from a seeded
        /// stream (`salt` = CupSalts.Confetti). `life` seconds of recycling, then it falls out.
        /// </summary>
        public static CupConfetti Create(Transform parent, Vector3 centre, Color primary, Color secondary, uint seed, uint salt,
                                         int count = CupTuning.ConfettiCount, float height = CupTuning.ConfettiHeight,
                                         float life = DefaultLife)
        {
            var go = new GameObject("CupConfetti");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;   // vertices are written in WORLD space
            var c = go.AddComponent<CupConfetti>();
            c.Build(centre, primary, secondary, new SeededRng(seed).Fork(salt), Mathf.Max(1, count), height, Mathf.Max(0f, life));
            return c;
        }

        void Build(Vector3 centre, Color primary, Color secondary, SeededRng rng, int count, float height, float life)
        {
            _centre = centre;
            _life = life;
            _elapsed = 0f;
            _rng = rng;
            _n = count;
            _alive = count;
            _pos = new Vector3[count];
            _prev = new Vector3[count];
            _phase = new float[count];
            _spinRate = new float[count];
            _spin = new float[count];
            _tiltPhase = new float[count];
            _second = new bool[count];
            _dead = new bool[count];
            for (int i = 0; i < count; i++)
            {
                Spawn(i, height, true);
                _phase[i] = rng.Range(0f, Mathf.PI * 2f);
                _spinRate[i] = rng.Range(-SpinMax, SpinMax);
                _spin[i] = rng.Range(0f, 360f);
                _tiltPhase[i] = rng.Range(0f, Mathf.PI * 2f);
                _second[i] = (i & 1) == 1;   // an even split of the two colours, interleaved
            }

            // The colour strip: primary on the left half, secondary on the right, point filtered.
            _tex = new Texture2D(2, 1, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.SetPixels(new[] { primary, secondary });
            _tex.Apply();
            _mat = Make.MatTex(_tex, 0.15f);

            // 8 vertices per card (4 per side), 12 indices per card (2 triangles per side).
            _vtx = new Vector3[count * 8];
            _nrm = new Vector3[count * 8];
            var uv = new Vector2[count * 8];
            var tri = new int[count * 12];
            for (int i = 0; i < count; i++)
            {
                float u = _second[i] ? 0.75f : 0.25f;
                for (int k = 0; k < 8; k++) uv[i * 8 + k] = new Vector2(u, 0.5f);
                int v = i * 8, t = i * 12;
                // Front face (0 1 2, 0 2 3) and the back face on the second four (reverse winding).
                tri[t + 0] = v + 0; tri[t + 1] = v + 1; tri[t + 2] = v + 2;
                tri[t + 3] = v + 0; tri[t + 4] = v + 2; tri[t + 5] = v + 3;
                tri[t + 6] = v + 4; tri[t + 7] = v + 6; tri[t + 8] = v + 5;
                tri[t + 9] = v + 4; tri[t + 10] = v + 7; tri[t + 11] = v + 6;
            }
            Fill();
            _mesh = new Mesh();
            _mesh.name = "CupConfetti";
            _mesh.MarkDynamic();
            // Vertices BEFORE the triangles (HairSim's rule): Unity rejects indices against an
            // empty vertex buffer and the whole shower would be invisible.
            _mesh.vertices = _vtx;
            _mesh.normals = _nrm;
            _mesh.uv = uv;
            _mesh.triangles = tri;
            // Fixed bounds (assigning vertices never recalculates them): the spread plus room for
            // the flutter's drift, so the renderer is never culled while cards are still in view.
            _mesh.bounds = new Bounds(_centre + Vector3.up * ((height + SpawnBand) * 0.5f),
                                      new Vector3(SpreadRadius * 2f + 8f, height + SpawnBand + 2f, SpreadRadius * 2f + 8f));

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var owner = gameObject.AddComponent<GeneratedMeshOwner>();
            owner.Mesh = _mesh;
            owner.Tex = _tex;
            _meshDirty = true;
        }

        /// <summary>Put a card back at the top: a random spot in the spread, a random height in the band, no velocity.</summary>
        void Spawn(int i, float height, bool first)
        {
            float a = _rng.Range(0f, Mathf.PI * 2f);
            float rad = Mathf.Sqrt(_rng.Next01()) * SpreadRadius;   // uniform over the disc
            float y = height + _rng.Range(0f, SpawnBand);
            var p = _centre + new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad);
            _pos[i] = p;
            // A little initial sideways motion so the first cards do not all start dead still.
            float vx = _rng.Range(-0.4f, 0.4f), vz = _rng.Range(-0.4f, 0.4f);
            _prev[i] = p - new Vector3(vx, 0f, vz) * 0.02f;
            _dead[i] = false;
            if (!first) _spin[i] = _rng.Range(0f, 360f);
        }

        void Update()
        {
            if (_pos == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _elapsed += dt;
            bool recycling = _elapsed < _life;
            float damp = Mathf.Exp(-Drag * dt);
            float gStep = -Gravity * dt * dt;
            float t = _elapsed;
            for (int i = 0; i < _n; i++)
            {
                if (_dead[i]) continue;
                // Verlet: the velocity is the last step, damped by the drag; gravity and the flutter
                // are accelerations applied as dt^2 steps.
                Vector3 vel = (_pos[i] - _prev[i]) * damp;
                _prev[i] = _pos[i];
                float ph = _phase[i];
                float fx = Mathf.Sin(t * FlutterRate + ph);
                float fz = Mathf.Cos(t * FlutterRate * 0.73f + ph * 1.7f);
                _pos[i] += vel;
                _pos[i] += new Vector3(fx * FlutterAccel * dt * dt, gStep, fz * FlutterAccel * dt * dt);
                _spin[i] += _spinRate[i] * dt;
                if (_pos[i].y <= 0.015f)
                {
                    if (recycling) Spawn(i, CupTuning.ConfettiHeight, false);
                    else
                    {
                        // Landed after the shower ended: gone (a zero-size card, never drawn again).
                        _dead[i] = true;
                        _alive--;
                    }
                }
            }
            _meshDirty = true;
        }

        void LateUpdate()
        {
            if (!_meshDirty || _mesh == null) return;
            _meshDirty = false;
            Fill();
            _mesh.vertices = _vtx;
            _mesh.normals = _nrm;
        }

        /// <summary>Write every card's eight vertices from its position, spin and tilt.</summary>
        void Fill()
        {
            float t = _elapsed;
            float hw = QuadW * 0.5f, hh = QuadH * 0.5f;
            for (int i = 0; i < _n; i++)
            {
                int v = i * 8;
                if (_dead[i])
                {
                    for (int k = 0; k < 8; k++) { _vtx[v + k] = _pos[i]; _nrm[v + k] = Vector3.up; }
                    continue;
                }
                // The card's plane: e1 horizontal along the spin yaw, e2 perpendicular to it, tilted
                // out of the horizontal by a swinging angle (the card sees-saws as it falls).
                float yaw = _spin[i] * Mathf.Deg2Rad;
                float tilt = Mathf.Sin(t * 1.9f + _tiltPhase[i]) * TiltMax * Mathf.Deg2Rad;
                float sy = Mathf.Sin(yaw), cy = Mathf.Cos(yaw);
                float st = Mathf.Sin(tilt), ct = Mathf.Cos(tilt);
                Vector3 e1 = new Vector3(cy, 0f, -sy);
                Vector3 e2 = new Vector3(sy * st, ct, cy * st);   // perpendicular to e1 (dot = 0)
                Vector3 n = Vector3.Cross(e1, e2);
                Vector3 c = _pos[i];
                Vector3 a = c - e1 * hw - e2 * hh;
                Vector3 b = c + e1 * hw - e2 * hh;
                Vector3 d = c + e1 * hw + e2 * hh;
                Vector3 e = c - e1 * hw + e2 * hh;
                _vtx[v + 0] = a; _vtx[v + 1] = b; _vtx[v + 2] = d; _vtx[v + 3] = e;
                _vtx[v + 4] = a; _vtx[v + 5] = b; _vtx[v + 6] = d; _vtx[v + 7] = e;
                _nrm[v + 0] = n; _nrm[v + 1] = n; _nrm[v + 2] = n; _nrm[v + 3] = n;
                _nrm[v + 4] = -n; _nrm[v + 5] = -n; _nrm[v + 6] = -n; _nrm[v + 7] = -n;
            }
        }

        /// <summary>Stop recycling now (the cards in the air fall out).</summary>
        public void Stop()
        {
            _life = 0f;
        }

        void OnDestroy()
        {
            // The mesh and the texture go with the GeneratedMeshOwner; the material is ours.
            if (_mat != null) Destroy(_mat);
            _mat = null;
            _mesh = null;
            _tex = null;
        }
    }
}
