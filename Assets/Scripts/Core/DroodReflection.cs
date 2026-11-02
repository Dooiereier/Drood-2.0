using System.Reflection;
using Assets.Scripts.Animation;
using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using Assets.Scripts.Flight.GameView.Cameras;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The private game members Drood 2.0's flight behaviours read, with plain reflection (not
    /// HarmonyLib), so the parachute, the poses and the sounds work without the Juno Harmony
    /// mod. Looked up once and checked at mod init (<see cref="Validate"/>): a member a game
    /// update renamed is reported then, and its feature quietly does without (the accessors
    /// return null). The Harmony patches and the tethers keep their own lookups (HarmonyLib's),
    /// and their members are checked here too, so one line lists everything a game update broke.
    /// </summary>
    internal static class DroodReflection
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // EvaScript's animation controller (the calm animator, the run-out) and its first-person
        // camera controller (the first-person body), private with no public accessor.
        private static readonly FieldInfo CrewAnimControllerField = typeof(EvaScript).GetField("_crewAnimController", Instance);
        private static readonly FieldInfo FpsCameraControllerField = typeof(EvaScript).GetField("_fpsCameraController", Instance);
        private static readonly FieldInfo EvaRenderersField = typeof(EvaScript).GetField("_renderers", Instance);
        private static readonly MethodInfo UnloadFromCrewCompartmentMethod = typeof(EvaScript).GetMethod("UnloadFromCrewCompartment", Instance);
        private static readonly MethodInfo ActivateEvaForDisconnectedMethod = typeof(EvaScript).GetMethod("ActivateEvaForDisconnectedAstronaut", Instance);
        private static readonly object[] SetTransformArgument = { true };

        // CameraVantageData's field of view (the first-person setting), private with no setter.
        private static readonly FieldInfo FieldOfViewField = typeof(CameraVantageData).GetField("_fieldOfView", Instance);

        internal static void SetFieldOfView(CameraVantageData vantage, float degrees)
        {
            if (vantage != null && FieldOfViewField != null)
            {
                FieldOfViewField.SetValue(vantage, degrees);
            }
        }

        // The orbit (third-person) camera (an internal class): its target rotation (Euler, eased
        // toward by the camera), its reference plane and the plane's neutral rotation, for the
        // canopy chase view.
        private static readonly System.Type OrbitCameraType =
            typeof(CameraManagerScript).Assembly.GetType("Assets.Scripts.Flight.GameView.Cameras.OrbitCameraController");
        private static readonly FieldInfo OrbitTargetRotationField = OrbitCameraType?.GetField("_targetRotation", Instance);
        private static readonly FieldInfo OrbitCurrentRotationField = OrbitCameraType?.GetField("_currentRotation", Instance);
        private static readonly FieldInfo OrbitParentTransformField = OrbitCameraType?.GetField("_parentTransform", Instance);
        private static readonly FieldInfo OrbitReferencePlaneField = OrbitCameraType?.GetField("_referencePlane", Instance);
        private static readonly MethodInfo OrbitNeutralRotationMethod = OrbitCameraType?.GetMethod("GetNeutralCameraRotation", Instance);

        internal static bool OrbitCameraAvailable =>
            OrbitTargetRotationField != null && OrbitCurrentRotationField != null && OrbitParentTransformField != null
            && OrbitReferencePlaneField != null && OrbitNeutralRotationMethod != null;

        /// <summary>The orbit camera's pivot (the camera hangs from it at its zoom distance).</summary>
        internal static Transform OrbitPivot(object camera) => OrbitParentTransformField.GetValue(camera) as Transform;

        /// <summary>Whether <paramref name="camera"/> is the orbit (third-person) camera.</summary>
        internal static bool IsOrbitCamera(object camera) => camera != null && OrbitCameraAvailable && camera.GetType() == OrbitCameraType;

        /// <summary>The orbit camera's current rotation (Euler, its own frame).</summary>
        internal static Vector3 OrbitCurrentRotation(object camera) => (Vector3)OrbitCurrentRotationField.GetValue(camera);

        /// <summary>The orbit camera's target rotation (Euler, its own frame).</summary>
        internal static Vector3 OrbitTargetRotation(object camera) => (Vector3)OrbitTargetRotationField.GetValue(camera);

        /// <summary>Sets the orbit camera's rotation now and as its target (no easing of its own).</summary>
        internal static void SetOrbitRotation(object camera, Vector3 euler)
        {
            OrbitTargetRotationField.SetValue(camera, euler);
            OrbitCurrentRotationField.SetValue(camera, euler);
        }

        /// <summary>The rotation the orbit camera's Euler angles are measured from.</summary>
        internal static Quaternion OrbitNeutralRotation(object camera) =>
            (Quaternion)OrbitNeutralRotationMethod.Invoke(camera, OrbitArgument(OrbitReferencePlaneField.GetValue(camera)));

        private static readonly object[] OrbitArguments = new object[1];

        private static object[] OrbitArgument(object value)
        {
            OrbitArguments[0] = value;
            return OrbitArguments;
        }

        // The camera views (DroodFlyByCameras, DroodChaseCameraMode, DroodCourseCamera).
        internal static readonly System.Type FlyByCameraType =
            typeof(CameraManagerScript).Assembly.GetType("Assets.Scripts.Flight.GameView.Cameras.FlyByCameraController");
        internal static readonly FieldInfo FlyByModeField = FlyByCameraType?.GetField("_mode", Instance);
        internal static readonly FieldInfo ViewPanelButtonMapField = typeof(Assets.Scripts.Flight.UI.ViewPanelController).GetField("_buttomMap", Instance);
        internal static readonly FieldInfo CustomCameraModeAddedField = typeof(CameraManagerScript).GetField("CustomCameraModeAdded", Instance);
        internal static readonly FieldInfo CustomCameraModeRemovedField = typeof(CameraManagerScript).GetField("CustomCameraModeRemoved", Instance);
        internal static readonly FieldInfo FirstPersonParentField = typeof(FirstPersonCameraController).GetField("_parentTransform", Instance);
        internal static readonly MethodInfo CameraRegisterMethod = typeof(CameraVantageScript).GetMethod("Register", Instance, null, System.Type.EmptyTypes, null);
        internal static readonly MethodInfo CameraUnregisterMethod = typeof(CameraVantageScript).GetMethod("UnregisterFromCameraManager", Instance);
        internal static readonly FieldInfo CameraRegisteredField = typeof(CameraVantageScript).GetField("_registered", Instance);

        internal static bool CameraRegistrationAvailable =>
            CameraRegisterMethod != null && CameraUnregisterMethod != null && CameraRegisteredField != null;

        // A part's "others can't disconnect from me" setting (no setter).
        private static readonly FieldInfo PreventDisconnectField = typeof(ModApi.Craft.Parts.Modifiers.ConfigData).GetField("_collisionPreventExternalDisconnections", Instance);

        internal static bool CanSetPreventDisconnect => PreventDisconnectField != null;

        /// <summary>Sets that setting; its value before.</summary>
        internal static bool SetPreventDisconnect(ModApi.Craft.Parts.PartData part, bool prevent)
        {
            var before = part.Config.CollisionPreventExternalDisconnections;
            if (PreventDisconnectField != null && part.Config is ModApi.Craft.Parts.Modifiers.ConfigData config)
            {
                PreventDisconnectField.SetValue(config, prevent);
            }

            return before;
        }

        // A part's place in its craft (setters not public).
        private static readonly MethodInfo PartPositionSetter = typeof(ModApi.Craft.Parts.PartData).GetProperty(nameof(ModApi.Craft.Parts.PartData.Position))?.GetSetMethod(true);
        private static readonly MethodInfo PartRotationSetter = typeof(ModApi.Craft.Parts.PartData).GetProperty(nameof(ModApi.Craft.Parts.PartData.Rotation))?.GetSetMethod(true);
        private static readonly object[] ZeroArgument = { Vector3.zero };

        /// <summary>A part's saved place in its craft put at the craft's origin.</summary>
        internal static void ZeroPartPlace(ModApi.Craft.Parts.PartData part)
        {
            PartPositionSetter?.Invoke(part, ZeroArgument);
            PartRotationSetter?.Invoke(part, ZeroArgument);
        }

        internal static CrewAnimController CrewAnimController(EvaScript eva) =>
            eva != null && CrewAnimControllerField != null ? CrewAnimControllerField.GetValue(eva) as CrewAnimController : null;

        /// <summary>The character's own renderers, as the game lists them (null: not available).</summary>
        internal static Renderer[] EvaRenderers(EvaScript eva) =>
            eva != null && EvaRenderersField != null ? EvaRenderersField.GetValue(eva) as Renderer[] : null;

        internal static FirstPersonCameraController FpsCameraController(EvaScript eva) =>
            eva != null && FpsCameraControllerField != null ? FpsCameraControllerField.GetValue(eva) as FirstPersonCameraController : null;

        /// <summary>
        /// The drood out of its seat or crew compartment without the player taking control of it:
        /// the game's exit (EvaScript.TakeControl) without its switch of control:
        /// UnloadFromCrewCompartment (at the compartment's exit point), then on EVA as a
        /// disconnected astronaut (ActivateEvaForDisconnectedAstronaut, the designer's path), then
        /// off the compartment's crew list (UnloadCrewMember, which raises CrewExit: without it a
        /// chair keeps pulling the hands and feet within its snap range to its control targets).
        /// False if either is missing.
        /// </summary>
        internal static bool LeaveSeat(EvaScript eva)
        {
            if (eva == null || UnloadFromCrewCompartmentMethod == null || ActivateEvaForDisconnectedMethod == null)
            {
                return false;
            }

            var compartment = eva.CrewCompartment;
            UnloadFromCrewCompartmentMethod.Invoke(eva, SetTransformArgument);
            ActivateEvaForDisconnectedMethod.Invoke(eva, null);
            compartment?.UnloadCrewMember(eva, false);
            return true;
        }

        private const string Chase = "the canopy chase camera";
        private const string Views = "the camera views";
        private const string Tethers = "persistent tethers";
        private const string Seats = "releases of a seated drood";

        /// <summary>
        /// One line with every member that wasn't found (always logged: settings aren't loaded at
        /// mod init).
        /// </summary>
        internal static void Validate()
        {
            var eva = typeof(EvaScript);
            var missing = new System.Text.StringBuilder();
            Check(missing, CrewAnimControllerField, "EvaScript._crewAnimController", "the calm hanging animation and the run-out's leg animation");
            Check(missing, FpsCameraControllerField, "EvaScript._fpsCameraController", "the drood's own body and fixed eye point in first person");
            Check(missing, EvaRenderersField, "EvaScript._renderers", "the invisible toggle");
            Check(missing, FieldOfViewField, "CameraVantageData._fieldOfView", "the first-person field of view setting");
            Check(missing, UnloadFromCrewCompartmentMethod, "EvaScript.UnloadFromCrewCompartment", Seats);
            Check(missing, ActivateEvaForDisconnectedMethod, "EvaScript.ActivateEvaForDisconnectedAstronaut", Seats);
            Check(missing, OrbitCameraType, "OrbitCameraController", Chase);
            Check(missing, OrbitTargetRotationField, "OrbitCameraController._targetRotation", Chase);
            Check(missing, OrbitReferencePlaneField, "OrbitCameraController._referencePlane", Chase);
            Check(missing, OrbitCurrentRotationField, "OrbitCameraController._currentRotation", Chase);
            Check(missing, OrbitParentTransformField, "OrbitCameraController._parentTransform", Chase);
            Check(missing, OrbitNeutralRotationMethod, "OrbitCameraController.GetNeutralCameraRotation", Chase);
            Check(missing, FlyByCameraType, "FlyByCameraController", "the stationary fly-by camera");
            Check(missing, FlyByModeField, "FlyByCameraController._mode", "the stationary fly-by camera");
            Check(missing, ViewPanelButtonMapField, "ViewPanelController._buttomMap", Views);
            Check(missing, CustomCameraModeAddedField, "CameraManagerScript.CustomCameraModeAdded", Views);
            Check(missing, CustomCameraModeRemovedField, "CameraManagerScript.CustomCameraModeRemoved", Views);
            Check(missing, FirstPersonParentField, "FirstPersonCameraController._parentTransform", "the Course Camera");
            Check(missing, CameraRegisterMethod, "CameraVantageScript.Register", "the Course Camera");
            Check(missing, CameraUnregisterMethod, "CameraVantageScript.UnregisterFromCameraManager", "the Course Camera");
            Check(missing, CameraRegisteredField, "CameraVantageScript._registered", "the Course Camera");
            Check(missing, PreventDisconnectField, "ConfigData._collisionPreventExternalDisconnections", "keeping carried parts in a release");
            Check(missing, PartPositionSetter, "PartData.Position's setter", "a release in flight");
            Check(missing, PartRotationSetter, "PartData.Rotation's setter", "a release in flight");

            // Those of the Harmony patches and the tethers, by their names.
            Check(missing, eva.GetField("_grapplingHook", Instance), "EvaScript._grapplingHook", Tethers);
            Check(missing, eva.GetField("_evaShootTetherProcessed", Instance), "EvaScript._evaShootTetherProcessed", Tethers);
            Check(missing, eva.GetProperty("GrapplingHook", Instance), "EvaScript.GrapplingHook", Tethers);
            Check(missing, eva.GetMethod("GrapplingHookPartDestroyed", Instance), "EvaScript.GrapplingHookPartDestroyed", Tethers);
            Check(missing, eva.GetMethod("DestroyGrapplingHook", Instance), "EvaScript.DestroyGrapplingHook", Tethers);
            Check(missing, eva.GetField("MaxTetherLength", Instance | BindingFlags.Static), "EvaScript.MaxTetherLength", Tethers);
            Check(missing, eva.GetMethod("UpdateGrapplingHook", Instance), "EvaScript.UpdateGrapplingHook", Tethers);
            Check(missing, eva.GetMethod("UpdateKinematicTurning", Instance), "EvaScript.UpdateKinematicTurning", "free look under the canopy");
            if (missing.Length > 0)
            {
                Debug.LogError($"[Drood 2.0] Game members not found (a game update?), these features are off: {missing}");
            }
        }

        private static void Check(System.Text.StringBuilder missing, object member, string name, string feature)
        {
            if (member == null)
            {
                missing.Append(name).Append(" (").Append(feature).Append("); ");
            }
        }
    }
}
