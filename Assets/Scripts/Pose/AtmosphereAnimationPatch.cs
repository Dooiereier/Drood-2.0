using Assets.Scripts.Animation;
using HarmonyLib;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The normal animations for Drood 2.0 in air thick enough for skydiving
    /// (<see cref="DroodAtmosphereAnimation"/>). The game picks a drood's animation set in
    /// CrewAnimController.SelectAnimationController: the space set while its zero-g switch is on,
    /// which EvaScript.UpdateAnimationController sets every frame from the reference frame not
    /// being locked to the surface. After a recentre high above the ground (a teleport up 5000 m)
    /// the frame isn't locked until the ground's collision loads about 1000 m above it, so the
    /// free-fall arch was bent from the space falling animation (nose down) and changed at
    /// 1000 m. For the selection only, the switch reads off; the game's own value stays, so its
    /// setter doesn't select again every frame. Stock droods are left alone.
    /// </summary>
    [HarmonyPatch(typeof(CrewAnimController), "SelectAnimationController")]
    public static class AtmosphereAnimationPatch
    {
        private static readonly AccessTools.FieldRef<CrewAnimController, bool> ZeroGeeField =
            AccessTools.FieldRefAccess<CrewAnimController, bool>("_zeroGeeAnimation");

        private static readonly AccessTools.FieldRef<CrewAnimController, Animator> AnimatorField =
            AccessTools.FieldRefAccess<CrewAnimController, Animator>("_animator");

        private static void Prefix(CrewAnimController __instance, out bool __state)
        {
            __state = false;
            if (!ZeroGeeField(__instance))
            {
                return;
            }

            var animator = AnimatorField(__instance);
            var look = animator != null ? animator.GetComponentInParent<DroodAtmosphereAnimation>() : null;
            if (look != null && look.Normal)
            {
                ZeroGeeField(__instance) = false;
                __state = true;
            }
        }

        private static void Postfix(CrewAnimController __instance, bool __state)
        {
            if (__state)
            {
                ZeroGeeField(__instance) = true;
            }
        }
    }
}
