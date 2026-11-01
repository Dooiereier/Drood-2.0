using Assets.Scripts.Career.Research;
using HarmonyLib;
using ModApi.Craft.Parts;

namespace Drood2
{
    /// <summary>
    /// Career mode hides designer parts whose tech item ("Part." + designer part name)
    /// isn't unlocked. The stock tech tree has no node for Drood 2.0, so it's unlocked
    /// once both the stock drood and the stock parachute are. Sandbox doesn't check.
    /// A prefix, so the original's lookup of Drood 2.0's own (missing) tech item, which logs
    /// "Could not find Tech Item", never runs.
    /// </summary>
    [HarmonyPatch(typeof(TechTree), nameof(TechTree.IsDesignerPartAvailable))]
    public static class Drood2TechTreePatch
    {
        private const string StockDroodItemId = "Part.DesignerParts.Eva.Drood.Name";
        private const string StockParachuteItemId = "Part.DesignerParts.Parachute1.Parachute.Name";

        private static bool Prefix(TechTree __instance, DesignerPart designerPart, ref bool __result)
        {
            if (designerPart?.Name != Drood2Part.DesignerPartName)
            {
                return true;
            }

            __result = IsUnlocked(__instance, StockDroodItemId) && IsUnlocked(__instance, StockParachuteItemId);
            return false;
        }

        /// <summary>
        /// Items the tree doesn't know count as unlocked. The existence check avoids
        /// GetItemValue's "Could not find Tech Item" error log.
        /// </summary>
        private static bool IsUnlocked(TechTree tree, string itemId)
        {
            return !tree.ItemValueExists(itemId) || tree.GetItemValue(itemId)?.ValueAsBool != false;
        }
    }
}
