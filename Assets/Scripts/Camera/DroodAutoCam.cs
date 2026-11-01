using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// AG3 "AutoCam" on a Drood 2.0 with the Drop Zone Challenge on (on by default,
    /// <see cref="Drood2ActivationGroups"/>; its only automatic view change is the challenge's):
    /// while on, the drop zone switches the view to Drood Chase in the arch; off, it never does. A view taken out of the list while shown still hands
    /// over to the drood's own view (<see cref="DroodChaseCameraMode"/>): the game needs one.
    /// Its key works like the parachute's AG1 key (<see cref="DroodParachute"/>), from the drop
    /// zone's step (only with the challenge on).
    /// </summary>
    internal static class DroodAutoCam
    {
        internal const int ActivationGroup = 3;
        internal const string Name = "AutoCam";

        // AG3's keys when the game has no keyboard binding for it.
        private static readonly KeyCode[] Keys = { KeyCode.Alpha3, KeyCode.Keypad3 };

        /// <summary>AutoCam is on for <paramref name="eva"/>.</summary>
        internal static bool IsOn(EvaScript eva) => eva?.PartScript?.CommandPod?.GetActivationGroupState(ActivationGroup) ?? true;

        /// <summary>Game-loop Update (the drood's input step): the AG3 key toggles AutoCam.</summary>
        internal static void HandleKey(EvaScript eva)
        {
            var controls = eva?.PartScript?.CommandPod?.Controls;
            var inputs = Assets.Scripts.Game.Instance?.Inputs;
            if (controls == null || !DroodKeys.CanUseKeys(eva)
                || !DroodKeys.BindingDown(inputs?.ActivationGroup3, Keys)
                || inputs?.ActivationGroup3?.GetButtonDownIfEnabled() == true)
            {
                return;
            }

            controls.ToggleActivationGroup(ActivationGroup);
            DroodLog.Debug($"AutoCam {(IsOn(eva) ? "on" : "off")}.");
        }
    }
}
