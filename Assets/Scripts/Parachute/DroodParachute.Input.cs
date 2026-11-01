using UnityEngine;

namespace Drood2
{
    // Input: the AG1 key for droods.
    public partial class DroodParachute
    {
        // AG1's keys when the game has no keyboard binding for it (one array, not one per frame).
        private static readonly KeyCode[] ActivationGroup1Keys = { KeyCode.Alpha1, KeyCode.Keypad1 };

        /// <summary>
        /// Keyboard AG1 for Drood 2.0. The game ignores activation-group keys on droods (their
        /// action map is disabled, see <see cref="DroodKeys"/>); the AG panel button still works.
        /// The player's own AG1 binding is read instead, and skipped when the game's own AG1
        /// input fired, so a press never toggles twice. Called from the modifier's game-loop
        /// Update, so the key does nothing while paused.
        /// </summary>
        internal void InputStep()
        {
            DroodMouseSteering.HandleToggle(_eva);

            var controls = _part?.CommandPod?.Controls;
            var inputs = Assets.Scripts.Game.Instance?.Inputs;
            if (controls == null || !DroodKeys.CanUseKeys(_eva)
                || !DroodKeys.BindingDown(inputs?.ActivationGroup1, ActivationGroup1Keys)
                || inputs?.ActivationGroup1?.GetButtonDownIfEnabled() == true)
            {
                return;
            }

            controls.ToggleActivationGroup(ActivationGroup);
        }
    }
}
