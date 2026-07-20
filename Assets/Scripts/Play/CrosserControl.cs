using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Human control for the crosser role. The crosser stands on the wing and picks WHERE the
    /// ball lands via the CrossMap overlay (M), then delivers with the pass buttons:
    ///   tap  Q/E = DRIVEN cross (low, fast)
    ///   hold Q/E = CHIPPED cross (high, floaty); longer hold floats it more
    /// (Q and E both cross; holding is what makes it a chip, matching the "hold = lofted"
    /// feel from scrimmage passing.) Aim + power reuse the existing Crosser.ServeNow launch.
    ///
    /// Host-authoritative: only the host's CrosserControl actually launches the ball (the
    /// Crosser it drives is the host's real one). A client's crosser body is a display puppet;
    /// its CrosserControl is not ticked, so it never serves - it only shows the host's result.
    ///
    /// Input comes through IStrikerInput, so this works for the local host crosser (GameInput)
    /// or a remote human crosser (NetInputSource fed from the wire), unchanged.
    /// </summary>
    public class CrosserControl : MonoBehaviour
    {
        IStrikerInput _input;
        Crosser _crosser;
        System.Func<bool> _mapOpen;    // is the cross-map overlay up (aiming, don't serve)
        System.Func<Vector3> _target;  // current chosen landing spot (from the map)

        float _groundCharge, _loftedCharge;   // hold time per pass button

        public void Init(IStrikerInput input, Crosser crosser, System.Func<bool> mapOpen, System.Func<Vector3> target)
        {
            _input = input;
            _crosser = crosser;
            _mapOpen = mapOpen;
            _target = target;
            if (_crosser != null) _crosser.AutoServe = false;   // human decides when to cross
        }

        // Host ticks this each frame. Charges on hold, serves on release: a bare tap is a
        // driven ball, a held press is a chip (charge scales the float). While the aim map is
        // open, swallow the release (you're placing the target, not serving).
        public void Tick()
        {
            if (_input == null || _crosser == null) return;

            bool aiming = _mapOpen != null && _mapOpen();

            if (_input.PassGroundHeld) _groundCharge += Time.deltaTime;
            if (_input.PassGroundReleased) { if (!aiming) Serve(_groundCharge); _groundCharge = 0f; }

            if (_input.PassLoftedHeld) _loftedCharge += Time.deltaTime;
            if (_input.PassLoftedReleased) { if (!aiming) Serve(_loftedCharge); _loftedCharge = 0f; }
        }

        // A tap (near-zero hold) drives it low; any real hold chips it, floatier with charge.
        void Serve(float held)
        {
            if (!_crosser.ReadyToServe) return;
            float charge = Mathf.Clamp01(held / SimConfig.CrossMaxCharge);
            bool chipped = held >= SimConfig.CrossTapMaxHold;    // held past the tap threshold = chip
            Vector3 target = _target != null ? _target() : SimConfig.ServeTarget;
            _crosser.ServeNow(target, chipped, charge, 0f);      // human crosser: no accuracy scatter
        }
    }
}
