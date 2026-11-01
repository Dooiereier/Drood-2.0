using Assets.Scripts.Craft.Parts.Modifiers;
using ModApi.Craft.Parts;

namespace Drood2
{
    /// <summary>
    /// Makes sure Drood 2.0 has 10 activation groups, with AG1 named "Parachute" where unnamed,
    /// AG4 "Steering Display" where unnamed,
    /// with the Drop Zone chosen AG3 "AutoCam" (on) where unnamed, and AG2 "Restart Challenge"
    /// (DroodDropZone, DroodSwoopCourse) with a challenge chosen, where unnamed or under an
    /// earlier name of the mod's; without a challenge those names are taken off again (AG2 and
    /// AG3 are the player's then).
    ///
    /// CommandPodScript.Get/SetActivationGroupState ignore groups beyond the length of
    /// CommandPodData.ActivationGroupStates. Drood 2.0's part XML already defines 10 groups,
    /// and the designer copies groups between the command pods of a craft (droods in saved
    /// crafts normally have 10), so this rarely changes anything. It covers a craft whose
    /// Drood 2.0 has fewer groups. Called from <see cref="Drood2Script"/>'s
    /// OnModifiersCreated, before anything reads the groups.
    /// </summary>
    internal static class Drood2ActivationGroups
    {
        private const int GroupCount = 10;
        private const string ChallengeName = "Restart Challenge";

        // AG2's earlier names, replaced.
        private static readonly string[] EarlierNames = { "Drop Zone", "Drop Challenge", "Swoop" };

        public static void Ensure(IPartScript part)
        {
            if (!(part?.CommandPod is CommandPodScript commandPod) || commandPod.Data == null)
            {
                return;
            }

            var states = commandPod.Data.ActivationGroupStates;
            var names = commandPod.Data.ActivationGroupNames;
            while (states.Count < GroupCount)
            {
                states.Add(false);
            }

            while (names.Count < states.Count)
            {
                names.Add(string.Empty);
            }

            if (string.IsNullOrEmpty(names[DroodParachute.ActivationGroup - 1]))
            {
                names[DroodParachute.ActivationGroup - 1] = "Parachute";
            }

            // The steering display: named where AG4 is unnamed (off: the player switches it on).
            if (string.IsNullOrEmpty(names[DroodSteeringDisplay.ActivationGroup - 1]))
            {
                names[DroodSteeringDisplay.ActivationGroup - 1] = DroodSteeringDisplay.Name;
            }


            var dropName = names[DroodDropZone.ActivationGroup - 1];
            var data = part.GetModifier<Drood2Script>()?.Data;
            var dropZone = data?.DropZone ?? false;
            var swoop = data?.Swoop ?? false;
            var challengeName = dropZone || swoop ? ChallengeName : string.Empty;
            if (string.IsNullOrEmpty(dropName) || dropName == ChallengeName || System.Array.IndexOf(EarlierNames, dropName) >= 0)
            {
                names[DroodDropZone.ActivationGroup - 1] = challengeName;
            }

            // AutoCam (the challenge's): named and switched on where AG3 is unnamed; taken off
            // again without the challenge (AG3 is the player's then).
            var autoCamName = names[DroodAutoCam.ActivationGroup - 1];
            if (dropZone && string.IsNullOrEmpty(autoCamName))
            {
                names[DroodAutoCam.ActivationGroup - 1] = DroodAutoCam.Name;
                states[DroodAutoCam.ActivationGroup - 1] = true;
            }
            else if (!dropZone && autoCamName == DroodAutoCam.Name)
            {
                names[DroodAutoCam.ActivationGroup - 1] = string.Empty;
                states[DroodAutoCam.ActivationGroup - 1] = false;
            }

            DroodLog.Debug($"Activation groups: challenge {(dropZone ? "Drop Zone" : swoop ? "Swoop" : "off")} (data {(data != null ? "there" : "missing")}); AG2 '{names[DroodDropZone.ActivationGroup - 1]}', AG3 '{names[DroodAutoCam.ActivationGroup - 1]}' {(states[DroodAutoCam.ActivationGroup - 1] ? "on" : "off")}.");
        }

        /// <summary>
        /// The flight's activation group panel shown again for the player's craft: it reads the
        /// names only when the craft becomes the player's (found while hidden too: it's closed
        /// until the player opens it).
        /// </summary>
        public static void RefreshPanel()
        {
            var craftNode = Assets.Scripts.Game.Instance?.FlightScene?.CraftNode as Assets.Scripts.Flight.Sim.CraftNode;
            var panel = UnityEngine.Object.FindObjectOfType<Assets.Scripts.Flight.UI.ActivationPanelController>(true);
            if (craftNode != null && panel != null)
            {
                panel.CraftNodeChanged(craftNode);
            }

            DroodLog.Debug($"Activation groups: panel {(panel != null ? "found" : "not found")}, the player's controls name AG3 '{craftNode?.Controls?.GetActivationGroupName(DroodAutoCam.ActivationGroup)}'.");
        }
    }
}
