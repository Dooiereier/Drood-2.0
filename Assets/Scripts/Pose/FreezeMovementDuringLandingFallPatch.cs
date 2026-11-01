using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using HarmonyLib;

namespace Drood2
{
    /// <summary>
    /// Freezes walking while the landing plays and just after any touchdown under the canopy
    /// (<see cref="DroodRunOut.Settling"/>). EvaScript.UpdateMaxSpeeds recomputes the (private)
    /// max forward/strafe speeds every physics tick, so they're zeroed right after.
    /// </summary>
    [HarmonyPatch(typeof(EvaScript), "UpdateMaxSpeeds")]
    public static class FreezeMovementDuringLandingFallPatch
    {
        // The private setters as delegates (plain .NET, so any Harmony version works).
        private static readonly System.Action<EvaScript, float> SetMaxForwardSpeed = Setter("MaxForwardSpeed");
        private static readonly System.Action<EvaScript, float> SetMaxStrafeSpeed = Setter("MaxStrafeSpeed");

        private static System.Action<EvaScript, float> Setter(string property) =>
            (System.Action<EvaScript, float>)System.Delegate.CreateDelegate(
                typeof(System.Action<EvaScript, float>), AccessTools.PropertySetter(typeof(EvaScript), property));

        private static void Postfix(EvaScript __instance)
        {
            var fall = __instance.GetComponent<DroodLandingFall>();
            var runOut = fall != null ? __instance.GetComponent<DroodRunOut>() : null;
            if (fall != null && (fall.Active || (runOut != null && runOut.Settling)))
            {
                SetMaxForwardSpeed(__instance, 0f);
                SetMaxStrafeSpeed(__instance, 0f);
            }
        }
    }
}
