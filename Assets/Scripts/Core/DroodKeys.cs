using System.Collections.Generic;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Input;
using Rewired;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Drood 2.0's keys: one guard for when a key may act, and the player's own bindings.
    ///
    /// Droods use the "FlightEva" input map; the game disables "FlightCraft", which holds the
    /// activation-group and translation-mode actions (FlightSceneScript.UpdateActiveControlMaps),
    /// so those actions never fire on a drood. Their keyboard bindings are looked up here in
    /// the input library (Rewired), including the disabled map, and read directly, so a rebound
    /// key (Settings → Controls) works for Drood 2.0 too. A game input's Id is its Rewired
    /// action name (GameInput's constructor). Without a binding, the fallback keys are used.
    /// </summary>
    internal static class DroodKeys
    {
        private static readonly List<ActionElementMap> Maps = new List<ActionElementMap>();
        private static readonly HashSet<string> WarnedMissing = new HashSet<string>();

        /// <summary>
        /// A Drood 2.0 key may act: this drood is the player-controlled craft and on EVA, the
        /// game isn't paused, no dialog is open and no text field has focus.
        /// </summary>
        public static bool CanUseKeys(EvaScript eva)
        {
            if (eva == null || !eva.IsPlayerCraft || !eva.EvaActive
                || eva.PartScript?.Data?.PartType?.Id != Drood2Part.PartTypeId)
            {
                return false;
            }

            var game = Assets.Scripts.Game.Instance;
            var ui = game?.UserInterface;
            return ui != null && !ui.IsTextInputFocused && !ui.AnyDialogsOpen
                && game.FlightScene?.TimeManager?.Paused != true;
        }

        /// <summary>
        /// The Drood 2.0 in control, if the player's craft is one on EVA and keys may act
        /// (<see cref="CanUseKeys"/>); otherwise null.
        /// </summary>
        public static EvaScript ControlledDrood()
        {
            var part = Assets.Scripts.Game.Instance?.FlightScene?.CraftNode?.CraftScript?.ActiveCommandPod?.Part?.PartScript;
            var eva = part?.GetModifier<EvaScript>();
            return CanUseKeys(eva) ? eva : null;
        }

        /// <summary>
        /// True on the frame a keyboard key bound to <paramref name="input"/> (with its modifier
        /// keys) went down, whether or not the action's map is enabled. Falls back to
        /// <paramref name="fallback"/> when the action has no keyboard binding.
        /// </summary>
        public static bool BindingDown(IGameInput input, params KeyCode[] fallback)
        {
            if (!Input.anyKeyDown)
            {
                return false;
            }

            if (input != null && FindKeyboardMaps(input.Id))
            {
                foreach (var map in Maps)
                {
                    if (Input.GetKeyDown(map.keyCode) && ModifiersHeld(map))
                    {
                        return true;
                    }
                }

                return false;
            }

            var name = input?.Id ?? "?";
            if (WarnedMissing.Add(name))
            {
                Debug.LogWarning($"[Drood 2.0] No keyboard binding found for '{name}'; using {string.Join("/", fallback)}.");
            }

            foreach (var key in fallback)
            {
                if (Input.GetKeyDown(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool FindKeyboardMaps(string actionName)
        {
            Maps.Clear();
            if (string.IsNullOrEmpty(actionName) || !ReInput.isReady)
            {
                return false;
            }

            var action = ReInput.mapping.GetAction(actionName);
            var player = ReInput.players.GetPlayer(0);
            if (action == null || player == null)
            {
                return false;
            }

            foreach (var map in player.controllers.maps.ElementMapsWithAction(ControllerType.Keyboard, action.id, false))
            {
                if (map != null && map.keyCode != KeyCode.None)
                {
                    Maps.Add(map);
                }
            }

            return Maps.Count > 0;
        }

        private static bool ModifiersHeld(ActionElementMap map)
        {
            var keyboard = ReInput.controllers.Keyboard;
            return ModifierHeld(keyboard, map.modifierKey1) && ModifierHeld(keyboard, map.modifierKey2) && ModifierHeld(keyboard, map.modifierKey3);
        }

        private static bool ModifierHeld(Keyboard keyboard, ModifierKey modifier) =>
            modifier == ModifierKey.None || (keyboard != null && keyboard.GetModifierKey(modifier));
    }
}
