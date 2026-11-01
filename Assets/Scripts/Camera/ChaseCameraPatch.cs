using System.Reflection;
using HarmonyLib;

namespace Drood2
{
    /// <summary>
    /// The chase views' last word on the camera. The game turns its orbit camera at the very
    /// end of each frame (GameViewScript.EndFrame → CameraManagerScript.UpdateCamera →
    /// OrbitCameraController.UpdateCamera), after every game-loop step, from the third-person
    /// view's frame, which follows the drood; so right after that the camera's pivot is turned to
    /// the chase view (<see cref="DroodChaseCamera.ChaseView"/>) and moved to the point it orbits
    /// (<see cref="DroodChaseCamera.ChaseCentre"/>, up the lines or the drood). Only while a chase
    /// view is selected and a Drood 2.0 has its canopy out; other views and crafts are
    /// untouched.
    /// While a chase view steers it, the camera isn't counted off centre (no recentre button).
    /// It also holds the orbit camera still for a moment when the mouse joystick switches with a
    /// Drood 2.0 in control (<see cref="DroodCameraViews"/>).
    /// </summary>
    [HarmonyPatch]
    public static class ChaseCameraPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("Assets.Scripts.Flight.GameView.Cameras.OrbitCameraController"), "UpdateCamera");

        private static void Postfix(object __instance)
        {
            // Held after a mouse joystick switch (DroodCameraViews): the rotation from before it.
            if (DroodCameraViews.HeldFrames > 0 && __instance == DroodCameraViews.HeldCamera)
            {
                DroodCameraViews.HeldFrames--;
                DroodReflection.SetOrbitRotation(__instance, DroodCameraViews.HeldRotation);
                var held = DroodReflection.OrbitPivot(__instance);
                if (held != null && DroodChaseCamera.ChaseCamera != __instance)
                {
                    held.rotation = DroodReflection.OrbitNeutralRotation(__instance) * UnityEngine.Quaternion.Euler(DroodCameraViews.HeldRotation);
                }
            }

            if (DroodChaseCamera.ChaseCamera == null || __instance != DroodChaseCamera.ChaseCamera || DroodChaseCameraMode.SelectedMode == null)
            {
                return;
            }

            var pivot = DroodReflection.OrbitPivot(__instance);
            if (pivot == null)
            {
                return;
            }

            pivot.rotation = DroodChaseCamera.ChaseView;

            // The camera orbits a point up the lines, not the drood.
            pivot.position = DroodChaseCamera.ChaseCentre;

            // The game counts its orbit camera off centre when rolled (more than 0.1°) or
            // panned, and the view panel then shows its recentre button; a chase view turns the
            // camera itself, so it isn't off centre.
            OffCenterSetter?.Invoke(__instance, NotOffCenter);
        }

        private static readonly MethodInfo OffCenterSetter = AccessTools.PropertySetter(
            AccessTools.TypeByName("Assets.Scripts.Flight.GameView.Cameras.CameraController"), "IsOffCenter");

        private static readonly object[] NotOffCenter = { false };
    }
}
