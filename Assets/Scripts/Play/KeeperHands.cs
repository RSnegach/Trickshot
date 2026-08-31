using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A goalkeeper's hands: gathering a ball, holding it, and distributing it. Shared by the
    /// striker-mode Goalkeeper and the match AI keeper so both handle the ball the same way.
    ///
    /// Holding reuses the DRIBBLE carry primitive rather than inventing a second one: ball/keeper
    /// collision is suspended (Dribble.SetCarryCollision), the ball is registered as that body's
    /// carry so the keeper's own limbs are not read as strikes on it, and it is pinned to his chest
    /// each frame. A global strike suppress is refreshed while he holds, so an attacker cannot
    /// toe-poke it out of his gloves.
    ///
    /// A HARD shot is never caught, only parried: claiming requires the ball to be slow enough to
    /// gather, so a rocket still has to be dived at and beaten away. That is the difference between
    /// a keeper and a vacuum.
    ///
    /// The hold ABORTS if the ball is teleported out from under it - a mode reset, a kickoff, the
    /// stuck-ball watchdog - so nothing can leave a keeper welded to a ball that has moved on.
    /// </summary>
    public class KeeperHands
    {
        BallController _ball;
        ActiveRagdoll _body;
        bool _holding;
        float _hold;        // seconds held so far
        float _cooldown;    // after a release, before he can gather again
        Vector3 _pin;

        public bool Holding => _holding;
        public float HeldFor => _hold;

        public void Init(BallController ball, ActiveRagdoll body)
        {
            _ball = ball;
            _body = body;
        }

        public void Tick(float dt)
        {
            if (_cooldown > 0f) _cooldown -= dt;
        }

        /// <summary>Where a held ball sits: just in front of the chest, clear of the torso.</summary>
        public Vector3 Chest()
        {
            if (_body == null) return Vector3.zero;
            var t = _body.Phys(Bone.Torso);
            Vector3 p = t != null ? t.position
                      : (_body.Pelvis != null ? _body.Pelvis.position + Vector3.up * 0.4f : Vector3.zero);
            return p + _body.FacingRotation * Vector3.forward * SimConfig.KeeperHoldForward;
        }

        /// <summary>
        /// Can he gather the ball right now? A CATCH IS THE EXCEPTION. Four things must all hold:
        /// the ball is slow enough FOR THE HEIGHT IT ARRIVES AT, it is at his hands, it is in FRONT
        /// of him, and it is not running away from him. Anything else is a parry (TryParry), which
        /// is what a keeper actually does with a struck ball.
        /// </summary>
        public bool CanClaim(float ability)
        {
            if (_holding || _cooldown > 0f || _ball == null || _body == null) return false;
            // In someone's feet: he has to challenge for it, not pluck it out of a carry.
            if (_ball.DribbleHold) return false;
            float reach = SimConfig.KeeperClaimReach * Mathf.Lerp(0.75f, 1.25f, ability);
            // CYLINDER, not a sphere around the chest. A keeper gathers at his boots and above his
            // head just as readily as at chest height, and a sphere centred on the chest misses a
            // shot rolling in at his feet by the best part of a metre. The vertical envelope scales
            // separately (1.4x) so tightening the radius does not also cost him the overhead grab.
            Vector3 c = Chest(), b = _ball.transform.position;
            Vector3 flat = new Vector3(b.x - c.x, 0f, b.z - c.z);
            if (flat.magnitude > reach) return false;
            if (b.y < -0.2f || b.y > c.y + reach * 1.4f) return false;
            // SLOW ENOUGH FOR THIS HEIGHT. The ceiling is not flat: a ball at his chest is the one
            // he can get two hands and his whole body behind, so it is the one he can hold hardest.
            // At his boots or above his head the same pace beats him and he parries instead. The
            // ability multiplier wraps the result rather than the base, or a strong keeper would
            // lose ceiling by the ball arriving at the ideal height.
            float dy = Mathf.Abs(b.y - c.y);
            float chest = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                SimConfig.KeeperClaimChestBand,
                SimConfig.KeeperClaimChestBand + SimConfig.KeeperClaimChestFade, dy));
            float ceiling = Mathf.Lerp(SimConfig.KeeperClaimMaxSpeed, SimConfig.KeeperClaimChestSpeed, chest);
            if (_ball.Speed > ceiling * Mathf.Lerp(0.55f, 1.6f, ability)) return false;
            // IN FRONT. He catches with his hands, so a ball level with his shoulder or behind him
            // is not a catch. A sharper keeper claims across a wider cone.
            if (flat.sqrMagnitude > 0.0004f)
            {
                Vector3 fwd = _body.FacingRotation * Vector3.forward; fwd.y = 0f;
                float frontDot = Mathf.Lerp(0.55f, SimConfig.KeeperClaimFrontDot, Mathf.Clamp01(ability));
                if (fwd.sqrMagnitude > 1e-4f
                    && Vector3.Dot(flat.normalized, fwd.normalized) < frontDot) return false;
                // LEAVING HIM. Without this he vacuums up his own rebound as it bounces clear.
                if (_ball.Rb != null
                    && Vector3.Dot(_ball.Rb.linearVelocity, (b - c).normalized) > SimConfig.KeeperClaimMaxRecede)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The REJECTED claim. A ball he cannot gather is beaten away with a real impulse instead of
        /// being left to bobble off the ragdoll's capsules on restitution alone (0.55 combine-Maximum,
        /// and ~0 on the slick foot bones - which is how a refused shot used to die at his boots or
        /// trickle over the line).
        ///
        /// Fires ONLY on a collision PhysX has already logged against this body, so it is never
        /// telekinesis - and because SaveWatch reads that same log, a parried shot still calls out as
        /// a SAVE. The impulse uses the LOGGED impact speed, not the live velocity, which the bounce
        /// has already cut.
        ///
        /// Direction is his own facing forward - a keeper always faces OUT toward the pitch, at both
        /// ends, AI or human in his look cone - weighted to the side the ball came in on so it goes
        /// WIDE rather than back to the shooter, plus a little lift to clear the turf.
        ///
        /// Shares _cooldown with the claim, so one parry is one touch: he cannot pinball it frame
        /// after frame, and he cannot gather the ball he has just pushed clear.
        /// </summary>
        public bool TryParry(float ability)
        {
            if (_holding || _cooldown > 0f || _ball == null || _body == null) return false;
            if (_ball.DribbleHold) return false;
            if (!_ball.BodyTouchedSince(_body, Time.time - SimConfig.KeeperParryTouchWindow,
                                        out float impact, out float _)) return false;
            Vector3 c = Chest(), b = _ball.transform.position;
            Vector3 away = b - c;
            if (away.magnitude > SimConfig.KeeperParryReach) return false;   // stale log entry, not his touch

            Vector3 fwd = _body.FacingRotation * Vector3.forward;
            Vector3 right = _body.FacingRotation * Vector3.right;
            float side = Vector3.Dot(away, right) >= 0f ? 1f : -1f;
            Vector3 dir = (fwd + right * (side * SimConfig.KeeperParrySide)
                               + Vector3.up * SimConfig.KeeperParryUp).normalized;
            float power = (impact * SimConfig.KeeperParryKeep + SimConfig.KeeperParryPush)
                          * Mathf.Lerp(0.7f, 1.3f, Mathf.Clamp01(ability));
            _ball.KickTo(dir * power, _body);   // also suppresses his own limbs re-striking it
            _cooldown = SimConfig.KeeperParryCooldown;
            return true;
        }

        public void Claim()
        {
            if (_ball == null || _body == null || _holding) return;
            _holding = true;
            _hold = 0f;
            Dribble.SetCarryCollision(_ball, _body, true);
            _ball.SetDribbleCarrier(_body);
            _pin = Chest();
            Pin();
        }

        /// <summary>
        /// Pin the ball to the chest for another frame. Returns false (and drops it) if the ball
        /// has been moved out from under the hold by something else.
        /// </summary>
        public bool Hold(float dt)
        {
            if (!_holding || _ball == null || _body == null) return false;
            if (Vector3.Distance(_ball.transform.position, _pin) > SimConfig.KeeperHoldBreak) { Drop(); return false; }
            _pin = Chest();
            Pin();
            _hold += dt;
            _ball.SuppressStrike(0.12f);   // nobody kicks it out of his gloves
            return true;
        }

        void Pin()
        {
            var rb = _ball.Rb;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.position = _pin;
            }
            _ball.transform.position = _pin;
        }

        /// <summary>Let go without playing it (knocked, reset, or interrupted).</summary>
        public void Drop()
        {
            if (!_holding) return;
            _holding = false;
            _hold = 0f;
            _cooldown = SimConfig.KeeperClaimCooldown;
            if (_ball != null && _body != null)
            {
                Dribble.SetCarryCollision(_ball, _body, false);
                if (_ball.DribbleCarrier == _body) _ball.SetDribbleCarrier(null);
            }
        }

        /// <summary>Play the held ball out to `aim`. Drops the hold first.</summary>
        public void Release(Vector3 aim, bool lofted, float ability)
        {
            if (!_holding || _ball == null) return;
            Vector3 from = _pin;
            Drop();
            _ball.ResetTo(from);
            Passing.Launch(_ball, aim, lofted, Mathf.Lerp(0.55f, 0.95f, ability), 1f, _body,
                           SimConfig.KeeperDistributeScatterDeg * (1f - ability),
                           SimConfig.KeeperDistributeWobble * (1f - ability));
        }
    }
}
