using ModApi.GameLoop;
using ModApi.GameLoop.Interfaces;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Per flight scene, in the flight game loop: keeps the chase camera views
    /// (<see cref="DroodChaseCameraMode"/>) in the game's list while a Drood 2.0 is in control
    /// with its canopy out (from deployment until it lies folded after landing), and out of it
    /// otherwise (packed, another craft, or none; a selected chase view hands over to the
    /// external view); keeps the game's fly-by cameras on for it (<see cref="DroodFlyByCameras"/>)
    /// and the crosshairs to its own views (<see cref="DroodCrosshairs"/>);
    /// and holds the orbit camera still for a
    /// moment when the mouse joystick switches.
    /// </summary>
    public class DroodCameraViews : MonoBehaviour, IFlightUpdate
    {
        /// <summary>Game loop bookkeeping (IGameLoopItem).</summary>
        public bool StartMethodCalled { get; set; }

        private void Awake()
        {
            Assets.Scripts.Game.Instance?.FlightScene?.GameLoop?.Register(this);
        }

        /// <summary>
        /// The flight scene ends: the mod's static references into it (cameras, views, the chase's
        /// drood) are dropped, so the old craft and scene don't stay in memory until the next
        /// flight replaces them.
        /// </summary>
        private void OnDestroy()
        {
            Assets.Scripts.Game.Instance?.FlightScene?.GameLoop?.Unregister(this);
            HeldCamera = null;
            HeldFrames = 0;
            DroodChaseCameraMode.ForgetAll();
            DroodChaseCamera.ForgetFlightScene();
            DroodFlyByCameras.ForgetFlightScene();
            DroodCrosshairs.ForgetFlightScene();
            DroodDropChallengeHud.ForgetFlightScene();
            DroodSteeringOverlay.ForgetFlightScene();
            DroodFlareIndicator.ForgetFlightScene();
        }

        // Once per flight scene, when the player's craft is there: with a Drood 2.0 flown its course
        // camera clears the camera crafts of a save itself (it keeps one for a course); otherwise
        // they are removed here.
        private bool _sweptCameraCrafts;

        private void SweepCameraCrafts(bool drood2)
        {
            if (_sweptCameraCrafts || Assets.Scripts.Game.Instance?.FlightScene?.CraftNode?.CraftScript == null)
            {
                return;
            }

            _sweptCameraCrafts = true;
            if (!drood2)
            {
                DroodCourseCameraCraft.RemoveAll();
            }
        }

        // When the game's mouse joystick switches, the cursor is freed or captured, and the
        // orbit camera reads its jump as a drag: its rotation is held for HoldFrames frames
        // (applied right after its update, <see cref="ChaseCameraPatch"/>).
        private const int HoldFrames = 3;
        private bool _wasMouseJoystick;

        /// <summary>The orbit camera whose rotation is held, and how many more frames.</summary>
        internal static object HeldCamera { get; private set; }

        internal static int HeldFrames { get; set; }

        internal static UnityEngine.Vector3 HeldRotation { get; private set; }

        void IFlightUpdate.FlightUpdate(in FlightFrameData frame)
        {
            var eva = DroodFlyByCameras.PlayerEva();
            var drood2 = eva != null && eva.PartScript?.Data?.PartType?.Id == Drood2Part.PartTypeId;
            var drood = drood2 ? eva : null;
            SweepCameraCrafts(drood2);
            var parachute = drood != null ? drood.GetComponent<DroodParachute>() : null;
            var freeFall = drood != null ? drood.GetComponent<DroodFreeFall>() : null;
            DroodChaseCameraMode.EnsureAll(parachute != null && parachute.CanopyOut, freeFall != null && freeFall.Weight > 0f);
            DroodFlyByCameras.Step(eva, drood2);
            DroodCrosshairs.Step(drood2);

            var mouseJoystick = drood != null && DroodMouseSteering.IsOn(drood);
            if (mouseJoystick != _wasMouseJoystick)
            {
                var camera = Assets.Scripts.Flight.GameView.Cameras.CameraManagerScript.Instance?.CurrentCameraController;
                if (DroodReflection.IsOrbitCamera(camera))
                {
                    HeldCamera = camera;
                    HeldRotation = DroodReflection.OrbitCurrentRotation(camera);
                    HeldFrames = HoldFrames;
                }
            }

            _wasMouseJoystick = mouseJoystick;
        }
    }
}
