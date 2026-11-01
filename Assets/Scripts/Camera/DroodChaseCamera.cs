using Assets.Scripts.Flight.GameView.Cameras;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The chase camera views (<see cref="DroodChaseCameraMode"/>, after the external view in the
    /// game's list): while one is selected and Drood 2.0's canopy is out, the game's orbit
    /// camera (which the view uses) orbits a point three quarters up the lines ("Canopy Chase": the drood
    /// hangs and swings below it) or the drood ("Drood Chase") and looks along the canopy's
    /// heading, ChasePitchDown down (Drood Chase DroodChasePitchDown; the ram-air's Canopy Chase: right behind the wing, turning
    /// with it in pitch, yaw and roll; Canopy Chase's point and camera CanopyChaseLower lower,
    /// the view's direction the same), kept on the
    /// -velocity side; the direction and the centre follow critically damped (ChaseHeadingSeconds,
    /// ChaseCentreSeconds), so the view turns with the canopy, not with the drood's swing or
    /// facing, and stays smooth through quick rolls. It sets the camera's rotation (Step) and,
    /// after the game's camera update at the end of the frame, its pivot
    /// (<see cref="ChaseCameraPatch"/>, with Harmony); the zoom stays the player's. Dragging with
    /// a mouse button turns the view around the same centre; it stays turned until the view is
    /// changed (another camera view, or the chase ends).
    /// Near the ground the view swings up about the orbited point so the camera stays
    /// GroundClearance above the ground below it (AboveGround).
    /// Skydiving (the free-fall arch, before the canopy is out), Drood Chase looks down at the
    /// drood from above and behind, SkydivePitchDown below the horizon, the drood's head toward
    /// the top of the view.
    /// </summary>
    public class DroodChaseCamera : MonoBehaviour
    {
        private const float ChasePitchDown = 12f;
        private const float DroodChasePitchDown = 20f;
        // Canopy Chase's orbited point (and so its camera) this much (m) lower than up the lines,
        // along the view's own down.
        private const float CanopyChaseLower = 3f;
        private const float SkydivePitchDown = 75f;
        // Looking around never pitches the view past straight down.
        private const float MaxPitchDown = 89f;
        private const float ChaseHeadingSeconds = 0.2f;

        // The ram-air's Canopy Chase follows the wing's whole attitude (see Step).
        private Quaternion _wingView;
        private bool _hasWingView;

        // Above ChasePathAbove (m/s, horizontal) the view stays within ChaseMaxOffPath (degrees)
        // of the flight path; the camera orbits a point up the lines (the view's UpLines).
        private const float ChasePathAbove = 5f;
        private const float ChaseMaxOffPath = 60f;
        private const float ChaseCentreSeconds = 0.15f;

        private DroodParachute _parachute;
        private DroodFreeFall _freeFall;
        private Vector3 _heading;
        private Vector3 _headingVelocity;

        // Looking around (dragging with a mouse button held): degrees off the chase view, kept
        // until the view changes.
        private const float ChaseLookDegreesPerPixel = 0.25f;
        private const float ChaseLookPitchLimit = 70f;
        private DroodChaseCameraMode _lookMode;
        private float _lookYaw;
        private float _lookPitch;
        private Vector3 _lastMouse;
        private bool _wasDragging;

        /// <summary>The orbit camera the chase view is for this frame (null: no chase).</summary>
        internal static object ChaseCamera { get; private set; }

        /// <summary>This frame's chase view (world rotation), see <see cref="ChaseCameraPatch"/>.</summary>
        internal static Quaternion ChaseView { get; private set; }

        /// <summary>The drood whose chase this is (for <see cref="ChaseCentre"/>).</summary>
        private static DroodChaseCamera _active;

        // The orbit camera the chase last set. The chase views share it with the game's Third
        // Person view: once the chase stops it gets back the rotation it had when the chase
        // started (_rotationBefore), so Third Person looks the way it did; the chase's own turn
        // (behind the wing, rolled with it: the game's views never level a roll) is not left on it.
        private static object _rolledCamera;
        private static Vector3 _rotationBefore;

        // The orbited point's offset from the drood, smoothed (the lines' direction moves in
        // physics steps, coarser than frames).
        private Vector3 _centreOffset;
        private Vector3 _centreVelocity;
        private bool _hasCentreOffset;

        /// <summary>
        /// The point the camera orbits (up the lines, world), from the drood's position at the
        /// moment the camera is placed.
        /// </summary>
        internal static Vector3 ChaseCentre => _active != null ? _active.transform.position + _active._centreOffset : Vector3.zero;

        /// <summary>The flight scene ends: no camera or drood of it is kept.</summary>
        internal static void ForgetFlightScene()
        {
            ChaseCamera = null;
            _active = null;
            _rolledCamera = null;
        }

        private void OnDestroy()
        {
            if (_active == this)
            {
                _active = null;
                ChaseCamera = null;
            }
        }

        /// <summary>Called from the modifier's late chain, after the canopy is drawn.</summary>
        internal void Step()
        {
            // The chase belongs to the drood in control: other droods in the scene leave it be.
            var player = GetComponent<Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript>();
            if (player == null || !player.IsPlayerCraft)
            {
                return;
            }

            ChaseCamera = null;

            var chase = DroodChaseCameraMode.SelectedMode;
            if (chase != _lookMode)
            {
                // Another view: the look-around and the eased centre start afresh.
                _lookMode = chase;
                _lookYaw = 0f;
                _lookPitch = 0f;
                _hasCentreOffset = false;
            }

            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            _freeFall = _freeFall != null ? _freeFall : GetComponent<DroodFreeFall>();
            var eva = GetComponent<Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript>();
            var canopyOut = _parachute != null && _parachute.CanopyOut;
            var skydiving = !canopyOut && chase == DroodChaseCameraMode.Drood && _freeFall != null && _freeFall.Weight > 0f;
            if (_rolledCamera != null && (chase == null || !(canopyOut || skydiving)
                || CameraManagerScript.Instance?.CurrentCameraController != _rolledCamera))
            {
                if (DroodReflection.IsOrbitCamera(_rolledCamera))
                {
                    DroodReflection.SetOrbitRotation(_rolledCamera, _rotationBefore);
                }

                _rolledCamera = null;
            }

            if (chase == null || !(canopyOut || skydiving)
                || eva == null || !eva.IsPlayerCraft || !DroodReflection.OrbitCameraAvailable)
            {
                _heading = Vector3.zero;
                _lookMode = null;
                return;
            }

            var camera = CameraManagerScript.Instance?.CurrentCameraController;
            if (!DroodReflection.IsOrbitCamera(camera))
            {
                _heading = Vector3.zero;
                return;
            }

            UpdateLook();

            var up = transform.up;
            var velocity = Vector3.zero;
            var craft = eva.PartScript?.CraftScript;
            if (craft != null)
            {
                up = -(Vector3)craft.FlightData.GravityFrameNormalized;
                velocity = (Vector3)craft.SurfaceVelocity;
            }

            // Look along the canopy's heading, kept within ChaseMaxOffPath of the horizontal flight
            // path when moving (the camera stays on the -velocity side, never swings round).
            // Skydiving: along the drood's head (its up, belly down in the arch).
            var heading = Vector3.ProjectOnPlane(skydiving ? transform.up : _parachute.CanopyHeading, up);
            var path = Vector3.ProjectOnPlane(velocity, up);
            var look = heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.ProjectOnPlane(transform.forward, up);
            if (!skydiving && path.magnitude > ChasePathAbove && look.sqrMagnitude > 1e-4f)
            {
                look = Vector3.RotateTowards(path.normalized, look.normalized, ChaseMaxOffPath * Mathf.Deg2Rad, 0f);
            }

            if (look.sqrMagnitude < 1e-4f)
            {
                return;
            }

            look.Normalize();
            if (_heading.sqrMagnitude < 0.5f)
            {
                _heading = look;
                _headingVelocity = Vector3.zero;
            }
            else
            {
                _heading = Vector3.ProjectOnPlane(Vector3.SmoothDamp(_heading, look, ref _headingVelocity, ChaseHeadingSeconds), up);
                _heading = _heading.sqrMagnitude > 1e-6f ? _heading.normalized : look;
            }

            // The camera's Euler angles are measured from its reference plane's neutral rotation
            // (for the third-person view that follows the drood), so they are worked out anew each
            // frame and set as its rotation now and as its target: it doesn't ease after the drood.
            var right = Vector3.Cross(up, _heading);
            var pitchDown = Mathf.Min((skydiving ? SkydivePitchDown : chase.UpLines > 0f ? ChasePitchDown : DroodChasePitchDown) + _lookPitch, MaxPitchDown);
            var view = Quaternion.LookRotation(Quaternion.AngleAxis(_lookYaw, up) * (Quaternion.AngleAxis(pitchDown, right) * _heading), up);

            // The ram-air's Canopy Chase: right behind the wing (ChasePitchDown above its chord),
            // turning with it in pitch, yaw and roll (smoothed over ChaseHeadingSeconds); looking
            // around turns it about the wing's own axes.
            var wingForward = chase.UpLines > 0f ? _parachute.WingForward : Vector3.zero;
            if (wingForward.sqrMagnitude > 0.5f)
            {
                var wing = Quaternion.LookRotation(wingForward, _parachute.WingUp) * Quaternion.Euler(ChasePitchDown + _lookPitch, _lookYaw, 0f);
                _wingView = _hasWingView ? Quaternion.Slerp(_wingView, wing, 1f - Mathf.Exp(-Time.deltaTime / ChaseHeadingSeconds)) : wing;
                _hasWingView = true;
                view = _wingView;
            }
            else
            {
                _hasWingView = false;
            }

            // Lowered along the view's own down (it rolls with the wing): straight down on screen.
            var offset = chase.UpLines > 0f ? _parachute.UpTheLines(chase.UpLines) - transform.position - view * Vector3.up * CanopyChaseLower : Vector3.zero;
            if (!_hasCentreOffset)
            {
                _centreOffset = offset;
                _centreVelocity = Vector3.zero;
            }
            else
            {
                _centreOffset = Vector3.SmoothDamp(_centreOffset, offset, ref _centreVelocity, ChaseCentreSeconds);
            }

            _hasCentreOffset = true;
            view = AboveGround(camera, view, transform.position + _centreOffset, up);
            TurnOrbit(camera, view);
            _active = this;
            ChaseCamera = camera;

            // The game places its camera at the start of each frame (FlightGameLoop.Update →
            // GameViewScript.EndFrame), before this frame's poses: with only the camera patch, it
            // showed the view worked out a frame earlier, a frame behind the wing, by as much as
            // that frame's length (the frame chase recorder: the camera's turn per frame went with
            // the previous frame's time, the wing's with this one's), so the view juddered with
            // uneven frame times. Placed here too, at the end of the frame's poses, the frame
            // renders with this frame's view (the camera hangs from the pivot).
            var pivot = DroodReflection.OrbitPivot(camera);
            if (pivot != null)
            {
                pivot.rotation = view;
                pivot.position = ChaseCentre;
            }
        }

        /// <summary>
        /// The view swung up about the orbited point <paramref name="centre"/> (still aimed at it)
        /// as far as keeps the camera, hanging the player's zoom distance behind it,
        /// GroundClearance above the ground straight below it; the lift eased (up quickly,
        /// ChaseGroundRiseSeconds, back slowly, ChaseGroundFallSeconds).
        /// </summary>
        private Quaternion AboveGround(object camera, Quaternion view, Vector3 centre, Vector3 up)
        {
            var pivot = DroodReflection.OrbitPivot(camera);
            var eye = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera?.NearCamera;
            var zoom = pivot != null && eye != null ? Vector3.Distance(eye.transform.position, pivot.position) : 0f;
            var needed = 0f;
            if (zoom > 0.5f)
            {
                var cameraAt = centre - view * Vector3.forward * zoom;
                if (GroundBelow(cameraAt, up, out var ground))
                {
                    needed = Mathf.Max(GroundClearance - Vector3.Dot(cameraAt - ground, up), 0f);
                }
            }

            var seconds = needed > _groundLift ? ChaseGroundRiseSeconds : ChaseGroundFallSeconds;
            _groundLift = Mathf.SmoothDamp(_groundLift, needed, ref _groundLiftRate, seconds);
            if (_groundLift <= 1e-3f || zoom <= 0.5f)
            {
                return view;
            }

            // The camera raised, then back onto its distance from the point, aimed at it.
            var raised = centre - view * Vector3.forward * zoom + up * _groundLift;
            var look = centre - raised;
            return look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look.normalized, view * Vector3.up) : view;
        }

        /// <summary>The ground straight below <paramref name="point"/> (its own drood skipped).</summary>
        private bool GroundBelow(Vector3 point, Vector3 up, out Vector3 ground)
        {
            ground = point;
            var body = GetComponentInParent<Rigidbody>();
            var count = Physics.RaycastNonAlloc(point + up * GroundRayAbove, -up, _groundHits, GroundRayAbove + GroundRayBelow, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.distance < best && hit.rigidbody != body)
                {
                    best = hit.distance;
                    ground = hit.point;
                }
            }

            return best < float.MaxValue;
        }

        // The chase camera kept this far (m) above the ground straight below it; the ray down
        // from GroundRayAbove above it, GroundRayBelow below; the lift eased over these (s).
        private const float GroundClearance = 1.5f;
        private const float GroundRayAbove = 50f;
        private const float GroundRayBelow = 50f;
        private const float ChaseGroundRiseSeconds = 0.08f;
        private const float ChaseGroundFallSeconds = 0.4f;
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];
        private float _groundLift;
        private float _groundLiftRate;

        /// <summary>
        /// The orbit camera's own rotation set to <paramref name="view"/> (its angles measured
        /// from its reference plane's neutral rotation), and the view kept for the camera patch.
        /// </summary>
        private static void TurnOrbit(object camera, Quaternion view)
        {
            var wanted = Quaternion.Inverse(DroodReflection.OrbitNeutralRotation(camera)) * view;
            var euler = wanted.eulerAngles;
            var current = DroodReflection.OrbitTargetRotation(camera);
            if (_rolledCamera != camera)
            {
                _rotationBefore = current;
            }

            // Nearest to the current angles, so the camera never spins the long way round.
            euler.x = current.x + Mathf.DeltaAngle(current.x, euler.x);
            euler.y = current.y + Mathf.DeltaAngle(current.y, euler.y);
            euler.z = current.z + Mathf.DeltaAngle(current.z, euler.z);
            DroodReflection.SetOrbitRotation(camera, euler);
            _rolledCamera = camera;
            ChaseView = view;
        }


        /// <summary>
        /// Dragging with any mouse button turns the view around the same centre (a click without
        /// moving changes nothing); it stays so until the view changes.
        /// </summary>
        private void UpdateLook()
        {
            var dragging = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            var mouse = Input.mousePosition;
            if (dragging && _wasDragging)
            {
                var delta = mouse - _lastMouse;
                if (delta.sqrMagnitude > 0f)
                {
                    _lookYaw = Mathf.Repeat(_lookYaw + delta.x * ChaseLookDegreesPerPixel + 180f, 360f) - 180f;
                    _lookPitch = Mathf.Clamp(_lookPitch - delta.y * ChaseLookDegreesPerPixel, -ChaseLookPitchLimit - ChasePitchDown, ChaseLookPitchLimit - ChasePitchDown);
                }
            }

            _wasDragging = dragging;
            _lastMouse = mouse;
        }
    }
}
