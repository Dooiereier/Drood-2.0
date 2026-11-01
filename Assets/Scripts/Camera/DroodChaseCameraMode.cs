using System.Collections;
using Assets.Scripts.Flight.GameView.Cameras;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The chase views in the game's list of camera views (cycled with the game's view keys),
    /// after the drood's own views (third person, first person), while a Drood 2.0 is the craft
    /// in control: "Canopy Chase" (orbiting a point three quarters up the lines, 3 m) and "Drood Chase"
    /// (orbiting the drood), both behind the canopy's heading while the canopy is out; Drood
    /// Chase also while skydiving (from above and behind, see DroodChaseCamera).
    /// Each is a camera mode on the game's own orbit camera (so zoom and dragging work as there);
    /// selected, <see cref="DroodChaseCamera"/> steers that camera. The modes are added to the
    /// camera manager's public list (CameraManagerScript.CameraModes, a CameraMode with the
    /// third-person view's controller and sub-mode) and taken out again when another craft is in
    /// control.
    /// </summary>
    internal sealed class DroodChaseCameraMode
    {
        internal static readonly DroodChaseCameraMode Canopy = new DroodChaseCameraMode("Canopy Chase", 0.75f);
        internal static readonly DroodChaseCameraMode Drood = new DroodChaseCameraMode("Drood Chase", 0f);

        /// <summary>The chase view selected now (null: none).</summary>
        internal static DroodChaseCameraMode SelectedMode => Canopy.Selected ? Canopy : Drood.Selected ? Drood : null;

        /// <summary>
        /// Both views while the canopy is out, Drood Chase also while skydiving; each taken out
        /// otherwise.
        /// </summary>
        internal static void EnsureAll(bool canopyOut, bool skydiving)
        {
            Canopy.Ensure(canopyOut);
            Drood.Ensure(canopyOut || skydiving);
        }

        internal readonly string Name;

        /// <summary>How far up the lines the orbited point is (0: the drood).</summary>
        internal readonly float UpLines;

        private CameraManagerScript _manager;
        private CameraMode _mode;
        private bool _failed;
        private bool _reportedList;

        private DroodChaseCameraMode(string name, float upLines)
        {
            Name = name;
            UpLines = upLines;
        }

        /// <summary>The flight scene ends: both views let go of its camera list.</summary>
        internal static void ForgetAll()
        {
            Canopy.Forget();
            Drood.Forget();
        }

        private void Forget()
        {
            _manager = null;
            _mode = null;
            _reportedList = false;
        }

        /// <summary>Selects Drood Chase if it is in the game's list now (false: not there yet).</summary>
        internal static bool SelectDroodChase() => Drood.Select();

        /// <summary>Selects this view if it is in the game's list now (false: not there yet).</summary>
        internal bool Select()
        {
            var manager = CameraManagerScript.Instance;
            if (!Exists || manager == null)
            {
                return false;
            }

            if (!_mode.IsSelected)
            {
                manager.SelectCameraMode(_mode);
            }

            return true;
        }

        /// <summary>This view is in the game's list.</summary>
        internal bool Exists => _mode != null && _manager == CameraManagerScript.Instance;

        /// <summary>This view is the one selected.</summary>
        internal bool Selected => _mode != null && _manager == CameraManagerScript.Instance && _mode.IsSelected;

        /// <summary>Adds the view while <paramref name="wanted"/>, takes it out otherwise.</summary>
        internal void Ensure(bool wanted)
        {
            var manager = CameraManagerScript.Instance;
            if (manager != _manager)
            {
                // A new flight scene: its camera list is new.
                _manager = manager;
                _mode = null;
                _reportedList = false;
            }

            if (manager == null || _failed)
            {
                return;
            }

            if (wanted && _mode == null)
            {
                Add(manager);
            }
            else if (!wanted && _mode != null)
            {
                Remove(manager);
            }
        }

        private void Add(CameraManagerScript manager)
        {
            var modes = manager.CameraModes as IList;
            if (modes == null)
            {
                return;
            }

            // The drood's external view ("Third Person"): the first enabled view on an orbit
            // camera. The new view goes after the last enabled view, so the view keys cycle
            // through it after the drood's own views (third person, first person).
            var orbitIndex = -1;
            var lastEnabled = -1;
            for (var i = 0; i < modes.Count; i++)
            {
                var mode = modes[i] as CameraMode;
                if (mode?.CameraController == null || !mode.CameraController.Enabled)
                {
                    continue;
                }

                lastEnabled = i;
                if (orbitIndex < 0 && DroodReflection.IsOrbitCamera(mode.CameraController))
                {
                    orbitIndex = i;
                }
            }

            if (orbitIndex < 0)
            {
                // The list may not be filled in yet: try again later, and say once what it holds.
                if (!_reportedList)
                {
                    _reportedList = true;
                    var listed = new System.Text.StringBuilder();
                    foreach (var item in modes)
                    {
                        var mode = item as CameraMode;
                        listed.Append(mode == null ? " | (not a CameraMode)" : $" | '{mode.Name}' {mode.CameraController?.GetType().Name} {mode.SubMode} enabled {mode.CameraController?.Enabled}");
                    }

                    Debug.LogWarning($"[Drood 2.0] No external (orbit) camera view yet for the '{Name}' view; the views:{listed}");
                }

                return;
            }

            var orbit = (CameraMode)modes[orbitIndex];
            try
            {
                // It adds itself to the controller's modes.
                _mode = new CameraMode(Name, orbit.CameraController, orbit.SubMode) { IsHidden = false };
                modes.Insert(lastEnabled + 1, _mode);
                Announce(manager, DroodReflection.CustomCameraModeAddedField, _mode);
                DroodLog.Debug($"Camera view '{Name}' added (on the '{orbit.Name}' camera) after '{((CameraMode)modes[lastEnabled]).Name}'.");
            }
            catch (System.Exception e)
            {
                _failed = true;
                _mode = null;
                Debug.LogError($"[Drood 2.0] Couldn't add the '{Name}' camera view: {e.GetBaseException().Message}");
            }
        }

        private void Remove(CameraManagerScript manager)
        {
            var modes = manager.CameraModes as IList;
            var mode = _mode;
            _mode = null;
            if (modes == null || mode == null)
            {
                return;
            }

            if (mode.IsSelected)
            {
                // Back to the external view first: the first enabled view on the same camera.
                foreach (var item in modes)
                {
                    if (item is CameraMode other && other != mode && other.CameraController == mode.CameraController
                        && other != Canopy._mode && other != Drood._mode)
                    {
                        manager.SelectCameraMode(other);
                        break;
                    }
                }
            }

            modes.Remove(mode);
            (mode.CameraController?.CameraModes as IList)?.Remove(mode);
            Announce(manager, DroodReflection.CustomCameraModeRemovedField, mode);
        }

        // The camera manager's events for views added and removed after the start (raised by the
        // game for camera parts, CameraManagerScript.RegisterCustomCameraVantage); the flight
        // view panel lists each such view as a button in its camera list, and takes it out again.
        /// <summary>Raises one of those events for <paramref name="mode"/>, as the game does for a camera part.</summary>
        private void Announce(CameraManagerScript manager, System.Reflection.FieldInfo field, CameraMode mode)
        {
            try
            {
                var handlers = field?.GetValue(manager) as System.Delegate;
                handlers?.DynamicInvoke(mode);
                DroodLog.Debug($"View panel: '{Name}' {(field == DroodReflection.CustomCameraModeAddedField ? "added" : "removed")} ({(field == null ? "no such event" : handlers == null ? "no listener" : $"{handlers.GetInvocationList().Length} listener(s)")}).");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] The view panel didn't take the '{Name}' view: {e.GetBaseException().Message}");
            }
        }
    }
}
