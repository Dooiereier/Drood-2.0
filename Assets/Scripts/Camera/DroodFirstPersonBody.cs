using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using Assets.Scripts.Flight.GameView.Cameras;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// In first person, Drood 2.0 sees its own body while skydiving: in the free-fall arch and
    /// under the chute until touchdown (arms in the arch, hands on the steering lines), and
    /// during a landing clip (<see cref="DroodLandingFall"/>), where the eye point also follows
    /// the animated head (see HeadFollowEaseSeconds). Elsewhere the game's own first-person view
    /// stays.
    ///
    /// When the camera switches to first person, the game sets the drood's own renderers to
    /// "shadows only" (EvaScript.UpdateRenderers, once, on the switch), so the body is
    /// invisible to the camera. Each frame in first person this turns the drood's body mesh
    /// back on; the eye point sits just in front of the visor (EyeInFront, EyeFromHead), so the
    /// head is behind the camera and every other camera sees it whole. Out of first person (or
    /// when destroyed) the renderers are restored.
    /// Only the drood's own skinned mesh: held parts, the canopy and its lines are separate.
    ///
    /// "Shadows only" is for the game's own view: any other camera (another mod's picture in
    /// picture, HorseEye, PlusOne) sees the drood, its renderers switched to visible just before
    /// that camera culls and back just after it renders (Camera.onPreCull / onPostRender). The
    /// head stays as it is (a bone's shape can't differ per camera: it is skinned once a frame
    /// for every camera), so the eye is in front of it instead.
    ///
    /// The hands come close to the eye (in the arch at head height), closer than the camera's
    /// near clip plane, and would be cut off. Like the game does for a drood seated in a crew
    /// compartment (EvaScript.OnCrewCompartmentStateChanged sets 0.06 m), the first-person
    /// camera's near clip gets an override (CameraController.NearClipOverride, public) while
    /// the body is shown. The near and far cameras split the depth range, so the distance
    /// keeps its precision.
    ///
    /// The view turns about a fixed eye point: the first-person camera normally sits at the
    /// vantage's point pushed along the view by an "eyeball offset"
    /// (FirstPersonCameraController.EyeballOffset, public), so looking around swings it around
    /// the head. While the body is shown the offset is off (null) and the vantage's point sits
    /// EyeInFront in front of the eyes (fixed to the body), so looking down shows the body.
    /// The offset is also off while the view is pitched up (DroodLookUp): looking up along the
    /// head's up vector, the offset's direction (the view flattened against
    /// FirstPersonCameraController.HeadUpVec) is tiny and swings with every head movement, so
    /// the camera would shake. With the offset null the game ignores HeadUpVec (its docs).
    ///
    /// The first-person camera controller is shared by all droods (EvaSharedCamerasScript),
    /// so its settings have one owner (static): the drood you fly while it wants them.
    /// The game's own values are saved when the mod first takes the controller and restored
    /// when no drood wants them any more, also when ownership passes from one drood to another
    /// in between. While owned, they are set again every frame. The same owner narrows the
    /// look limit left and right while skydiving (InteractiveCameraController.
    /// ClampDeltaRotationRange, which the game sets to 70° up/down, 180° left/right), and
    /// switches mouse look off while mouse steering is on (InteractiveCameraController.MouseLook).
    ///
    /// Runs at the end of the late chain (after the IK solve), so the eye is placed on the
    /// final pose of the frame.
    /// </summary>
    public class DroodFirstPersonBody : MonoBehaviour
    {
        // The first-person camera's near clip (m) while the body is shown.
        private const float NearClip = 0.05f;

        // While skydiving, the view turns at most this far left and right (degrees; the game
        // allows 180). Up and down keep the game's limit.
        private const float LookSideways = 95f;

        // The shared controller's settings (see the class comment): its owner, and the game's
        // values to restore.
        private static FirstPersonCameraController _ownedCamera;
        private static DroodFirstPersonBody _owner;
        private static float? _gameEyeballOffset;
        private static System.Func<float> _gameNearClip;
        private static Vector2? _gameLookRange;
        private static bool _gameMouseLook;

        // After mouse look is switched: the look angle held for this many frames (see Own).
        private const int LookSettleFrames = 3;
        private static int _settleFrames;
        private static Vector2 _settleLook;
        private static readonly System.Func<float> NearClipValue = () => NearClip;

        // The eye point this far (m) in front of the vantage's own point (the head's centre),
        // along the drood's facing: just in front of the visor.
        private const float EyeInFront = 0.24f;

        // The vantage's camera point and its own place.
        private Transform _cameraPoint;
        private Vector3 _cameraPointRest;
        private Quaternion _cameraPointRestRotation;

        // During a landing clip the eye point follows the animated head (position fully, its
        // rotation smoothed over HeadFollowSmoothSeconds), blending in and out over
        // HeadFollowEaseSeconds; the mouse look stays on top (the camera adds it to the point's
        // rotation). Its place and rotation in the head's frame are taken from the last frame
        // before the clip, so the view starts where it was.
        private const float HeadFollowEaseSeconds = 0.2f;
        private const float HeadFollowSmoothSeconds = 0.1f;
        private DroodLandingFall _landingFall;

        // Under the canopy a pull or a stall leans the body and the head goes forward and down
        // with it; the vantage's point stays where an upright head would be (22 cm behind the
        // leaning head, 20 cm above it: looking down at the neck). There the eye point sits on
        // the head itself, EyeFromHead (m: right, up, forward in the drood's frame; forward as
        // far as the body's eye point, EyeInFront, so looking down clears the neck) from it,
        // every frame; its turn stays the mouse's.
        private static readonly Vector3 EyeFromHead = new Vector3(0f, 0.09f, 0.24f);

        /// <summary>
        /// In the free-fall arch the eye point turns with the pose's head lift
        /// (<see cref="DroodFreeFallPose.HeadLiftApplied"/>): about the neck, by the same angle, so
        /// the view looks up with the head.
        /// </summary>
        private void FollowHeadLift()
        {
            _freeFallPose = _freeFallPose != null ? _freeFallPose : GetComponent<DroodFreeFallPose>();
            var lift = _freeFallPose != null ? _freeFallPose.HeadLiftApplied : 0f;
            var neck = _freeFallPose != null ? _freeFallPose.Neck : null;
            if (lift <= 0f || neck == null)
            {
                return;
            }

            var turn = Quaternion.AngleAxis(-lift, transform.right);
            _cameraPoint.position = neck.position + turn * (_cameraPoint.position - neck.position);
            _cameraPoint.rotation = turn * _cameraPoint.rotation;
        }

        private DroodFreeFallPose _freeFallPose;

        // The share of the body's lean under the canopy the first-person view keeps.
        private const float CanopyLeanKept = 1f / 3f;
        private const float LevelFromHeight = 20f;
        private const float LevelFullHeight = 10f;
        private const float LevelFromSpeed = 20f;
        private const float LevelFullSpeed = 17f;
        // The steadied view's response time (a critically damped follow, s).
        private const float LevelSmoothSeconds = 0.35f;
        // The steadied view's takeover follows its height and speed share this slowly (s).
        private const float LevelTakeoverSeconds = 1f;

        // The steadied view's pitch above the horizon (degrees) and its rate; NaN: not following.
        // Its share of the view, taken over in time, and that share's rate.
        private float _levelPitch = float.NaN;
        private float _levelPitchRate;
        private float _levelShare;
        private float _levelShareRate;

        /// <summary>
        /// Low under the canopy (the flare) the view is held steady like a head: its pitch above the
        /// horizon follows CanopyLeanKept of the body's (the rest of the lean taken out) through a
        /// critically damped follow of LevelSmoothSeconds, so the body's quick swings are mostly
        /// smoothed away and slow leans come through softly. The steadied pitch takes over on
        /// S-curves from LevelFromHeight down to LevelFullHeight of the feet above the ground and
        /// from LevelFromSpeed down to LevelFullSpeed (m/s), followed over LevelTakeoverSeconds;
        /// higher up or faster the view is the
        /// body's. Its bank and heading stay.
        /// </summary>
        private void LevelUnderCanopy()
        {
            var craft = _eva != null ? _eva.PartScript?.CraftScript : null;
            if (_parachute == null || !_parachute.IsHanging || craft == null)
            {
                _levelPitch = float.NaN;
                return;
            }

            var up = -(Vector3)craft.FlightData.GravityFrameNormalized;
            var forward = _cameraPoint.forward;
            var level = Vector3.ProjectOnPlane(forward, up);
            if (level.sqrMagnitude < 1e-4f)
            {
                _levelPitch = float.NaN;
                return;
            }

            level.Normalize();
            var bodyPitch = 90f - Vector3.Angle(forward, up);
            var target = bodyPitch * CanopyLeanKept;
            if (float.IsNaN(_levelPitch))
            {
                _levelPitch = bodyPitch;
                _levelPitchRate = 0f;
                _levelShare = 0f;
                _levelShareRate = 0f;
            }

            // Followed all the time under the canopy, so taking over starts from a settled view.
            _levelPitch = Mathf.SmoothDamp(_levelPitch, target, ref _levelPitchRate, LevelSmoothSeconds, Mathf.Infinity, Time.deltaTime);

            // Low and slow: not in a dive's pull-out near the ground, where it came too suddenly.
            // Taken over in time too (the flare's slowing would sweep the speed's share in a moment).
            var speed = (float)craft.SurfaceVelocity.magnitude;
            var share = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LevelFromHeight, LevelFullHeight, _parachute.FeetAboveGround))
                * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LevelFromSpeed, LevelFullSpeed, speed));
            _levelShare = Mathf.Clamp01(Mathf.SmoothDamp(_levelShare, share, ref _levelShareRate, LevelTakeoverSeconds, Mathf.Infinity, Time.deltaTime));
            var low = _levelShare;
            if (low <= 1e-4f)
            {
                return;
            }

            var pitch = Mathf.Lerp(bodyPitch, _levelPitch, low) * Mathf.Deg2Rad;
            var look = level * Mathf.Cos(pitch) + up * Mathf.Sin(pitch);
            _cameraPoint.rotation = Quaternion.FromToRotation(forward, look) * _cameraPoint.rotation;
        }

        private void PlaceEyeOnHead()
        {
            // Also seated in the drop zone's plane (leaning forward, the body's eye point would be
            // behind the head, the neck in view).
            if (_head == null || ((_parachute == null || !_parachute.IsHanging) && (_planeRide == null || !_planeRide.InPlane)))
            {
                return;
            }

            _cameraPoint.position = _head.position + transform.TransformDirection(EyeFromHead);
        }
        private float _headFollow;
        private bool _wasFirstPerson;
        private Vector3 _eyeInHead;
        private Quaternion _eyeInHeadRotation = Quaternion.identity;
        private Quaternion _eyeRotation;
        private bool _hasEyeInHead;

        private EvaScript _eva;
        private DroodFreeFall _freeFall;
        private DroodPlaneRide _planeRide;
        private DroodParachute _parachute;
        private DroodLookUp _lookUp;
        private SkinnedMeshRenderer[] _body;

        // All the drood's renderers (body, backpack, held parts), looked up again when they
        // change (RenderersEvery seconds); those switched from "shadows only" to visible for
        // another camera now.
        private const float RenderersEvery = 2f;
        private Renderer[] _renderers;
        private float _nextRenderers;
        private bool[] _shownForCamera;
        private bool _anyShownForCamera;

        /// <summary>
        /// Any camera about to cull: for a camera other than the game's own (whose view the
        /// first person hides the drood from), the drood's "shadows only" renderers visible.
        /// </summary>
        private void OnAnyPreCull(Camera camera)
        {
            if (_eva == null || !_eva.IsPlayerCraft || IsGameCamera(camera))
            {
                return;
            }

            if (_renderers == null || Time.unscaledTime >= _nextRenderers)
            {
                _nextRenderers = Time.unscaledTime + RenderersEvery;
                _renderers = GetComponentsInChildren<Renderer>(true);
            }

            if (_shownForCamera == null || _shownForCamera.Length != _renderers.Length)
            {
                _shownForCamera = new bool[_renderers.Length];
            }

            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                var shadowsOnly = renderer != null && renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                _shownForCamera[i] = shadowsOnly;
                if (shadowsOnly)
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    _anyShownForCamera = true;
                }
            }
        }

        /// <summary>That camera has rendered: the renderers back to "shadows only".</summary>
        private void OnAnyPostRender(Camera camera)
        {
            if (!_anyShownForCamera)
            {
                return;
            }

            _anyShownForCamera = false;
            for (var i = 0; i < _renderers.Length; i++)
            {
                if (_shownForCamera[i] && _renderers[i] != null)
                {
                    _renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
            }
        }

        /// <summary>One of the game view's own cameras (near and far share the game camera's transform).</summary>
        private static bool IsGameCamera(Camera camera)
        {
            var gameCamera = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera;
            if (gameCamera == null || camera == null)
            {
                return true;
            }

            var near = gameCamera.NearCamera;
            return camera == near || (near != null && camera.transform.parent == near.transform.parent) || camera.transform.IsChildOf(gameCamera.Transform);
        }
        private Transform _head;
        private bool _showing;

        private void Awake()
        {
            Camera.onPreCull += OnAnyPreCull;
            Camera.onPostRender += OnAnyPostRender;
            _eva = GetComponent<EvaScript>();
            _freeFall = GetComponent<DroodFreeFall>();
            _parachute = GetComponent<DroodParachute>();
            _body = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head")
                {
                    _head = t;
                    break;
                }
            }

            DroodLog.Debug($"First-person body: {_body.Length} body mesh(es), head {(_head != null ? "found" : "missing")}.");
        }

        // A soft touchdown under the canopy (no landing clip; the game's first-person view stays
        // still otherwise): the eye dips and nods a little, a tiny version of the hard landing's
        // crouch. Down DipDepthSoftest..DipDepthHardest (m) and DipPitchSoftest..DipPitchHardest (°)
        // with the touchdown's severity (DroodLandingFall.Severity, below 1), in DipDownSeconds,
        // back up over DipUpSeconds. The game puts the eye point back each frame (the vantage's
        // offset, the look-up's rotation), so the dip is added on top each frame.
        private const float DipDepthSoftest = 0.03f;
        private const float DipDepthHardest = 0.12f;
        private const float DipPitchSoftest = 1.5f;
        private const float DipPitchHardest = 5f;
        private const float DipDownSeconds = 0.1f;
        private const float DipUpSeconds = 0.4f;
        private float _dipStart = float.NegativeInfinity;
        private float _dipDepth;
        private float _dipPitch;

        /// <summary>A soft touchdown at <paramref name="sinkSpeed"/> down and <paramref name="forwardSpeed"/> forward (m/s).</summary>
        internal void SoftTouchdown(float sinkSpeed, float forwardSpeed)
        {
            var severity = Mathf.Clamp01(DroodLandingFall.Severity(sinkSpeed, forwardSpeed));
            _dipDepth = Mathf.Lerp(DipDepthSoftest, DipDepthHardest, severity);
            _dipPitch = Mathf.Lerp(DipPitchSoftest, DipPitchHardest, severity);
            _dipStart = Time.time;
        }

        private void Dip()
        {
            var t = Time.time - _dipStart;
            if (_cameraPoint == null || t >= DipDownSeconds + DipUpSeconds)
            {
                return;
            }

            var weight = t < DipDownSeconds
                ? Mathf.SmoothStep(0f, 1f, t / DipDownSeconds)
                : 1f - Mathf.SmoothStep(0f, 1f, (t - DipDownSeconds) / DipUpSeconds);
            _cameraPoint.position -= transform.up * (_dipDepth * weight);
            // Positive pitch about the eye's own right is down.
            _cameraPoint.rotation *= Quaternion.Euler(_dipPitch * weight, 0f, 0f);
        }

        /// <summary>Late chain, after the IK solve and the pose blend.</summary>
        internal void Step()
        {
            // Found at start (created before this component); looked up again only if missing.
            _freeFall = _freeFall != null ? _freeFall : GetComponent<DroodFreeFall>();
            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            _lookUp = _lookUp != null ? _lookUp : GetComponent<DroodLookUp>();
            _landingFall = _landingFall != null ? _landingFall : GetComponent<DroodLandingFall>();
            _planeRide = _planeRide != null ? _planeRide : GetComponent<DroodPlaneRide>();
            var skydiving = (_freeFall != null && _freeFall.Weight > 0f) || (_parachute != null && _parachute.UnderChuteInAir)
                || (_planeRide != null && _planeRide.InPlane);
            var flownInFirstPerson = _eva != null && _eva.IsPlayerCraft && _eva.EvaActive && _eva.IsFpsActive;
            var landing = _landingFall != null && _landingFall.Active;
            _headFollow = Mathf.MoveTowards(_headFollow, landing && flownInFirstPerson ? 1f : 0f, Time.deltaTime / HeadFollowEaseSeconds);
            var firstPerson = (skydiving || landing || _headFollow > 0f) && flownInFirstPerson;
            var lookingUp = flownInFirstPerson && _lookUp != null && _lookUp.CurrentAngle >= 0.1f;
            if (firstPerson != _wasFirstPerson && flownInFirstPerson)
            {
                // Debug: what decides the body view (and with it mouse look under mouse steering).
                _wasFirstPerson = firstPerson;
                DroodLog.Debug($"First person: body view {(firstPerson ? "on" : "off")} (free fall {(_freeFall != null ? _freeFall.Weight : -1f):0.00}, under the canopy in the air {(_parachute != null && _parachute.UnderChuteInAir)}, hanging {(_parachute != null && _parachute.IsHanging)}, landing clip {landing}, looking up {lookingUp}, grounded {_eva.IsGrounded}, in water {_eva.IsInWater}).");
            }
            FindCameraPoint();
            if (firstPerson)
            {
                foreach (var renderer in _body)
                {
                    if (renderer != null && renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.On)
                    {
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    }
                }

                // The eye point in front of the eyes, fixed to the body; under the canopy on the
                // head itself (see EyeFromHead).
                if (_cameraPoint != null && _cameraPoint.parent != null)
                {
                    _cameraPoint.localPosition = _cameraPointRest + _cameraPoint.parent.InverseTransformDirection(transform.forward * EyeInFront);
                    _cameraPoint.localRotation = _cameraPointRestRotation;
                    PlaceEyeOnHead();
                    FollowHeadLift();
                    LevelUnderCanopy();
                }

                FollowHead(landing);
                if (!_showing)
                {
                    _showing = true;
                    DroodLog.Debug("First person: body shown, the eye in front of the visor.");
                }
            }
            else
            {
                if (!landing)
                {
                    FollowHead(false);
                }
            }

            if (!firstPerson)
            {
                _levelPitch = float.NaN;
            }

            if (!firstPerson && _showing)
            {
                Restore();
            }

            if (flownInFirstPerson)
            {
                Dip();
            }

            if (firstPerson || lookingUp)
            {
                Own(DroodReflection.FpsCameraController(_eva), firstPerson);
            }
            else if (_owner == this)
            {
                Release();
            }
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnAnyPreCull;
            Camera.onPostRender -= OnAnyPostRender;
            if (_showing)
            {
                Restore();
            }

            if (_owner == this)
            {
                Release();
            }
        }

        /// <summary>
        /// Takes the shared camera controller's settings (see the class comment) and sets them:
        /// no eyeball offset, and while the body is shown the near clip override and the narrower
        /// look limit.
        /// </summary>
        private void Own(FirstPersonCameraController camera, bool bodyShown)
        {
            if (camera == null)
            {
                return;
            }

            if (camera != _ownedCamera)
            {
                // A different controller (a new flight scene): the old one's values back first.
                Release();
                _ownedCamera = camera;
                _gameEyeballOffset = camera.EyeballOffset;
                _gameNearClip = camera.NearClipOverride;
                _gameLookRange = camera.ClampDeltaRotationRange;
                _gameMouseLook = camera.MouseLook;
            }

            _owner = this;
            camera.EyeballOffset = null;
            camera.NearClipOverride = bodyShown ? NearClipValue : _gameNearClip;

            // The look limit (x up/down, y left/right, as the game's EvaSharedCamerasScript sets
            // it): left/right narrowed while skydiving.
            var range = _gameLookRange ?? new Vector2(70f, 180f);
            camera.ClampDeltaRotationRange = bodyShown ? new Vector2(range.x, Mathf.Min(range.y, LookSideways)) : _gameLookRange;
            if (bodyShown)
            {
                // Applied when the angle is next set; clamp the current one now.
                camera.DeltaRotation = camera.DeltaRotation;
            }

            // Mouse steering (DroodMouseSteering) needs the cursor: no mouse look meanwhile
            // (the view then only turns while a mouse button drags it, the game's other mode).
            // Switching mouse look locks or frees the cursor, whose jump the camera would read as a
            // look movement: the look angle is held where it was for LookSettleFrames frames.
            if (_settleFrames > 0)
            {
                _settleFrames--;
                camera.DeltaRotation = _settleLook;
            }

            var mouseLook = bodyShown && DroodMouseSteering.IsOn(_eva) ? false : _gameMouseLook;
            if (camera.MouseLook != mouseLook)
            {
                DroodLog.Debug($"First person: mouse look {(mouseLook ? "on" : "off")} (body view {bodyShown}, mouse steering {DroodMouseSteering.IsOn(_eva)}).");
                // Mouse steering on: the view centred forward (held there while the cursor settles).
                _settleLook = mouseLook ? camera.DeltaRotation : default;
                _settleFrames = LookSettleFrames;
                camera.MouseLook = mouseLook;
            }
        }

        /// <summary>The game's values back on the shared controller; nobody owns it any more.</summary>
        private static void Release()
        {
            if (_ownedCamera != null)
            {
                _ownedCamera.EyeballOffset = _gameEyeballOffset;
                _ownedCamera.NearClipOverride = _gameNearClip;
                _ownedCamera.ClampDeltaRotationRange = _gameLookRange;
                if (_ownedCamera.MouseLook != _gameMouseLook)
                {
                    _ownedCamera.MouseLook = _gameMouseLook;
                }
            }

            _ownedCamera = null;
            _owner = null;
            _gameEyeballOffset = null;
            _gameNearClip = null;
            _gameLookRange = null;
        }

        /// <summary>
        /// The camera vantage's point (it is created in the vantage's FlightStart, so looked up
        /// lazily).
        /// </summary>
        private void FindCameraPoint()
        {
            if (_cameraPoint != null || _eva == null)
            {
                return;
            }

            var vantage = _eva.PartScript?.GetModifier<CameraVantageScript>();
            _cameraPoint = vantage != null ? vantage.CameraPosition : null;
            if (_cameraPoint != null)
            {
                _cameraPointRest = _cameraPoint.localPosition;
                _cameraPointRestRotation = _cameraPoint.localRotation;
            }
        }

        /// <summary>
        /// The eye point on the animated head during a landing clip (see HeadFollowEaseSeconds);
        /// before it, its place in the head's frame is kept up to date.
        /// </summary>
        private void FollowHead(bool landing)
        {
            if (_head == null || _cameraPoint == null || _cameraPoint.parent == null)
            {
                return;
            }

            var headRotation = _head.rotation;
            if (!landing && _headFollow <= 0f)
            {
                // The view as it is without a clip (the look-up's pitch left out): its place and
                // rotation in the head's frame.
                var rotation = _cameraPoint.parent.rotation * _cameraPointRestRotation;
                _eyeInHead = Quaternion.Inverse(headRotation) * (_cameraPoint.position - _head.position);
                _eyeInHeadRotation = Quaternion.Inverse(headRotation) * rotation;
                _eyeRotation = rotation;
                _hasEyeInHead = true;
                return;
            }

            if (!_hasEyeInHead)
            {
                return;
            }

            var target = headRotation * _eyeInHeadRotation;
            _eyeRotation = Quaternion.Slerp(_eyeRotation, target, 1f - Mathf.Exp(-Time.deltaTime / HeadFollowSmoothSeconds));
            var weight = Mathf.SmoothStep(0f, 1f, _headFollow);
            _cameraPoint.position = Vector3.Lerp(_cameraPoint.position, _head.position + headRotation * _eyeInHead, weight);
            _cameraPoint.rotation = Quaternion.Slerp(_cameraPoint.rotation, _eyeRotation, weight);
        }

        /// <summary>
        /// The head back to its size, the eye point to its place; the renderers as the game
        /// wants them out of first person.
        /// </summary>
        private void Restore()
        {
            _showing = false;
            if (_cameraPoint != null)
            {
                _cameraPoint.localPosition = _cameraPointRest;
            }

            var firstPerson = _eva != null && _eva.IsFpsActive;
            foreach (var renderer in _body)
            {
                if (renderer != null)
                {
                    renderer.shadowCastingMode = firstPerson
                        ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                        : UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
        }
    }
}
