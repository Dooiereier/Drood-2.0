using Assets.Scripts.Craft.Parts;
using HarmonyLib;

namespace Drood2
{
    [HarmonyPatch(typeof(PartScript), nameof(PartScript.CreateAttachPoints))]
    public static class CreateLimbCollidersPatch
    {
        private static void Prefix(PartScript __instance)
        {
            if (Drood2Types.IsDroodPartType(__instance.Data?.PartType?.Id))
            {
                DroodLimbSurfaces.CreateColliders(__instance.transform);

                // This only runs in the designer: record the posed skeleton once per session.
                if (DroodDesignerPose.NeedsRecording && __instance.GetComponent<DroodDesignerPoseRecorder>() == null)
                {
                    __instance.gameObject.AddComponent<DroodDesignerPoseRecorder>();
                }
            }
        }
    }
}
