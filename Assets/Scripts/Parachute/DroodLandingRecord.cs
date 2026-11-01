using System.Globalization;
using System.IO;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Every canopy landing appended as a line to UserData/Drood20/Landings.csv (next to the drop
    /// challenge's bests, which it never touches): when, the canopy, the touchdown's speeds,
    /// grade and kind, where the flare began (feet height, equivalent airspeed, seconds before
    /// the touchdown; empty without one), and with a scored drop challenge jump its distance and
    /// score. For looking at many landings at once (the game's log keeps two sessions only).
    /// </summary>
    internal static class DroodLandingRecord
    {
        private const string FileName = "Landings.csv";
        private const string Header = "time,canopy,down m/s,forward m/s,grade,kind,flare height m,flare airspeed m/s,flare seconds,drop distance m,drop score";

        private static string Folder => Path.Combine(Application.persistentDataPath, "UserData", "Drood20");

        internal static void Add(string canopy, float down, float forward, int grade, DroodLandingFall.Kind kind,
            float flareHeight, float flareSpeed, float flareSeconds, float dropDistance, int dropScore)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, FileName);
                var flared = !float.IsInfinity(flareSeconds) && !float.IsNaN(flareHeight);
                var line = string.Join(",",
                    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    canopy,
                    Number(down, "0.00"),
                    Number(forward, "0.00"),
                    grade.ToString(CultureInfo.InvariantCulture),
                    kind.ToString(),
                    flared ? Number(flareHeight, "0.0") : string.Empty,
                    flared ? Number(flareSpeed, "0.0") : string.Empty,
                    flared ? Number(flareSeconds, "0.00") : string.Empty,
                    float.IsNaN(dropDistance) ? string.Empty : Number(dropDistance, "0.00"),
                    dropScore >= 0 ? dropScore.ToString(CultureInfo.InvariantCulture) : string.Empty);
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, Header + "\n");
                }

                File.AppendAllText(path, line + "\n");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Couldn't record the landing: {e.Message}");
            }
        }

        private static string Number(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
    }
}
