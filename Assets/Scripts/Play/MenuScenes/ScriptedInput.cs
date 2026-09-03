using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// An IStrikerInput a menu scene WRITES instead of a device or the wire, so a choreography can
    /// drive a real Striker through the real controller code (jump, air pitch, leg raises, the
    /// slide) rather than posing bones by hand.
    ///
    /// Edges are derived exactly the way NetInputSource derives them: hold the intent for a frame,
    /// then Commit() rolls current into previous, so JumpPressed is true for exactly the frame after
    /// the caller first set Jump. The caller sets the held fields, calls Commit() once per frame
    /// BEFORE Striker.Tick, and Striker sees a device-shaped stream.
    ///
    /// Scroll is the one field that is NOT a held bit: Striker reads it per frame as an event
    /// (|value| > SimConfig.ScrollDeadzone is one AirPitchStep of lean), so the scene sets it on the
    /// frames it wants a step and it self-clears in Commit.
    /// </summary>
    public class ScriptedInput : IStrikerInput
    {
        // What the scene wants held THIS frame. Written directly by the choreography.
        public bool Jump, LegL, LegR, Sprint;
        public Vector2 MoveWish;
        public float ScrollWish;

        bool _pJump, _pLegL, _pLegR;
        bool _jump, _legL, _legR;
        float _scroll;

        /// <summary>Latch this frame's intent and roll the previous one. Call once per frame, before
        /// Striker.Tick, so every Pressed/Released edge lasts exactly one frame.</summary>
        public void Commit()
        {
            _pJump = _jump; _pLegL = _legL; _pLegR = _legR;
            _jump = Jump; _legL = LegL; _legR = LegR;
            _scroll = ScrollWish;
            ScrollWish = 0f;   // an event, not a held bit: one frame per step
        }

        /// <summary>Forget everything, including the edge history, so a re-run of the scene cannot
        /// see a phantom press from the last one.</summary>
        public void Clear()
        {
            Jump = LegL = LegR = Sprint = false;
            MoveWish = Vector2.zero; ScrollWish = 0f;
            _pJump = _pLegL = _pLegR = false;
            _jump = _legL = _legR = false;
            _scroll = 0f;
        }

        public Vector2 Move => MoveWish;
        public float Scroll => _scroll;
        public bool SprintHeld => Sprint;
        public bool CloseControlHeld => false;

        public bool JumpHeld => _jump;
        public bool JumpPressed => _jump && !_pJump;
        public bool JumpReleased => !_jump && _pJump;

        public bool LeftLegHeld => _legL;
        public bool RightLegHeld => _legR;

        // Click edges from the leg bits, exactly as the networked source derives them - which is
        // what lets a scripted slide arm (Striker.UpdateSit wants both click EDGES inside SitWindow,
        // not merely both bits held).
        public bool LeftClickPressed => _legL && !_pLegL;
        public bool RightClickPressed => _legR && !_pLegR;

        public bool ResetPressed => false;
        public bool PassGroundPressed => false;
        public bool PassLoftedPressed => false;
        public bool PassGroundHeld => false;
        public bool PassLoftedHeld => false;
        public bool PassGroundReleased => false;
        public bool PassLoftedReleased => false;
        public bool PassChipPressed => false;
        public bool PassChipHeld => false;
        public bool PassChipReleased => false;
        public bool Fresh => true;
        public int EmoteId => 255;
        public bool CrossPressed => false;
        public bool ThirdLegHeld => false;
    }
}
