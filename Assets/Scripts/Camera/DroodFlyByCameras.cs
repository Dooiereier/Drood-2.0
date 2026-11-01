using System.Collections;
using System.Reflection;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using Assets.Scripts.Flight.GameView.Cameras;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The game's static fly-by camera for Drood 2.0 (the cinematic one stays off: its shots
    /// are framed for crafts, not a drood). While a drood is in
    /// control the game switches every camera off except its third-person and first-person
    /// views (EvaSharedCamerasScript.SetEvaCamerasEnabled, through the view panel's
    /// SetButtonsEnabledExcluding); the fly-by camera itself has no drood check. With a Drood 2.0
    /// in control this switches the fly-by camera back on, with its view panel buttons, so the
    /// view keys and the panel reach it; when control passes to the stock drood it is switched
    /// off again (other crafts get it from the game).
    /// </summary>
    internal static class DroodFlyByCameras
    {
        // The cinematic fly-by camera: its kind (FlyByCameraController._mode; private) or its view's
        // name contains this.
        private const string CinematicMode = "Cinematic";

        private static bool _switchedOn;
        private static Assets.Scripts.Flight.UI.ViewPanelController _viewPanel;
        private static bool _loggedMissing;
        private static bool _loggedModes;
        private static int _lastSelected = -1;

        /// <summary>
        /// Called once a frame from the flight game loop (<see cref="DroodCameraViews"/>) with the
        /// drood in control (null: none) and whether it is a Drood 2.0.
        /// </summary>
        internal static void Step(EvaScript eva, bool drood2)
        {
            if (drood2)
            {
                SetFlyBy(true);
                SkipCinematic();
                ShowCameraList(true);
                return;
            }

            if (_switchedOn)
            {
                // Back to the game's own state: off for the stock drood, on for other crafts
                // (the game has already switched them on for those).
                if (eva != null)
                {
                    SetFlyBy(false);
                    ShowCameraList(false);
                }

                _switchedOn = false;
            }
        }

        /// <summary>The drood in control (null: none or another craft).</summary>
        internal static EvaScript PlayerEva()
        {
            var part = Assets.Scripts.Game.Instance?.FlightScene?.CraftNode?.CraftScript?.ActiveCommandPod?.Part?.PartScript;
            var eva = part?.GetModifier<EvaScript>();
            return eva != null && eva.IsPlayerCraft && eva.EvaActive ? eva : null;
        }

        private static void SetFlyBy(bool enabled)
        {
            var manager = CameraManagerScript.Instance;
            if (!(manager?.CameraModes is IList modes))
            {
                return;
            }

            var changed = false;
            for (var i = 0; i < modes.Count; i++)
            {
                var mode = modes[i] as CameraMode;
                var controller = mode?.CameraController;
                if (controller == null || controller.GetType() != DroodReflection.FlyByCameraType)
                {
                    continue;
                }

                if (!_loggedModes)
                {
                    DroodLog.Debug($"Fly-by view '{mode.Name}': kind {DroodReflection.FlyByModeField?.GetValue(controller)}, {(IsStaticFlyBy(mode) ? "stationary" : "cinematic")}.");
                }

                if (IsStaticFlyBy(mode))
                {
                    if (controller.Enabled != enabled)
                    {
                        controller.SetEnabled(enabled, true);
                        changed = true;
                    }
                }
            }

            _loggedModes = true;
            if (enabled)
            {
                _switchedOn = true;
            }

            if (!changed)
            {
                return;
            }

            _switchedOn = enabled;
            SetButtons(enabled);
            DroodLog.Debug($"Fly-by cameras {(enabled ? "on" : "off")} for Drood 2.0.");
        }

        /// <summary>Whether every view strictly between <paramref name="from"/> and
        /// <paramref name="to"/>, stepping by <paramref name="step"/> (wrapping round), is off.</summary>
        private static bool PassesOnlyOff(IList modes, int from, int to, int step)
        {
            var n = modes.Count;
            for (var k = (from + step + n) % n; k != to; k = (k + step + n) % n)
            {
                if (k == from)
                {
                    return false;
                }

                var mode = modes[k] as CameraMode;
                if (mode?.CameraController != null && mode.CameraController.Enabled)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The cinematic fly-by view is skipped while a Drood 2.0 is in control: it shares its
        /// camera with the stationary one (the game's view cycle only skips views whose camera is
        /// off), so on landing on it the cycle moves on, the way it was going.
        /// </summary>
        private static void SkipCinematic()
        {
            var manager = CameraManagerScript.Instance;
            if (!(manager?.CameraModes is IList modes))
            {
                return;
            }

            for (var i = 0; i < modes.Count; i++)
            {
                var mode = modes[i] as CameraMode;
                if (mode == null || !mode.IsSelected)
                {
                    continue;
                }

                var controller = mode.CameraController;
                if (controller != null && controller.GetType() == DroodReflection.FlyByCameraType && !IsStaticFlyBy(mode))
                {
                    // The way the cycle was going: the game's cycle passes only views that are
                    // off, so it came the way with none on between the last view and this one
                    // (forward when both ways are clear or the last view is unknown).
                    var forward = _lastSelected < 0 || PassesOnlyOff(modes, _lastSelected, i, 1) || !PassesOnlyOff(modes, _lastSelected, i, -1);
                    manager.SwitchToNextViewMode(false, true, forward);
                    DroodLog.Debug($"Skipped the cinematic fly-by view ({(forward ? "forward" : "back")}).");
                    return;
                }

                _lastSelected = i;
                return;
            }
        }

        /// <summary>A stationary fly-by camera view (not the cinematic one).</summary>
        private static bool IsStaticFlyBy(CameraMode mode)
        {
            var controller = mode?.CameraController;
            if (controller == null || controller.GetType() != DroodReflection.FlyByCameraType)
            {
                return false;
            }

            if (StaticFlyBy.TryGetValue(mode, out var known))
            {
                return known;
            }

            var kind = DroodReflection.FlyByModeField?.GetValue(controller)?.ToString() ?? string.Empty;
            var name = mode.Name ?? string.Empty;
            var isStatic = kind.IndexOf(CinematicMode, System.StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf(CinematicMode, System.StringComparison.OrdinalIgnoreCase) < 0;
            StaticFlyBy[mode] = isStatic;
            return isStatic;
        }

        // Worked out once per view (it is asked every frame), for the flight scene.
        private static readonly System.Collections.Generic.Dictionary<CameraMode, bool> StaticFlyBy = new System.Collections.Generic.Dictionary<CameraMode, bool>();

        /// <summary>
        /// The view panel's camera list button (its "camera parts" dropdown, where the chase views
        /// and the Course Camera are listed, see DroodChaseCameraMode): the game hides it while a
        /// drood is in control (EvaSharedCamerasScript.SetEvaCamerasEnabled →
        /// ShowCustomCameraPartsPanel(false)); shown again each frame while a Drood 2.0 is (the game
        /// may hide it again), hidden again for the stock drood.
        /// </summary>
        private static void ShowCameraList(bool shown)
        {
            if (_viewPanel == null && Time.unscaledTime >= _nextPanelSearch)
            {
                // Looked for at most once a second until found.
                _nextPanelSearch = Time.unscaledTime + 1f;
                _viewPanel = Object.FindObjectOfType<Assets.Scripts.Flight.UI.ViewPanelController>(true);
            }

            _viewPanel?.ShowCustomCameraPartsPanel(shown);
        }

        private static float _nextPanelSearch;

        /// <summary>The view panel's button for <paramref name="mode"/> (null: none yet).</summary>
        private static Component ButtonOf(CameraMode mode)
        {
            if (_viewPanel == null)
            {
                _viewPanel = Object.FindObjectOfType<Assets.Scripts.Flight.UI.ViewPanelController>(true);
            }

            if (_viewPanel == null || mode == null || !(DroodReflection.ViewPanelButtonMapField?.GetValue(_viewPanel) is IDictionary buttons))
            {
                return null;
            }

            foreach (DictionaryEntry entry in buttons)
            {
                if (entry.Value == mode)
                {
                    return entry.Key as Component;
                }
            }

            return null;
        }

        /// <summary>The panel button of a camera part's view shown (the game hides them while a drood is in control).</summary>
        internal static void ShowButton(CameraMode mode)
        {
            var button = ButtonOf(mode);
            if (button != null && !button.gameObject.activeSelf)
            {
                button.gameObject.SetActive(true);
            }
        }

        /// <summary>The label of a camera part's panel button (its "text" element) set to <paramref name="text"/>.</summary>
        internal static void SetButtonText(CameraMode mode, string text)
        {
            var button = ButtonOf(mode) as UI.Xml.XmlElement;
            var label = button?.GetElementByInternalId("text");
            if (label != null)
            {
                UI.Xml.ExtensionMethods.SetText(label, text);
            }
        }

        /// <summary>The flight scene ends: its view panel and view list are let go.</summary>
        internal static void ForgetFlightScene()
        {
            _viewPanel = null;
            StaticFlyBy.Clear();
            _switchedOn = false;
            _nextPanelSearch = 0f;
            _lastSelected = -1;
        }

        /// <summary>Shows or hides the static fly-by camera's buttons in the view panel.</summary>
        private static void SetButtons(bool visible)
        {
            if (_viewPanel == null)
            {
                _viewPanel = Object.FindObjectOfType<Assets.Scripts.Flight.UI.ViewPanelController>();
            }

            if (_viewPanel == null || !(DroodReflection.ViewPanelButtonMapField?.GetValue(_viewPanel) is IDictionary buttons))
            {
                if (!_loggedMissing)
                {
                    _loggedMissing = true;
                    Debug.LogWarning("[Drood 2.0] The view panel's camera buttons weren't found; the fly-by cameras are on but have no panel buttons (the view keys still reach them).");
                }

                return;
            }

            foreach (DictionaryEntry entry in buttons)
            {
                if (!IsStaticFlyBy(entry.Value as CameraMode))
                {
                    continue;
                }

                var button = entry.Key as Component;
                if (button != null)
                {
                    button.gameObject.SetActive(visible);
                }
            }
        }
    }
}
