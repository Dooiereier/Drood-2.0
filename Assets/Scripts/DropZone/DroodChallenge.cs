using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft;
using ModApi.Craft.Parts;
using ModApi.Flight.GameView;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// What the Drop Zone and Swoop challenges share: when a jump may start, the AG2 key, and
    /// getting the drood ready for and moved to a start.
    /// </summary>
    internal static class DroodChallenge
    {
        // Only a drood with at most this much attached (kg: held parts, parts on its limbs) is
        // teleported: the teleport moves the drood's whole craft, so a drood standing on or built
        // into something bigger would take it up 3 km.
        private const float MaxAttachedMass = 200f;

        // A jump starts (at launch or with AG2 on the ground) only standing still, at most this
        // fast over the ground (m/s): on a moving vehicle the target would be a spot it leaves.
        private const float MaxStartSpeed = 1f;

        // ... and only in air at least this dense (kg/m³) at the drood's spot: the arch, which
        // deploys and opens the canopy, needs the airflow; without air the drood would only fall
        // 3 km.
        private const float MinStartAirDensity = 0.5f;

        // AG2's keys when the game has no keyboard binding for it.
        private static readonly KeyCode[] ActivationGroup2Keys = { KeyCode.Alpha2, KeyCode.Keypad2 };

        /// <summary>Not in time warp.</summary>
        internal static bool NormalTime()
        {
            var time = Assets.Scripts.Game.Instance?.FlightScene?.TimeManager?.CurrentMode;
            return time == null || time.TimeMultiplier <= 1.0;
        }

        /// <summary>On EVA, on the ground or in water, not in time warp.</summary>
        internal static bool Down(EvaScript eva) => eva.EvaActive && (eva.IsGrounded || eva.IsInWater) && NormalTime();

        /// <summary>
        /// The AG2 key pressed, only where <paramref name="canAct"/> (skipped when the game's own
        /// input fired, so a press never counts twice).
        /// </summary>
        internal static bool Ag2KeyPressed(EvaScript eva, bool canAct)
        {
            var inputs = Assets.Scripts.Game.Instance?.Inputs;
            return canAct && DroodKeys.CanUseKeys(eva)
                && DroodKeys.BindingDown(inputs?.ActivationGroup2, ActivationGroup2Keys)
                && inputs?.ActivationGroup2?.GetButtonDownIfEnabled() != true;
        }

        /// <summary>
        /// Standing still (MaxStartSpeed) in air dense enough (MinStartAirDensity): otherwise a
        /// Flight Log line says why no jump starts.
        /// </summary>
        internal static bool CanStartHere(ICraftScript craft, IPartScript part, string challenge)
        {
            var speed = craft.SurfaceVelocity.magnitude;
            var density = craft.AtmosphereSample.AirDensity;
            string reason = null;
            if (speed > MaxStartSpeed)
            {
                reason = $"the drood is moving ({speed:0.0} m/s); stand still on the ground to start a jump.";
            }
            else if (density < MinStartAirDensity)
            {
                reason = density <= 0.0
                    ? "there is no air here for the parachute."
                    : $"the air here is too thin for the parachute ({density:0.00} kg/m³, at least {MinStartAirDensity:0.0} needed).";
            }

            if (reason == null)
            {
                return true;
            }

            Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.FlightLog?.AddLog(
                $"{challenge}: no jump, {reason}", ModApi.Flight.UI.FlightLogEntryCategory.Default, false, part);
            DroodLog.Debug($"{challenge}: no jump ({reason})");
            return false;
        }

        /// <summary>More than MaxAttachedMass on the drood: a Flight Log line says why it stays.</summary>
        internal static bool TooMuchAttached(ICraftScript craft, IPartScript part, string challenge)
        {
            var attached = (craft.Mass - (part.Data?.Mass ?? 0f)) / ModApi.Constants.MassScale;
            if (attached <= MaxAttachedMass)
            {
                return false;
            }

            Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.FlightLog?.AddLog(
                $"{challenge}: no teleport, {attached:0} kg is attached to the drood (at most {MaxAttachedMass:0} kg). Launch the drood on its own or with only what it holds.",
                ModApi.Flight.UI.FlightLogEntryCategory.Default, false, part);
            DroodLog.Debug($"{challenge}: {attached:0} kg attached; no teleport.");
            return true;
        }

        /// <summary>
        /// A jump starts with the jetpack off (the EVA inspector's toggle; it stays off after the
        /// landing, the player switches it back on). Straight from a touchdown, the landing
        /// animation and a queued victory end here, not in the air.
        /// </summary>
        internal static void ReadyForJump(EvaScript eva, DroodLandingFall landingFall)
        {
            if (eva.JetpackEnabled)
            {
                eva.JetpackEnabled = false;
            }

            eva.GetComponent<DroodParachute>()?.ForgetJetpackSuspension();
            landingFall?.Abort();
            eva.GetComponent<DroodVictory>()?.Cancel();
        }

        /// <summary>The craft at a planet position and velocity, in the view at once.</summary>
        internal static void MoveTo(ICraftNode node, Vector3d position, Vector3d velocity)
        {
            node.SetStateVectors(position, velocity, node.Orbit.Time);
            var view = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView;
            if (view != null && node is IGameViewObject viewObject)
            {
                viewObject.RecalculateFrameState(view.ReferenceFrame);
            }
        }

        /// <summary>A bone of the drood's by its name.</summary>
        internal static Transform FindBone(Component drood, string name)
        {
            foreach (var t in drood.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
