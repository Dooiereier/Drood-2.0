using System;
using System.Collections;
using System.Text.RegularExpressions;
using ModApi.Ui;
using UnityEngine;
using UnityEngine.Networking;

namespace Drood2
{
    /// <summary>
    /// Once per game start, compares the installed version (the game reads it from the mod's
    /// manifest: Mod.Instance.ModInfo.Version) with the "Mod Version" on Drood 2.0's published
    /// SimpleRockets.com page, and if that's newer offers to open the page. Nothing is
    /// downloaded or installed. Off with the "Check for Updates" setting; a failed check is
    /// logged and otherwise silent.
    /// </summary>
    public static class UpdateCheck
    {
        // The mod's SimpleRockets.com page.
        private const string PageUrl = "https://www.simplerockets.com/Mods/View/358081";

        private const int TimeoutSeconds = 10;

        // The page's general info: <span class="spec-label">Mod Version:</span> <span class="spec-value">v1.0</span>
        private static readonly Regex PageVersion = new Regex(@"Mod Version:\s*</span>\s*<span[^>]*>\s*v?(\d+)\.(\d+)", RegexOptions.IgnoreCase);

        private static bool _started;

        /// <summary>Starts the check once (from the first scene load, when the settings are in).</summary>
        public static void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            if (!(Assets.Scripts.ModSettings.Instance?.CheckForUpdates?.Value ?? true))
            {
                DroodLog.Debug("Update check off (setting).");
                return;
            }

            var host = new GameObject("Drood2.UpdateCheck");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>().StartCoroutine(Check(host));
        }

        /// <summary>The "Mod Version" on a published mod page, or null.</summary>
        public static Version ParseVersion(string page)
        {
            var match = PageVersion.Match(page ?? string.Empty);
            return match.Success ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : null;
        }

        private static IEnumerator Check(GameObject host)
        {
            try
            {
                var installed = Assets.Scripts.Mod.Instance.ModInfo?.Version;
                if (installed == null)
                {
                    yield break;
                }

                using (var request = UnityWebRequest.Get(PageUrl))
                {
                    request.timeout = TimeoutSeconds;
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"[Drood 2.0] Update check failed: {request.error}");
                        yield break;
                    }

                    var published = ParseVersion(request.downloadHandler.text);
                    if (published == null)
                    {
                        Debug.LogWarning("[Drood 2.0] Update check: no mod version found on the page.");
                        yield break;
                    }

                    Debug.Log($"[Drood 2.0] Update check: installed {installed}, published {published}.");
                    if (published.CompareTo(installed) > 0)
                    {
                        ShowDialog(installed, published);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(host);
            }
        }

        private static void ShowDialog(Version installed, Version published)
        {
            try
            {
                var dialog = Assets.Scripts.Game.Instance.UserInterface.CreateMessageDialog(MessageDialogType.OkayCancel);
                dialog.MessageText = $"A new version of Drood 2.0 is available.\n\nInstalled: {installed}\nPublished: {published}";
                dialog.OkayButtonText = "OPEN DOWNLOAD PAGE";
                dialog.CancelButtonText = "LATER";
                dialog.OkayClicked += d =>
                {
                    d.Close();
                    Application.OpenURL(PageUrl);
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Couldn't show the update dialog: {e.Message}");
            }
        }

        // A host for the coroutine: the mod class isn't a MonoBehaviour.
        private class Runner : MonoBehaviour
        {
        }
    }
}
