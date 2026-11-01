using System.Globalization;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The drop challenge's personal bests, one per canopy type (ram-air and swoop), wherever the
    /// jump was. Kept in the
    /// game's user data, beside the player's crafts (UserData/Drood20/DropChallengeBests.xml),
    /// so they last across saves and mod updates; read on first use, written on a new best.
    /// A missing or unreadable file starts empty.
    /// </summary>
    internal static class DroodDropChallengeBests
    {
        private const string FileName = "DropChallengeBests.xml";

        private static bool _loaded;
        private static readonly System.Collections.Generic.Dictionary<string, int> Bests = new System.Collections.Generic.Dictionary<string, int>();

        private static string Folder => Path.Combine(Application.persistentDataPath, "UserData", "Drood20");

        /// <summary>The best so far for <paramref name="canopy"/>, or -1.</summary>
        internal static int Best(string canopy)
        {
            Load();
            return Bests.TryGetValue(canopy, out var best) ? best : -1;
        }

        /// <summary>Records a score; true if it is a new best (then saved).</summary>
        internal static bool Record(string canopy, int score, float distance, int grade, float seconds)
        {
            Load();
            if (Bests.TryGetValue(canopy, out var best) && best >= score)
            {
                return false;
            }

            Bests[canopy] = score;
            Save(canopy, score, distance, grade, seconds);
            return true;
        }

        /// <summary>
        /// Clears the swoop's bests (<paramref name="swoop"/>: the keys starting "Swoop") or the drop
        /// zone's (all others). The file as it was is kept aside (a copy with the date), the
        /// others' bests stay in it.
        /// </summary>
        internal static void Reset(bool swoop)
        {
            Load();
            var gone = new System.Collections.Generic.List<string>();
            foreach (var key in Bests.Keys)
            {
                if (IsSwoop(key) == swoop)
                {
                    gone.Add(key);
                }
            }

            foreach (var key in gone)
            {
                Bests.Remove(key);
            }

            try
            {
                var path = Path.Combine(Folder, FileName);
                var root = File.Exists(path) ? TryLoad(path) : null;
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".reset-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), true);
                }

                if (root != null)
                {
                    foreach (var best in new System.Collections.Generic.List<XElement>(root.Elements("Best")))
                    {
                        if (IsSwoop((string)best.Attribute("canopy") ?? string.Empty) == swoop)
                        {
                            best.Remove();
                        }
                    }

                    new XDocument(root).Save(path);
                }

                DroodLog.Debug($"{(swoop ? "Swoop" : "Drop zone")} high scores reset ({gone.Count}).");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Couldn't reset the high scores: {e.Message}");
            }
        }

        /// <summary>Asks, then clears the swoop's bests or the drop zone's.</summary>
        internal static void AskReset(bool swoop)
        {
            var dialog = Assets.Scripts.Game.Instance?.UserInterface?.CreateMessageDialog(ModApi.Ui.MessageDialogType.OkayCancel);
            if (dialog == null)
            {
                return;
            }

            dialog.MessageText = swoop
                ? "Reset all swoop high scores (both variants, all difficulties, both wings)?"
                : "Reset the Drop Zone challenge's high scores?";
            dialog.OkayButtonText = "Reset";
            dialog.CancelButtonText = "Cancel";
            dialog.UseDangerButtonStyle = true;
            dialog.OkayClicked += d =>
            {
                d.Close();
                Reset(swoop);
            };
            dialog.CancelClicked += d => d.Close();
        }

        private static bool IsSwoop(string key) => key.StartsWith("Swoop", System.StringComparison.Ordinal);

        private static void Load()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            try
            {
                var path = Path.Combine(Folder, FileName);
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (var best in XDocument.Load(path).Root.Elements("Best"))
                {
                    var canopy = (string)best.Attribute("canopy");
                    if (!string.IsNullOrEmpty(canopy) && int.TryParse((string)best.Attribute("score"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score))
                    {
                        Bests[canopy] = score;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Couldn't read the drop challenge bests: {e.Message}");
            }
        }

        /// <summary>Writes the file, the new best with its details, the others as they were.</summary>
        private static void Save(string canopy, int score, float distance, int grade, float seconds)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, FileName);
                var root = File.Exists(path) ? TryLoad(path) : null;
                if (root == null && File.Exists(path))
                {
                    // Unreadable: kept aside, not overwritten.
                    File.Copy(path, path + ".unreadable-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), true);
                }

                root = root ?? new XElement("DropChallengeBests");
                foreach (var old in root.Elements("Best"))
                {
                    if ((string)old.Attribute("canopy") == canopy)
                    {
                        old.Remove();
                        break;
                    }
                }

                root.Add(new XElement("Best",
                    new XAttribute("canopy", canopy),
                    new XAttribute("score", score.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("distance", distance.ToString("0.00", CultureInfo.InvariantCulture)),
                    new XAttribute("grade", grade.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("seconds", seconds.ToString("0.0", CultureInfo.InvariantCulture)),
                    new XAttribute("date", System.DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
                new XDocument(root).Save(path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Couldn't save the drop challenge best: {e.Message}");
            }
        }

        private static XElement TryLoad(string path)
        {
            try
            {
                return XDocument.Load(path).Root;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
