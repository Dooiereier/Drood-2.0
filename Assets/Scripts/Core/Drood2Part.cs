using System.IO;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// "Drood 2.0": a copy of the stock drood (part type "Eva") with a built-in parachute
    /// (<see cref="DroodParachute"/>). The original drood is untouched.
    ///
    /// The game loads the part from the mod's part XML (Assets/Parts/Drood2.xml, listed in
    /// ModData's parts): ModManagerScript.ModLoaded registers the mod's part modifiers and then
    /// its parts. The game recognises droods by their Eva modifier, not by the "Eva" id (the id
    /// is only checked by the flight inspector and an old save upgrader), so a second part type
    /// with the same modifiers is a full drood. It reuses the stock Eva prefab, so the part has no
    /// model of its own.
    ///
    /// The XML is the stock Eva entry from ModTools/Parts/Parts.xml (Juno 1.4.105) with: the
    /// id and name, mass 115 (stock drood 100 kg + 15 kg parachute pack), 10 activation groups
    /// with AG1 named "Parachute" (the stock XML has an empty list; the designer normally
    /// copies groups from the craft's other pods), the body-segment surface attach points
    /// (<see cref="DroodLimbSurfaces.Segments"/>, same names and order), the designer entry,
    /// and the first-person field of view 70 (stock 60; saved crafts store only changed values,
    /// so existing crafts get it too). &lt;Eva&gt; stays before the Drood 2.0 modifier: the game
    /// runs the modifiers' game-loop updates in the order they are created, and Drood 2.0's
    /// FixedUpdate must follow EvaScript's (the run-out and the heading hold set the body after
    /// it; verified in-game).
    /// </summary>
    public static class Drood2Part
    {
        public const string PartTypeId = "Drood2";

        public const string DesignerPartName = "Drood 2.0";

        private static bool _checked;

        /// <summary>
        /// Logs once, on the first scene load, whether the game loaded the part from the mod's
        /// part XML.
        /// </summary>
        public static void CheckLoaded()
        {
            var partTypes = Assets.Scripts.Game.Instance?.PartTypes;
            if (_checked || partTypes == null)
            {
                return;
            }

            _checked = true;
            if (Drood2Types.FindPartType(partTypes, PartTypeId) != null)
            {
                Debug.Log("[Drood 2.0] Part 'Drood 2.0' loaded from the mod's part XML.");
            }
            else
            {
                Debug.LogError("[Drood 2.0] The part 'Drood 2.0' wasn't loaded from the mod's part XML (Assets/Parts/Drood2.xml), so crafts with it can't load.");
            }
        }

        /// <summary>
        /// The designer icon's file in GameData/Parts/Icons, named in the part XML
        /// (DesignerPart iconType="File" iconPath="Drood2.png"). With iconType File the game
        /// takes no snapshot of the part (PartViewerScript.TakeAllPartPictures skips it), so
        /// the embedded icon isn't overwritten.
        /// </summary>
        public const string IconFileName = "Drood2.png";

        /// <summary>
        /// Writes the embedded icon (<see cref="Drood2Icon"/>: the drood under its ram-air
        /// canopy and a red "2.0") to <see cref="IconFileName"/>. The file is rewritten
        /// whenever it differs, so icon updates ship with the mod. This must run before the
        /// designer loads, because DesignerPart.GetIcon caches a failed load.
        /// </summary>
        public static void EnsureIcon()
        {
            var iconFolder = Path.Combine(Assets.Scripts.Game.PersistentDataPath, "GameData/Parts/Icons");
            var icon = Path.Combine(iconFolder, IconFileName);
            var png = System.Convert.FromBase64String(Drood2Icon.PngBase64);
            if (File.Exists(icon) && System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(icon), png))
            {
                return;
            }

            Directory.CreateDirectory(iconFolder);
            File.WriteAllBytes(icon, png);
            Debug.Log("[Drood 2.0] Wrote the Drood 2.0 part icon.");
        }
    }
}
