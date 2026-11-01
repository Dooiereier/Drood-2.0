using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using HarmonyLib;

namespace Drood2
{
    /// <summary>
    /// Free look under the canopy. In first person in the air, EvaScript.UpdateKinematicTurning
    /// turns the whole drood to the camera's direction and zeroes the camera's mouse offset,
    /// so every mouse movement turns the body. While Drood 2.0 hangs under its canopy that
    /// step is skipped: the mouse offset then stays on the camera (the game adds it on top of
    /// the drood's view point), so the mouse looks around and the body keeps its heading. The
    /// turn keys steer instead (<see cref="DroodParachute.FreeLook"/>). The same in the free-fall
    /// arch (<see cref="DroodFreeFall.FreeLook"/>), where the body flies belly to the airflow.
    /// On the ground nothing changes, except during a landing clip (<see cref="DroodLandingFall"/>):
    /// the turn keys (or walking forward with the camera-following setting) would spin the body
    /// lying on the ground or getting up, so the step is skipped then too. Seated in the drop
    /// zone's plane (<see cref="DroodPlaneRide.InPlane"/>) it's skipped too: the seat holds the body.
    /// </summary>
    [HarmonyPatch(typeof(EvaScript), "UpdateKinematicTurning")]
    public static class FreeLookUnderCanopyPatch
    {
        private static bool Prefix(EvaScript __instance)
        {
            // Only Drood 2.0 has these components; the stock drood stops at the first lookup.
            var parachute = __instance.GetComponent<DroodParachute>();
            if (parachute == null)
            {
                return true;
            }

            if (parachute.FreeLook)
            {
                return false;
            }

            var ride = __instance.GetComponent<DroodPlaneRide>();
            if (ride != null && ride.InPlane)
            {
                return false;
            }

            var freeFall = __instance.GetComponent<DroodFreeFall>();
            var landingFall = __instance.GetComponent<DroodLandingFall>();
            return (freeFall == null || !freeFall.FreeLook) && (landingFall == null || !landingFall.Active);
        }
    }
}
