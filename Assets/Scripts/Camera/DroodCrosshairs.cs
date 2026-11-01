using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using Assets.Scripts.Flight.GameView.Cameras;

namespace Drood2
{
    /// <summary>
    /// The game's crosshairs (the tether aim in the middle of the screen) only in the drood's own
    /// views: with a Drood 2.0 in control they are hidden in any other camera view (the chase,
    /// course and fly-by views, camera parts), and the game's own choice is restored on returning to first or third
    /// person (EvaScript.UpdateCrosshairsVisibility; the game shows them for a drood with its
    /// grappling hook on, whatever the view).
    /// </summary>
    internal static class DroodCrosshairs
    {
        private static bool _hidden;

        internal static void ForgetFlightScene()
        {
            _hidden = false;
        }

        /// <summary>Called once a frame from the flight game loop (<see cref="DroodCameraViews"/>).</summary>
        internal static void Step(bool drood2InControl)
        {
            var ui = Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI as Assets.Scripts.Flight.UI.FlightSceneInterfaceScript;
            var crosshairs = ui != null ? ui.Crosshairs : null;
            if (crosshairs == null)
            {
                return;
            }

            var camera = CameraManagerScript.Instance?.CurrentCameraController;
            var ownView = IsDroodEyes(camera as FirstPersonCameraController)
                || (DroodReflection.IsOrbitCamera(camera) && DroodChaseCameraMode.SelectedMode == null);
            if (drood2InControl && !ownView)
            {
                if (crosshairs.enabled)
                {
                    crosshairs.enabled = false;
                    _hidden = true;
                }

                return;
            }

            if (_hidden)
            {
                _hidden = false;
                EvaScript.UpdateCrosshairsVisibility(null);
            }
        }

        // First person through a drood's eyes: the first camera on a drood part. The Course Camera
        // (its second) and camera parts use the same controller type.
        private static bool IsDroodEyes(FirstPersonCameraController camera)
        {
            var vantage = camera?.CameraVantageScript;
            var part = vantage != null ? vantage.PartScript : null;
            return part != null && part.GetModifier<EvaScript>() != null && part.GetModifier<CameraVantageScript>() == vantage;
        }
    }
}
