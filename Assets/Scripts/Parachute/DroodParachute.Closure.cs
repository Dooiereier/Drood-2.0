using UnityEngine;

namespace Drood2
{
    // Ram-air opening malfunction: one opening in ClosureOdds (a player's drood only), one half of
    // the wing doesn't fill. From ClosureFromOpen of the opening that half is held stalled at
    // ClosedHalfStall (its stall's collapse, flutter, sink and the turn toward it) until cleared
    // by ClosurePumps brake pumps: both toggles pulled past PumpDeep and let back up under
    // PumpUp. Then the half flies again as from a stall (StallRecoverSeconds). While it is
    // closed the brakes reach all their travel at any speed (UpdateSteering).
    public partial class DroodParachute
    {
        private const int ClosureOdds = 12;
        private const float ClosureFromOpen = 0.7f;
        private const float ClosedHalfStall = 0.7f;
        private const int ClosurePumps = 2;
        private const float PumpDeep = 0.75f;
        private const float PumpUp = 0.25f;

        // The closed half (-1 left, 1 right, 0 none), the pumps done and whether one is down.
        private int _closedSide;
        private int _pumpsDone;
        private bool _pumpDown;

        /// <summary>A half is held closed (the brakes' reach is lifted meanwhile).</summary>
        private bool HalfClosed => _closedSide != 0;

        /// <summary>At line stretch: whether this opening has a closed half (one in ClosureOdds).</summary>
        private void RollClosure()
        {
            _closedSide = 0;
            _pumpsDone = 0;
            _pumpDown = false;
            if (!IsRamAir || !_eva.IsPlayerCraft || Random.Range(0, ClosureOdds) != 0)
            {
                return;
            }

            _closedSide = Random.value < 0.5f ? -1 : 1;
            Log($"Opening malfunction: the {(_closedSide < 0 ? "left" : "right")} half doesn't fill ({ClosurePumps} brake pumps clear it).");
        }

        /// <summary>
        /// After the stall step: the closed half held at ClosedHalfStall (once the wing is
        /// ClosureFromOpen open), and the brake pumps counted.
        /// </summary>
        private void StepClosure(bool landed, float dt)
        {
            if (!HalfClosed || landed)
            {
                return;
            }

            var pull = Mathf.Min(_leftPull, _rightPull);
            if (!_pumpDown && pull >= PumpDeep)
            {
                _pumpDown = true;
            }
            else if (_pumpDown && pull <= PumpUp)
            {
                _pumpDown = false;
                _pumpsDone++;
                if (_pumpsDone >= ClosurePumps)
                {
                    Log($"Opening malfunction cleared after {_pumpsDone} brake pumps.");
                    _closedSide = 0;
                    return;
                }
            }

            if (_inflate < ClosureFromOpen)
            {
                return;
            }

            if (_closedSide < 0)
            {
                _leftStall = Mathf.MoveTowards(_leftStall, ClosedHalfStall, dt / StallOnsetSeconds);
            }
            else
            {
                _rightStall = Mathf.MoveTowards(_rightStall, ClosedHalfStall, dt / StallOnsetSeconds);
            }
        }
    }
}
