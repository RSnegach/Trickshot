using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The FIFA-style penalty camera (design 7.9): behind the taker on the ball-to-goal axis, close and
    /// low, with the vertical FOV SOLVED from the camera-to-goal distance and the window's aspect so
    /// the goal is big in the frame - the posts at about <see cref="CupTuning.PenaltyCamPostFrac"/>
    /// and 1 - that of the frame width when the taker looks at the goal centre.
    ///
    /// Aiming is unchanged: GameCamera's yaw/pitch are still the aim (SetPieceTaker.LookAimPoint
    /// from the ball spot), and the mouse looks around as normal within the clamp the rig installs
    /// (yaw +-PenaltyCamYawLimit about the goal, pitch PenaltyCamPitchMin..Max). This helper only
    /// answers two questions each frame: where the camera stands (latched once per kick) and how
    /// it is pointed / how wide it sees for the current look.
    ///
    /// Pointing: the camera's forward passes THROUGH the aim point on the goal plane, so with no
    /// reticle the screen centre IS where the shot goes - exactly the feel of the follow camera,
    /// whose aim ray runs parallel to its forward. Running the forward through the aim point (rather
    /// than parallel to the aim ray) is what lets the camera stand a little to the side of the axis
    /// without the aim drifting off centre by the parallax.
    ///
    /// Why a side offset at all: a run-up of <see cref="CupTuning.RunUpDistance"/> (3 m) puts the
    /// taker's charging stance at exactly <see cref="CupTuning.PenaltyCamBack"/> (3 m) behind the
    /// ball - inside the camera. The rig latches the stand-off at least <see cref="MinBehindTaker"/>
    /// behind the taker, and <see cref="SideOffset"/> steps the camera off his shoulder so his back
    /// does not fill the middle of the frame (where the goal is) for the whole charge. Both are
    /// (tune) values, as the design flags the whole placement.
    ///
    /// The FOV solve honours the post framing first, then WIDENS if that framing would drop the
    /// ball out of the bottom of the frame in the reference pose (looking at the goal centre): a
    /// penalty camera that cannot see the ball is broken however big the goal looks. With the
    /// design's 3 m / 1.5 m placement the ball rule wins at every aspect (the ball sits ~24 deg
    /// under the goal line from that close), which lands the posts nearer 35% / 65% than 11% / 89%;
    /// a lower, further camera (e.g. 6 m back, 0.9 m high) lets the post rule win. Worked numbers
    /// are in the C2 build report; the levers are the CupTuning constants and the two here.
    /// </summary>
    public sealed class CupPenaltyCam
    {
        // ---- local tunables (tune) that CupTuning does not carry --------------------------------
        /// <summary>The camera stands this far to the side of the ball-to-goal axis (m), off the taker's shoulder.</summary>
        public const float SideOffset = 0f;   // CENTRED on the ball-to-goal axis (owner: an offset made left/right looks asymmetric)
        /// <summary>
        /// The stand-off behind the ball is at least this far behind the taker's start mark (m).
        /// 3.5: the first pass's 1.0 put the camera a metre behind the taker's head, which filled
        /// the right third of the frame as a flesh-coloured blob for the whole charge (seen in
        /// play mode); from 3.5 m his whole body fits under the solved FOV, off to one side.
        /// </summary>
        public const float MinBehindTaker = 4f;   // 4 m behind the taker at his run-up mark = 7 m behind the ball
        /// <summary>Camera height (m): high enough that the line to the goal clears the taker's head, so he never blocks the goal.</summary>
        public const float CamHeight = 2.4f;
        /// <summary>The ball must sit at least this fraction of the frame height above the bottom edge (reference pose).</summary>
        public const float BallMarginFrac = 0.08f;
        /// <summary>Sanity range for the solved vertical FOV (deg).</summary>
        public const float MinFov = 18f;
        public const float MaxFov = 80f;
        /// <summary>Rotation smoothing (1/s): high enough that the aim never lags the mouse, low enough to hide frame jitter.</summary>
        public const float TurnRate = 22f;

        // ---- latched per kick -----------------------------------------------------------------
        /// <summary>Latch has run for this kick.</summary>
        public bool Latched { get; private set; }
        /// <summary>Where the camera stands for this kick (world).</summary>
        public Vector3 Position { get; private set; }
        /// <summary>Yaw (deg) from the ball to the goal centre: the look-clamp centre and the starting yaw.</summary>
        public float YawToGoal { get; private set; }
        /// <summary>Starting pitch (deg, GameCamera convention: + = down) that aims the ray from the ball at the goal centre's half height.</summary>
        public float PitchToGoal { get; private set; }
        /// <summary>The ball spot the aim ray starts from.</summary>
        public Vector3 BallSpot { get; private set; }
        /// <summary>The goal centre (ground level) the framing is solved against.</summary>
        public Vector3 GoalCenter { get; private set; }
        /// <summary>The plane the aim ray is intersected with (the goal line).</summary>
        public float GoalPlaneZ { get; private set; }
        /// <summary>The last solved vertical FOV (deg).</summary>
        public float Fov { get; private set; }
        /// <summary>Reference-pose diagnostics from the last Solve: where the OUTER post lands as a fraction of the frame width (0 = left edge).</summary>
        public float OuterPostFrac { get; private set; }

        Quaternion _rot;
        bool _rotValid;

        /// <summary>
        /// Place the camera for a kick. <paramref name="takerPos"/> is where the taker STANDS to
        /// charge (his run-up mark), so the camera can stay behind him; <paramref name="leftFooted"/>
        /// mirrors the side offset so the camera is off his non-kicking shoulder.
        /// </summary>
        public void Latch(Vector3 ballSpot, Vector3 goalCenter, Vector3 takerPos, bool leftFooted)
        {
            BallSpot = ballSpot;
            GoalCenter = goalCenter;
            GoalPlaneZ = goalCenter.z;

            Vector3 axis = goalCenter - ballSpot;
            axis.y = 0f;
            if (axis.sqrMagnitude < 1e-4f) axis = Vector3.forward;
            axis.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, axis);

            // Behind the ball by the design distance, but never inside the taker: at least
            // MinBehindTaker behind his charging mark along the axis.
            float takerBehind = Vector3.Dot(ballSpot - takerPos, axis);
            float back = Mathf.Max(CupTuning.PenaltyCamBack, takerBehind + MinBehindTaker);
            float side = leftFooted ? SideOffset : -SideOffset;   // a right-footer: camera off his left shoulder
            Position = ballSpot - axis * back + right * side + Vector3.up * CamHeight;

            YawToGoal = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg;
            float dist = Vector3.Dot(goalCenter - ballSpot, axis);
            float aimY = SimConfig.GoalHeight * 0.5f;
            // GameCamera pitch is +down, and the aim ray starts at the ball: looking UP at the
            // goal's half height is a negative pitch.
            PitchToGoal = -Mathf.Atan2(aimY - ballSpot.y, Mathf.Max(0.5f, dist)) * Mathf.Rad2Deg;

            Latched = true;
            _rotValid = false;
            Solve(16f / 9f);
        }

        /// <summary>The rotation of the reference pose: the camera looking at the goal centre's half height.</summary>
        public Quaternion ReferenceRotation()
        {
            Vector3 aimRef = GoalCenter + Vector3.up * (SimConfig.GoalHeight * 0.5f);
            Vector3 d = aimRef - Position;
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            return Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        /// <summary>
        /// Solve the vertical FOV for the current aspect (call every frame; the window can resize).
        /// Post rule: in the reference pose the outer post sits PenaltyCamPostFrac of the width in
        /// from its edge. Ball rule: the ball centre sits at least BallMarginFrac of the height above
        /// the bottom edge. The wider of the two wins; the result is clamped to MinFov..MaxFov.
        /// </summary>
        public float Solve(float aspect)
        {
            if (!Latched) return Fov;
            if (aspect < 0.5f || float.IsNaN(aspect) || float.IsInfinity(aspect)) aspect = 16f / 9f;
            Quaternion inv = Quaternion.Inverse(ReferenceRotation());
            float halfW = SimConfig.GoalWidth * 0.5f;
            float postY = SimConfig.GoalHeight * 0.5f;

            // Posts in VIEW space of the reference pose; the larger |x/z| is the outer post.
            float maxSx = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 post = new Vector3(GoalCenter.x + s * halfW, postY, GoalCenter.z);
                Vector3 v = inv * (post - Position);
                if (v.z <= 0.05f) continue;
                float sx = Mathf.Abs(v.x / v.z);
                if (sx > maxSx) maxSx = sx;
            }
            float postFrac = Mathf.Clamp(CupTuning.PenaltyCamPostFrac, 0.01f, 0.45f);
            float tanHalfH = maxSx > 0f ? maxSx / (1f - 2f * postFrac) : Mathf.Tan(30f * Mathf.Deg2Rad);
            float tanHalfV = tanHalfH / aspect;

            // The ball, same view space: how far below the axis it sits, as a tangent.
            Vector3 ball = BallSpot + Vector3.up * SimConfig.BallRadius;
            Vector3 b = inv * (ball - Position);
            if (b.z > 0.05f)
            {
                float below = -b.y / b.z;   // positive when the ball is under the axis
                if (below > 0f)
                {
                    float need = below / Mathf.Max(0.05f, 1f - 2f * BallMarginFrac);
                    if (need > tanHalfV) tanHalfV = need;
                }
            }

            float fov = 2f * Mathf.Atan(tanHalfV) * Mathf.Rad2Deg;
            Fov = Mathf.Clamp(fov, MinFov, MaxFov);

            // Diagnostics: where the outer post actually lands with the FOV in force (0..1 of width).
            float tanHalfHFinal = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * aspect;
            OuterPostFrac = tanHalfHFinal > 1e-4f ? 0.5f - 0.5f * Mathf.Clamp01(maxSx / tanHalfHFinal) : 0.5f;
            return Fov;
        }

        /// <summary>
        /// Point the camera for this frame's look: stand at <see cref="Position"/>, forward through
        /// the aim point (SetPieceTaker.LookAimPoint from the ball at GameCamera's yaw/pitch), FOV
        /// as solved. <paramref name="dt"/> is unscaled; pass 0 to snap.
        /// </summary>
        public void Apply(Camera cam, float yaw, float pitch, float dt)
        {
            if (cam == null || !Latched) return;
            Solve(cam.aspect);
            Vector3 aim = SetPieceTaker.LookAimPoint(BallSpot, yaw, pitch, GoalPlaneZ);
            Vector3 d = aim - Position;
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
            if (!_rotValid || dt <= 0f) { _rot = want; _rotValid = true; }
            else _rot = Quaternion.Slerp(_rot, want, 1f - Mathf.Exp(-TurnRate * dt));
            cam.transform.position = Position;
            cam.transform.rotation = _rot;
            cam.fieldOfView = Fov;
        }

        /// <summary>Forget the kick (the next Latch snaps rather than eases the rotation).</summary>
        public void Clear()
        {
            Latched = false;
            _rotValid = false;
        }

        /// <summary>
        /// The GameCamera pitch clamp for this format's aim, in GameCamera's +down convention. The
        /// design writes the range as -5..+20 with UP positive (5 deg under the goal line to well
        /// over the bar - the top corners need about 12 deg up from the spot), so the sign flips.
        /// </summary>
        public static void PitchClamp(out float min, out float max)
        {
            min = -CupTuning.PenaltyCamPitchMax;   // looking up (over the bar)
            max = -CupTuning.PenaltyCamPitchMin;   // looking slightly down (the goal line)
        }
    }
}
