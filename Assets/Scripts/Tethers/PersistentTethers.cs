using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using HarmonyLib;
using ModApi.Craft.Parts;
using ModApi.GameLoop;
using ModApi.GameLoop.Interfaces;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Stock tether cycle (EvaScript.UpdateGrapplingHook, on each "shoot tether" press):
    ///   1. no tether          -> shoot drood -> A
    ///   2. drood -> A exists  -> re-shoot from A, giving A -> B (drood is freed)
    ///   3. A -> B exists      -> DestroyGrapplingHook, the A -> B link disappears
    /// EvaScript only ever tracks one GrapplingHookScript. This patch changes step 3:
    /// the A -> B hook is handed over to <see cref="PersistentTethers"/> instead, and
    /// EvaScript forgets it, so the same press shoots a fresh drood -> C tether. With Shift
    /// held it changes step 2 the same way: drood -> A is kept (the drood stays tied to A)
    /// and the press shoots another drood -> C, so a drood can hold several tethers. Kept
    /// tethers still follow that drood's length commands (<see cref="FollowTetherLengthPatch"/>).
    /// </summary>
    [HarmonyPatch(typeof(EvaScript), "UpdateGrapplingHook")]
    public static class KeepOldTethersPatch
    {
        internal static readonly AccessTools.FieldRef<EvaScript, GrapplingHookScript> GrapplingHookField =
            AccessTools.FieldRefAccess<EvaScript, GrapplingHookScript>("_grapplingHook");

        private static readonly AccessTools.FieldRef<EvaScript, bool> ShootTetherProcessedField =
            AccessTools.FieldRefAccess<EvaScript, bool>("_evaShootTetherProcessed");

        // The private setter also refreshes zoom handling (UpdateZoomEnabled).
        private static readonly MethodInfo GrapplingHookSetter =
            AccessTools.PropertySetter(typeof(EvaScript), "GrapplingHook");

        private static readonly MethodInfo PartDestroyedHandler =
            AccessTools.Method(typeof(EvaScript), "GrapplingHookPartDestroyed");

        private static void Prefix(EvaScript __instance)
        {
            // Stock droods keep the stock tether behaviour.
            if (!Drood2Types.IsDroodPartType(__instance.PartScript?.Data?.PartType?.Id))
            {
                return;
            }

            var hook = GrapplingHookField(__instance);
            if (hook == null)
            {
                return;
            }

            var controls = __instance.PartScript.CommandPod?.Controls;
            if (controls == null || !controls.EvaShootTether || ShootTetherProcessedField(__instance))
            {
                return;
            }

            // Step 2 (drood -> A becomes A -> B) stays stock, unless Shift is held: then
            // drood -> A is kept too.
            var droodBody = __instance.PartScript.BodyScript?.RigidBody;
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (droodBody == null || ((hook.BodyFrom == droodBody || hook.BodyTo == droodBody) && !shift))
            {
                return;
            }

            // EvaScript.ConnectViaRaycast subscribed GrapplingHookPartDestroyed on both
            // ends. Left in place, destroying A or B later would make the drood destroy
            // whatever tether it is holding at that moment.
            Unsubscribe(__instance, hook.PartFrom);
            Unsubscribe(__instance, hook.PartTo);

            GrapplingHookSetter.Invoke(__instance, new object[] { null });
            PersistentTethers.Add(hook, __instance);
        }

        private static void Unsubscribe(EvaScript eva, IPartScript part)
        {
            if (part == null)
            {
                return;
            }

            try
            {
                var handler = (PartScriptDestroyedDelegate)Delegate.CreateDelegate(
                    typeof(PartScriptDestroyedDelegate), eva, PartDestroyedHandler);
                part.PartDestroyed -= handler;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Could not unhook tether part-destroyed handler: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Makes every tether a Drood 2.0 has made follow its tether length command (extend /
    /// retract), not just the one it's holding. Postfix on the private
    /// EvaScript.AdjustTetherLength, which the game calls while the drood holds a tether.
    /// When it holds none, <see cref="PersistentTethers"/> reads the control itself.
    /// </summary>
    [HarmonyPatch(typeof(EvaScript), "AdjustTetherLength")]
    public static class FollowTetherLengthPatch
    {
        private static void Postfix(EvaScript __instance, float adjustmentScalar)
        {
            if (Drood2Types.IsDroodPartType(__instance.PartScript?.Data?.PartType?.Id))
            {
                PersistentTethers.AdjustOwnedBy(__instance, adjustmentScalar);
            }
        }
    }

    /// <summary>
    /// Owns tethers the drood has let go of, and which drood made each one. There is no
    /// limit on how many.
    ///
    /// One per flight scene (<see cref="Assets.Scripts.Mod"/> creates it when a flight scene
    /// loads; it goes with the scene), stepped from the flight game loop like the game's own
    /// GrapplingHookScript: not while paused.
    /// </summary>
    public class PersistentTethers : MonoBehaviour, IFlightUpdate
    {
        private static readonly List<GrapplingHookScript> Hooks = new List<GrapplingHookScript>();
        private static readonly Dictionary<GrapplingHookScript, EvaScript> Owners = new Dictionary<GrapplingHookScript, EvaScript>();

        // EvaScript.MaxTetherLength (750 m), read by reflection in case the game changes it.
        private static readonly float MaxTetherLength =
            AccessTools.Field(typeof(EvaScript), "MaxTetherLength")?.GetValue(null) is float max ? max : 750f;

        private static readonly MethodInfo DestroyGrapplingHookMethod =
            AccessTools.Method(typeof(EvaScript), "DestroyGrapplingHook");

        // Removes a kept tether when the part at either end is destroyed, like the game does
        // for the held tether (EvaScript.GrapplingHookPartDestroyed). The body can outlive a
        // destroyed part (other parts on it), so checking the bodies alone isn't enough.
        private static readonly Dictionary<GrapplingHookScript, PartScriptDestroyedDelegate> DestroyedHandlers =
            new Dictionary<GrapplingHookScript, PartScriptDestroyedDelegate>();

        public static void Add(GrapplingHookScript hook, EvaScript owner)
        {
            if (hook != null && !Hooks.Contains(hook))
            {
                Hooks.Add(hook);
                Owners[hook] = owner;

                PartScriptDestroyedDelegate handler = null;
                handler = _ =>
                {
                    DroodLog.Debug("A kept tether's end part was destroyed; removing the tether.");
                    Remove(hook);
                };
                DestroyedHandlers[hook] = handler;
                if (hook.PartFrom != null)
                {
                    hook.PartFrom.PartDestroyed += handler;
                }

                if (hook.PartTo != null)
                {
                    hook.PartTo.PartDestroyed += handler;
                }
            }
        }

        /// <summary>Destroys a kept tether and forgets it.</summary>
        private static void Remove(GrapplingHookScript hook)
        {
            if (DestroyedHandlers.TryGetValue(hook, out var handler))
            {
                DestroyedHandlers.Remove(hook);
                if (hook != null)
                {
                    if (hook.PartFrom != null)
                    {
                        hook.PartFrom.PartDestroyed -= handler;
                    }

                    if (hook.PartTo != null)
                    {
                        hook.PartTo.PartDestroyed -= handler;
                    }
                }
            }

            // OnDestroy -> RemoveGrapplingHook cleans up the joint, line and any temporary
            // kinematic body added to terrain.
            if (hook != null)
            {
                Destroy(hook);
            }

            Hooks.Remove(hook);
            Owners.Remove(hook);
        }

        /// <summary>
        /// Applies a tether length command to every kept tether <paramref name="owner"/>
        /// made. Each changes at the same rate, so they keep their length differences.
        /// </summary>
        public static void AdjustOwnedBy(EvaScript owner, float adjustmentScalar)
        {
            foreach (var hook in Hooks)
            {
                if (hook != null && Owners.TryGetValue(hook, out var hookOwner) && hookOwner == owner)
                {
                    hook.AdjustTetherLength(adjustmentScalar, MaxTetherLength);
                }
            }
        }

        /// <summary>
        /// Kept tethers plus the one each Drood 2.0 is currently holding. Stock droods'
        /// tethers are left alone.
        /// </summary>
        public static void RemoveAllIncludingHeld()
        {
            var removed = Hooks.Count;
            RemoveAll();

            // DestroyGrapplingHook also unsubscribes the part-destroyed handlers and
            // resets the tether length control.
            foreach (var eva in FindObjectsOfType<EvaScript>())
            {
                if (Drood2Types.IsDroodPartType(eva.PartScript?.Data?.PartType?.Id)
                    && KeepOldTethersPatch.GrapplingHookField(eva) != null)
                {
                    // DestroyGrapplingHook(bool immediate, bool resetAdjustmentSlider)
                    DestroyGrapplingHookMethod.Invoke(eva, new object[] { false, true });
                    removed++;
                }
            }

            DroodLog.Debug($"Removed {removed} tether(s).");
        }

        public static void RemoveAll()
        {
            foreach (var hook in Hooks.ToArray())
            {
                Remove(hook);
            }

            Hooks.Clear();
            Owners.Clear();
            DestroyedHandlers.Clear();
        }

        private static KeyCode RemoveKey()
        {
            try
            {
                return Assets.Scripts.ModSettings.Instance?.RemoveTethersKey?.Value.ToKeyCode() ?? KeyCode.O;
            }
            catch (Exception)
            {
                return KeyCode.O;
            }
        }

        /// <summary>Game loop bookkeeping (IGameLoopItem).</summary>
        public bool StartMethodCalled { get; set; }

        private void Awake()
        {
            Assets.Scripts.Game.Instance?.FlightScene?.GameLoop?.Register(this);
        }

        private void OnDestroy()
        {
            Assets.Scripts.Game.Instance?.FlightScene?.GameLoop?.Unregister(this);
            RemoveAll();
        }

        void IFlightUpdate.FlightUpdate(in FlightFrameData frame)
        {
            // The "Remove Tethers Key" setting; only while a Drood 2.0 is in control.
            var key = RemoveKey();
            if (key != KeyCode.None && Input.GetKeyDown(key) && DroodKeys.ControlledDrood() != null)
            {
                RemoveAllIncludingHeld();
            }

            AdjustForDroodsWithoutHeldTether();

            for (var i = Hooks.Count - 1; i >= 0; i--)
            {
                // A SpringJoint whose connected body is gone anchors to world space and
                // would pin the other object in mid-air, so drop the tether when either
                // end disappears (craft unloaded, scene change).
                var hook = Hooks[i];
                var joint = hook != null ? hook.GrapplingHookJoint : null;
                if (joint == null || joint.connectedBody == null || hook.BodyFrom == null || hook.BodyTo == null || OwnerLeftEva(hook))
                {
                    Remove(hook);
                }
            }
        }

        /// <summary>
        /// A kept tether tied to its drood, which has gone into a seat or pod (the game drops
        /// the held tether then too).
        /// </summary>
        private static bool OwnerLeftEva(GrapplingHookScript hook)
        {
            if (!Owners.TryGetValue(hook, out var owner) || owner == null || owner.EvaActive)
            {
                return false;
            }

            var body = owner.PartScript?.BodyScript?.RigidBody;
            return body != null && (hook.BodyFrom == body || hook.BodyTo == body);
        }

        /// <summary>
        /// The game only processes the tether length control while the drood holds a
        /// tether (EvaScript.UpdateGrapplingHook). When it holds none but has kept tethers,
        /// apply the control here so they still follow.
        /// </summary>
        // Reused each frame (AdjustForDroodsWithoutHeldTether).
        private static readonly HashSet<EvaScript> OwnersThisFrame = new HashSet<EvaScript>();

        private static void AdjustForDroodsWithoutHeldTether()
        {
            if (Owners.Count == 0)
            {
                return;
            }

            OwnersThisFrame.Clear();
            foreach (var pair in Owners)
            {
                OwnersThisFrame.Add(pair.Value);
            }

            foreach (var owner in OwnersThisFrame)
            {
                if (owner == null || !owner.IsPlayerCraft || !owner.EvaActive
                    || KeepOldTethersPatch.GrapplingHookField(owner) != null)
                {
                    continue;
                }

                var amount = owner.PartScript?.CommandPod?.Controls?.EvaTetherLength ?? 0f;
                if (Mathf.Abs(amount) > 0.01f)
                {
                    AdjustOwnedBy(owner, amount);
                }
            }
        }
    }
}
