using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// AG4 "Steering Display" on Drood 2.0 (off by default, <see cref="Drood2ActivationGroups"/>):
    /// the mouse steering's display (<see cref="DroodSteeringOverlay"/>) in three steps, each press
    /// the next: switched on, the full display; pressed again (AG4 stays on), only the dead band;
    /// pressed once more, off. The game only switches a group on and off, so the second press's
    /// switch-off is turned back on here. Its key works like the parachute's AG1 key, read in the
    /// drood's input step (droods ignore activation-group keys, <see cref="DroodKeys"/>).
    /// </summary>
    internal static class DroodSteeringDisplay
    {
        internal const int ActivationGroup = 4;
        internal const string Name = "Steering Display";

        // AG4's keys when the game has no keyboard binding for it.
        private static readonly KeyCode[] Keys = { KeyCode.Alpha4, KeyCode.Keypad4 };

        private static bool _deadbandOnly;
        private static bool _lastOn;
        private static EvaScript _lastEva;

        /// <summary>Anything of the display is shown.</summary>
        internal static bool On(EvaScript eva) => eva?.PartScript?.CommandPod?.GetActivationGroupState(ActivationGroup) ?? false;

        /// <summary>Only the dead band is shown.</summary>
        internal static bool DeadbandOnly => _deadbandOnly;

        /// <summary>Game-loop Update (the player's drood): the AG4 key, and the steps from AG4's switches.</summary>
        internal static void Step(EvaScript eva)
        {
            var pod = eva?.PartScript?.CommandPod;
            if (pod == null || !eva.IsPlayerCraft)
            {
                return;
            }

            if (eva != _lastEva)
            {
                // Another drood (or a new flight): its group as it is, the full display.
                _lastEva = eva;
                _lastOn = On(eva);
                _deadbandOnly = false;
            }

            var inputs = Assets.Scripts.Game.Instance?.Inputs;
            if (DroodKeys.CanUseKeys(eva) && pod.Controls != null
                && DroodKeys.BindingDown(inputs?.ActivationGroup4, Keys)
                && inputs?.ActivationGroup4?.GetButtonDownIfEnabled() != true)
            {
                pod.Controls.ToggleActivationGroup(ActivationGroup);
            }

            var on = On(eva);
            if (on == _lastOn)
            {
                return;
            }

            if (!on && !_deadbandOnly)
            {
                // The second press: on again, the dead band only.
                _deadbandOnly = true;
                pod.SetActivationGroupState(ActivationGroup, true);
                on = true;
            }
            else
            {
                _deadbandOnly = false;
            }

            _lastOn = on;
            DroodLog.Debug($"Steering display: {(on ? _deadbandOnly ? "dead band only" : "full" : "off")}.");
        }
    }
}
