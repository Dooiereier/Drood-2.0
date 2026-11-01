using System.Collections;
using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Flight.GameView.Cameras;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The Course Camera: a camera part's camera on Drood 2.0 (the part's second CameraVantage,
    /// appended in its XML; the first is the drood's eyes, which GetModifier and the drood's own
    /// first person use), so the game lists it with the camera parts (the view panel's camera
    /// list) and other mods that collect camera parts (PlusOne, HorseEye) find it too. The game
    /// registers it when the drood's craft is the active one, under the part's name; it is
    /// renamed CameraName. While the drood has a course it stands beside it, EyeHeight above the
    /// course's own height (the swoop's starting line, the gate's ground point: 20 m past the gate,
    /// 50 m to its left; the drop zone's target: 50 m east of it; see their CameraSpot), turned to follow the drood and zoomed to it:
    /// its field of view keeps the drood under its canopy (FrameCanopy high), or on its own
    /// (FrameDrood), FrameShare of the view's height, within MinFieldOfView..MaxFieldOfView,
    /// followed over ZoomSeconds. Without a course it stays where the part XML puts it (behind
    /// and above the drood). While a drood is in control the game switches the camera parts'
    /// views off; this one is switched back on for Drood 2.0.
    /// The game puts the camera point back at the XML's offset from its parent every frame
    /// (CameraVantageScript.FlightUpdate) before it reads it for the view, so while filming the
    /// point hangs from a mount of our own (its offset taken out of the mount's place), and
    /// goes back onto the part without a course.
    /// Only with a course is it a camera view at all: the game registers a part camera whenever
    /// its craft becomes the active one (a drood on a plane, every drood aboard), so without a
    /// course it is taken out of the camera list again (UnregisterFromCameraManager), and put
    /// back (Register) when one starts, the drood's craft being the player's. A view being
    /// watched stays until the player looks elsewhere.
    /// </summary>
    public class DroodCourseCamera : MonoBehaviour
    {
        internal const string CameraName = "Course Camera";

        private const float EyeHeight = 3.5f;
        // The least height above the ground the camera craft is spawned at (a spot below the ground
        // there is still placed exactly, after spawning).
        private const float MinAboveGround = 1f;
        private const float GroundRayAbove = 100f;
        private const float GroundRayLength = 200f;
        // The aim: the drood, or this far up its lines while the canopy is out.
        private const float AimUpLines = 0.4f;
        private const float FrameCanopy = 12f;
        private const float FrameDrood = 3f;
        private const float FrameShare = 0.5f;
        private const float MinFieldOfView = 1.5f;
        private const float MaxFieldOfView = 60f;
        // Without a course: the part XML's own field of view.
        private const float RestFieldOfView = 60f;
        private const float ZoomSeconds = 0.4f;
        private const float MinAimDistance = 1f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript _eva;
        private CameraVantageScript _vantage;
        private DroodSwoopCourse _swoop;
        private DroodDropZone _dropZone;
        private DroodParachute _parachute;
        private Rigidbody _body;
        private float _fieldOfView = RestFieldOfView;
        private float _fieldOfViewRate;
        private float _aboveGround = EyeHeight;
        private bool _filming;
        private CameraController _named;
        private Transform _mount;

        // The real camera beside the course, for mods that collect camera parts.
        private readonly DroodCourseCameraCraft _cameraCraft = new DroodCourseCameraCraft();
        private bool _hadCourse;
        private bool _swept;
        private Transform _pointParent;

        /// <summary>
        /// Late chain, after the canopy is drawn (the drood's craft in control): the camera
        /// placed, aimed and zoomed, its view named and switched on.
        /// </summary>
        internal void Step()
        {
            _eva = _eva != null ? _eva : GetComponent<Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript>();
            _vantage = _vantage != null ? _vantage : SecondVantage();
            if (_vantage == null || _eva == null)
            {
                return;
            }

            SetRegistered(_eva.IsPlayerCraft && Spot(out _, out _));
            if (!_eva.IsPlayerCraft)
            {
                return;
            }

            if (!_swept)
            {
                _swept = true;
                _cameraCraft.Sweep(Spot(out _, out _));
            }

            var controller = _vantage.CameraController;
            if (controller != null)
            {
                Announce(controller);
            }

            var point = _vantage.CameraPosition;
            if (point == null)
            {
                return;
            }

            float wanted;
            if (Aim(out var spot, out var view, out var distance, out var size))
            {
                _aimOffset = spot + view * Vector3.forward * distance - transform.position;
                _aimUp = view * Vector3.up;
                Mount(point, spot, view);
                wanted = Mathf.Clamp(2f * Mathf.Atan(size / (2f * FrameShare * distance)) * Mathf.Rad2Deg, MinFieldOfView, MaxFieldOfView);
                if (!_filming)
                {
                    _filming = true;
                    _fieldOfView = wanted;
                    _fieldOfViewRate = 0f;
                }
            }
            else
            {
                Unmount(point);
                _filming = false;
                wanted = RestFieldOfView;
            }

            _fieldOfView = Mathf.SmoothDamp(_fieldOfView, wanted, ref _fieldOfViewRate, ZoomSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
            if (!Mathf.Approximately(_fieldOfView, _writtenFieldOfView))
            {
                _writtenFieldOfView = _fieldOfView;
                DroodReflection.SetFieldOfView(_vantage.Data, _fieldOfView);
            }

            if (_filming)
            {
                _hadCourse = true;
                _cameraCraft.Place(_eva.PartScript?.CraftScript, point.position, point.rotation, _aboveGround);
            }
            else if (_hadCourse && !Spot(out _, out _))
            {
                _hadCourse = false;
                _cameraCraft.Remove();
            }

            // The game reads the field of view when the view is selected: while it is, set live.
            if (controller != null && CameraManagerScript.Instance?.CurrentCameraController == controller)
            {
                var camera = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera;
                if (camera != null)
                {
                    camera.FieldOfView = _fieldOfView;
                }

                // The game places this camera at the start of the next frame from the point as
                // set now (FirstPersonCameraController.UpdateCameraPosition: its parent at the
                // point, its camera turned as the point), before that frame's poses: a frame
                // behind the drood, which the zoom shows as the canopy jumping back and forth.
                // Placed here too, the frame renders with this frame's view.
                if (_filming && DroodReflection.FirstPersonParentField?.GetValue(controller) is Transform parent && controller.CameraTransform != null)
                {
                    parent.position = point.position;
                    controller.CameraTransform.rotation = point.rotation;
                }
            }
        }

        /// <summary>
        /// The camera point at <paramref name="spot"/>, turned to <paramref name="view"/>: hung
        /// from the mount, the mount placed so that the point, at the XML's offset from it, lands
        /// there.
        /// </summary>
        private void Mount(Transform point, Vector3 spot, Quaternion view)
        {
            if (_mount == null)
            {
                _mount = new GameObject("Drood2.CourseCameraMount").transform;
            }

            if (point.parent != _mount)
            {
                _pointParent = point.parent;
                point.SetParent(_mount, false);
            }

            point.localPosition = _vantage.Data.CameraOffset;
            point.localRotation = Quaternion.identity;
            _mount.SetPositionAndRotation(spot - view * _vantage.Data.CameraOffset, view);
        }

        /// <summary>The camera point back on the part, at the XML's offset and turn.</summary>
        private void Unmount(Transform point)
        {
            if (_mount == null || point.parent != _mount)
            {
                return;
            }

            point.SetParent(_pointParent != null ? _pointParent : _vantage.transform, false);
            point.localPosition = _vantage.Data.CameraOffset;
            point.localRotation = Quaternion.Euler(_vantage.Data.CameraRotationOffset);
        }

        /// <summary>The game moved the world (recentring): the mount and the camera craft's place with it.</summary>
        internal void OnFrameShift(Vector3 positionDelta)
        {
            if (_mount != null)
            {
                _mount.position += positionDelta;
            }

            _cameraCraft.OnFrameShift(positionDelta);
        }

        // The aim point's offset from the drood as last placed (late chain), for the early aim.
        private Vector3 _aimOffset;
        private Vector3 _aimUp = Vector3.up;

        /// <summary>
        /// Game-loop Update: the camera craft turned to this frame's drood already (its
        /// interpolated place is set before Update), so mods that read the camera part in their
        /// Update (HorseEye) aren't a frame behind; the late chain then sets it exactly.
        /// </summary>
        internal void EarlyStep()
        {
            if (_filming)
            {
                _cameraCraft.AimEarly(transform.position + _aimOffset, _aimUp);
            }
        }

        /// <summary>Physics step: the camera craft held at its place, by the player's drood only.</summary>
        internal void PhysicsStep()
        {
            if (_eva != null && _eva.IsPlayerCraft)
            {
                _cameraCraft.Hold();
            }
        }

        private void OnDestroy()
        {
            if (_vantage != null && _vantage.CameraPosition != null)
            {
                Unmount(_vantage.CameraPosition);
            }

            if (_mount != null)
            {
                Destroy(_mount.gameObject);
            }

            _cameraCraft.Forget();
        }

        private float _writtenFieldOfView = float.NaN;
        private bool _registeredWanted;
        private bool _registeredAsWanted;

        /// <summary>The part camera in the game's camera list or out of it (not while it is the view watched).</summary>
        private void SetRegistered(bool wanted)
        {
            // The game's flag read only when the wish changes (or the last change had to wait).
            if (_registeredAsWanted && wanted == _registeredWanted)
            {
                return;
            }

            _registeredWanted = wanted;
            _registeredAsWanted = false;
            if (!DroodReflection.CameraRegistrationAvailable)
            {
                return;
            }

            if (DroodReflection.CameraRegisteredField.GetValue(_vantage) is bool registered && registered == wanted)
            {
                _registeredAsWanted = true;
                return;
            }

            if (!wanted)
            {
                var controller = _vantage.CameraController;
                if (controller != null && CameraManagerScript.Instance?.CurrentCameraController == controller)
                {
                    return;
                }

                if (_vantage.CameraPosition != null)
                {
                    Unmount(_vantage.CameraPosition);
                }

                _filming = false;
                _named = null;
                DroodReflection.CameraUnregisterMethod.Invoke(_vantage, null);
                _registeredAsWanted = true;
                DroodLog.Debug("Course Camera taken out of the camera views (no course).");
                return;
            }

            DroodReflection.CameraRegisterMethod.Invoke(_vantage, null);
            _registeredAsWanted = true;
            DroodLog.Debug("Course Camera back in the camera views (a course).");
        }

        /// <summary>The part's second camera (the first is the drood's eyes).</summary>
        private CameraVantageScript SecondVantage()
        {
            var vantages = GetComponents<CameraVantageScript>();
            return vantages.Length > 1 ? vantages[1] : null;
        }

        /// <summary>
        /// Its view named CameraName (once per registration) and switched on with its panel
        /// button (the game switches camera parts off while a drood is in control).
        /// </summary>
        private void Announce(CameraController controller)
        {
            var manager = CameraManagerScript.Instance;
            if (manager == null || !(manager.CameraModes is IList modes))
            {
                return;
            }

            CameraMode mode = null;
            for (var i = 0; i < modes.Count; i++)
            {
                if (modes[i] is CameraMode candidate && candidate.CameraController == controller)
                {
                    mode = candidate;
                    break;
                }
            }

            if (mode == null)
            {
                return;
            }

            if (_named != controller)
            {
                _named = controller;

                // Selected, a part camera hides its own part (FirstPersonCameraController.OnSelected,
                // DisablePartMaterial): here the drood, with the canopy drawn on its materials.
                if (controller is FirstPersonCameraController firstPerson)
                {
                    firstPerson.DisablePartMaterial = false;
                }

                mode.Name = CameraName;
                DroodFlyByCameras.SetButtonText(mode, CameraName);
                DroodLog.Debug($"Course Camera registered as a camera view ('{CameraName}').");
            }

            if (!controller.Enabled)
            {
                controller.SetEnabled(true, true);
                DroodFlyByCameras.ShowButton(mode);
            }
        }

        private bool Aim(out Vector3 spot, out Quaternion view, out float distance, out float size)
        {
            view = Quaternion.identity;
            distance = size = 0f;
            if (!Spot(out spot, out var up))
            {
                return false;
            }

            var ground = OnGround(spot, up);
            spot += up * EyeHeight;
            // Its height above the ground there (where the camera craft is spawned), at least MinAboveGround.
            _aboveGround = Mathf.Max(Vector3.Dot(spot - ground, up), MinAboveGround);
            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            var canopy = _parachute != null && _parachute.CanopyOut;
            var target = canopy ? _parachute.UpTheLines(AimUpLines) : transform.position;
            var look = target - spot;
            distance = look.magnitude;
            if (distance < MinAimDistance)
            {
                return false;
            }

            view = Quaternion.LookRotation(look / distance, up);
            size = canopy ? FrameCanopy : FrameDrood;
            return true;
        }

        // The course's camera spot, at the course's own height.
        private bool Spot(out Vector3 spot, out Vector3 up)
        {
            _swoop = _swoop != null ? _swoop : GetComponent<DroodSwoopCourse>();
            _dropZone = _dropZone != null ? _dropZone : GetComponent<DroodDropZone>();
            spot = up = Vector3.zero;
            return (_swoop != null && _swoop.CameraSpot(out spot, out up))
                || (_dropZone != null && _dropZone.CameraSpot(out spot, out up));
        }

        /// <summary>The ground below <paramref name="point"/> (its own drood skipped), or the point itself.</summary>
        private Vector3 OnGround(Vector3 point, Vector3 up)
        {
            _body = _body != null ? _body : GetComponentInParent<Rigidbody>();
            var count = Physics.RaycastNonAlloc(point + up * GroundRayAbove, -up, _hits, GroundRayLength, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            var ground = point;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.distance < best && (_body == null || hit.rigidbody != _body))
                {
                    best = hit.distance;
                    ground = hit.point;
                }
            }

            return ground;
        }
    }
}
