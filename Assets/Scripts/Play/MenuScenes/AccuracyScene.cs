using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// ACCURACY: the player's body strikes a dead ball into a floating pop-up target, which flashes
    /// and disappears on the hit. The taker, the ball and the target are the only things in frame -
    /// there is no goal behind it, so the target hangs in the air the way the scoring discs do.
    /// </summary>
    public class AccuracyScene : DeadBallScene
    {
        const float TargetOutZ = 5.0f;
        static readonly Vector3 TargetAt = new Vector3(0.75f, 1.55f, TargetOutZ);

        AccuracyTarget _target;
        Vector3 _targetPos;

        protected override float HoldSeconds => 3.4f;

        // Straight at the disc: a flat, driven strike reads as a shot at a target.
        protected override Vector3 Aim => _targetPos;

        public override void Build()
        {
            BuildTaker(Vector3.zero);
            _targetPos = Origin + TargetAt;

            var go = new GameObject("MsTarget");
            go.transform.SetParent(Root, true);
            // The disc faces its own local Z; turn it back toward the taker so it reads as a face
            // rather than an edge.
            go.transform.rotation = Quaternion.identity;
            _target = go.AddComponent<AccuracyTarget>();
            _target.Spawn(_targetPos, 0.42f, new Color(1f, 0.82f, 0.29f), 2);

            // Stop the ball a little past the target instead of letting it run off the stage.
            BuildCatcher(new Vector3(30f, 8f, 0.2f), Origin + new Vector3(0f, 4f, TargetOutZ + 2.5f));
        }

        public override void Reset()
        {
            base.Reset();
            // Re-arm the disc: Spawn clears Hit, re-enables the trigger and shows it again.
            _target.Spawn(_targetPos, 0.42f, new Color(1f, 0.82f, 0.29f), 2);
        }

        public override void Freeze()
        {
            base.Freeze();
            // The target pulses and bobs from its own Update, which would keep moving on a frozen
            // panel; the component is the only thing that animates it.
            if (_target != null) _target.enabled = false;
        }

        public override void Thaw()
        {
            base.Thaw();
            if (_target != null) _target.enabled = true;
        }

        public override void Frame(out Vector3 camPos, out Vector3 lookAt, out float fov)
        {
            // Behind the taker's shoulder, so the ball, the flight and the target are all in shot.
            // Over his shoulder, close, with the target in the far half of the frame.
            // Side on, fitted to the whole flight: the taker at one end, the target at the other,
            // so the strike and the hit are both in the panel.
            fov = 46f;
            // The span is the RUN-UP START (behind the spot) through the target, not the spot
            // through the target - centring on the latter pushed the taker to the frame edge.
            // Bias toward the TAKER: he is the subject, the target is the payoff, and centring the
            // raw span put him hard against the frame edge.
            // Looking from +X so the taker reads at frame LEFT and the ball travels away to the
            // right; the centre sits at chest height so a standing body is not clipped at the top.
            float zA = -RunUpDist - 0.2f, zB = TargetOutZ + 0.3f;
            FitCamera(Origin + new Vector3(0.3f, 1.0f, Mathf.Lerp(zA, zB, 0.5f)),
                      new Vector3((zB - zA) * 0.5f, 1.15f, 0.3f),
                      new Vector3(1f, 0.22f, -0.10f), fov, PanelAspect, out camPos, out lookAt);
        }

        public override void Destroy()
        {
            if (_target != null) _target.OnHit = null;
            base.Destroy();
        }
    }
}
