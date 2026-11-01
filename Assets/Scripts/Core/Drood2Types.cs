using System.Linq;
using ModApi.Craft;
using ModApi.Craft.Parts;
using ModApi.Exceptions;

namespace Drood2
{
    /// <summary>
    /// Which part types get Drood 2.0's features: surface attach points, limb
    /// colliders, limb following, persistent tethers and the parachute. Only Drood 2.0;
    /// the stock drood and tourist are left alone. Drood 2.0 defines its attach points
    /// in its own XML (see <see cref="Drood2Part"/>).
    /// </summary>
    public static class Drood2Types
    {
        /// <summary>The first (torso) point. Its name and id 1 are part of the save format.</summary>
        public const string AttachPointName = "DroodSurface";

        private static readonly string[] DroodPartTypeIds = { Drood2Part.PartTypeId };

        public static bool IsDroodPartType(string partTypeId)
        {
            return partTypeId != null && DroodPartTypeIds.Contains(partTypeId);
        }

        /// <summary>
        /// PartTypeList.GetPartType throws InvalidPartTypeException for unknown ids
        /// instead of returning null.
        /// </summary>
        public static PartType FindPartType(PartTypeList partTypes, string id)
        {
            try
            {
                return partTypes.GetPartType(id);
            }
            catch (InvalidPartTypeException)
            {
                return null;
            }
        }

        public static bool CraftUsesDroodSurface(CraftData craftData)
        {
            var parts = craftData?.Assembly?.Parts;
            return parts != null && parts.Any(p => IsDroodPartType(p.PartType?.Id));
        }
    }
}
