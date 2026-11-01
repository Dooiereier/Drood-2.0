using System.Collections.Generic;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft.Parts;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Built-in parachute for Drood 2.0: a round canopy or a ram-air wing, chosen per drood in
    /// the designer (<see cref="Drood2Data"/>).
    ///
    /// AG1 on arms it. It deploys below MaxDeploymentAltitude and MaxDeploymentSpeed (the ram-air
    /// by hand: pilot chute, bag, line stretch; DroodParachute.RamAir.cs) and opens in a fixed
    /// time. AG1 off repacks it instantly, and it can be reused. On landing it collapses next to
    /// the drood but stays attached. Starved of air (held still in the air, or a ram-air far too
    /// slow to fly) it deflates and reopens when the air comes back (UpdateStarved).
    ///
    /// Unlike the stock parachute there is no base cord: the lines run straight from the
    /// canopy to the drood's shoulders. The canopy is a massless drag surface. Its force is
    /// applied at the harness (above the centre of mass), so the drood hangs and swings under
    /// it. The line direction trails the canopy's force with some lag (the pendulum). The
    /// ram-air's force comes from its attitude (DroodParachute.RamAirAttitude.cs). There's no
    /// separate rigidbody; the few world positions kept between frames (the landed canopy)
    /// move with world recentring and teleports (OnFrameShift).
    /// </summary>
    public partial class DroodParachute : MonoBehaviour
    {
        // Sized like a personal round parachute: lines about as long as the canopy is
        // wide, canopy height ~0.65 x radius. At sea level a 115 kg drood lands at ~5 m/s.
        // Deploys anywhere below MaxDeploymentAltitude (m above sea level), then the canopy
        // opens fully once the lines are out, whatever the air density. Max speed is the
        // stock Parachute's value.
        public const int ActivationGroup = 1;
        public const float InflatedRadius = 4f;
        public const float DeflatedRadius = 0.4f;
        public const float CanopyHeight = 2.6f;

        // The round canopy as drawn while it opens: a bag BagLength of its height long while the
        // lines pay out, stretching to its length with them, StartRadius wide, then filling out
        // (eased, slow at first) as it inflates.
        private const float StartRadius = 0.06f;
        private const float BagLength = 0.15f;
        public const float LineLength = 8.5f; // shroud lines: canopy rim -> shoulders
        public const float MaxDeploymentAltitude = 10000f;
        public const float MaxDeploymentSpeed = 320f;

        // The round canopy deploys by itself (AG1 switched on) once the drood has been in the
        // free-fall arch (which needs the air's dynamic pressure) this long (s).
        private const float RoundAutoDeploySeconds = 1f;

        private const float DragCoefficient = 1.5f;

        // Forward drive, like a round canopy with drive slots (T-10 "TU", MC-1). The canopy
        // makes lift = drag x GlideRatio, perpendicular to the airflow, toward where it's
        // facing. In a steady descent, forward speed / sink speed = GlideRatio. The lift also
        // carries some weight, so the sink drops a little as the ratio rises: 0.52 gives
        // ~4.8 m/s sink and ~2.5 m/s forward at sea level. 0 = plain round chute (drifts only).
        private const float GlideRatio = 0.52f;
        private const float LineExtendSeconds = 1f;
        private const float InflateSeconds = 2f;
        private const float CollapseSeconds = 0.6f;

        // Brief hops (e.g. during the landing fall) shorter than this don't re-inflate a
        // canopy that has started collapsing.
        private const float LandedGraceSeconds = 0.5f;

        // Landed canopy: it tips over downwind, apex away from the drood and skirt toward
        // it, and settles at LandedDistance (fraction of the line length) from the drood as
        // a long flattened strip: LandedLength from skirt to apex, LandedRadius half-width,
        // LandedThickness thick. If the drood walks further than DragDistance (fraction of
        // the line length), the canopy is dragged along.
        private const float LandedDistance = 0.92f;
        private const float DragDistance = 0.95f;
        private const float LyingFoldExtra = 1.2f;
        private float _landedExtra;
        // Sized from the 4 m-radius canopy: skirt to apex over the canopy is about 4.8 m, and
        // the folded strip is about 3.3 m wide.
        private const float LandedRadius = 1.65f;
        private const float LandedLength = 4.8f;
        private const float LandedThickness = 0.1f;

        /// <summary>
        /// Seconds the canopy takes to come down after touchdown. Without the jumper's
        /// weight it floats down slowly, drifting downwind, and only falls over and
        /// flattens near the ground. Visual only: the pull stops with the physical collapse
        /// (CollapseSeconds).
        /// </summary>
        private const float VisualCollapseSeconds = 5f;

        /// <summary>
        /// The ram-air's fold goes RamAirFoldSecondPartSpeed times as fast from
        /// RamAirFoldSecondPartFrom of the collapse on (where it starts to fall over onto the
        /// ground).
        /// </summary>
        private const float RamAirFoldSecondPartFrom = 0.45f;
        private const float RamAirFoldSecondPartSpeed = 1.2f;

        /// <summary>
        /// Height above ground (m) at which a landed drood counts as airborne again, so the
        /// canopy reinflates. Lower hops keep the canopy on the ground.
        /// </summary>
        private const float ReinflateAltitude = 3f;

        // After a landing clip begins, a bounce doesn't reopen the canopy for this long (s): it
        // reinflated in the air with the drood still lying in the clip.
        private const float BounceHoldSeconds = 2f;
        private DroodLandingFall _landingFall;

        /// <summary>
        /// Structures (buildings, pads, crafts) below the drood count as ground within this
        /// height (m) above the terrain; see HeightAboveGround.
        /// </summary>
        private const float StructureRayRange = 200f;

        /// <summary>How far the canopy's radius shrinks while it floats down, before it lies flat.</summary>
        private const float FloatingDeflation = 0.3f;

        // Line points: a few along the ground, more for the curve up to the shoulder.
        private const int GroundLinePoints = 4;
        private const int HangingLinePoints = 16;
        private const int LinePoints = GroundLinePoints + HangingLinePoints;

        // Hanging attitude, one physical pendulum (see ApplyCanopyPull): the lines pull at a
        // fixed harness point HarnessHeight (m) above the centre of mass, along the line
        // direction, which trails the canopy's pull with a delay of line length / airspeed.
        // SwingDamping is a damping ratio (0 swings freely, 1 settles without overshoot);
        // OpeningSwingDamping at line stretch, so the drood swings upright from the arch with
        // ~7° of overshoot (offline simulation of the snatch from the logged openings). Rough
        // air comes from the canopy's sway (CanopySway), which the drood follows on its lines.
        private const float HarnessHeight = 0.3f;
        private const float SwingDamping = 0.3f;
        private const float OpeningSwingDamping = 0.6f;

        // Line steering (DroodParachute.Steering.cs). The EVA turn keys pull one steering line,
        // the back key both (brakes). ToggleSeconds: time to pull a line all the way down.
        private const float ToggleSeconds = 0.3f;

        // Mouse steering (DroodMouseSteering) under the canopy: the deadzone toward forward (the
        // front risers' dive) is this many times the player's, so a cursor a little above the
        // middle doesn't dive.
        private const float MouseForwardDeadzoneScale = 3f;

        // How far (m) a hand pulls its line down at most, turning or braking.
        private const float PullTravel = 0.1f;

        // A shoulder's lines reach its half of the rim; their middle is 2/π of the radius out.
        private const float FanCentre = 0.64f;

        // Round canopy: one line pulled turns the canopy at most RoundMaxTurnRate (rad/s,
        // ~10°/s); both lines cut the forward drive by RoundBrakeDriveLoss and add
        // RoundBrakeDragGain drag. Deliberately gentle; the ram-air canopy is the agile one.
        private const float RoundMaxTurnRate = 0.175f;
        private const float RoundBrakeDriveLoss = 0.5f;
        private const float RoundBrakeDragGain = 0.1f;

        // The round canopy's turn rate follows the steering lines with this response (1/s): its
        // own inertia, so turns build up over about a second.
        private const float TurnResponse = 1.2f;

        // The twisted lines turn the drood toward the canopy's heading: stiffness (rad/s² per
        // rad) and damping ratio (near 1, so it follows without swinging back and forth). The
        // damping acts on the twist rate relative to the canopy's own turn rate, so in a steady
        // turn the drood turns with the canopy instead of trailing it.
        private const float LineTwistStiffness = 9f;
        private const float LineTwistDampingRatio = 0.9f;

        // Starved of air (see UpdateStarved): below StarvedPressure (Pa; ~3 m/s at sea level)
        // for StarvedSeconds, the canopy deflates over StarvedDeflateSeconds and its lines swing
        // down within StarvedHangSeconds; the fabric bundle has StarvedDragArea (m²) of drag. It
        // opens again above ReopenPressure (Pa; ~5 m/s).
        private const float StarvedPressure = 5f;
        private const float StarvedSeconds = 1f;
        private const float ReopenPressure = 15f;
        private const float StarvedDeflateSeconds = 1.5f;

        // Ram-air deflation (see UpdateStarved): below RamAirDeflatePressure (Pa; ~5 m/s at sea
        // level) at once, reinflating above RamAirReinflatePressure (Pa; ~7 m/s). The deflated
        // bundle weighs StarvedBundleMass (kg) against its drag.
        private const float RamAirDeflatePressure = 15f;
        private const float RamAirReinflatePressure = 30f;
        private const float StarvedBundleMass = 3f;

        private const float StarvedHangSeconds = 0.7f;
        private const float StarvedDragArea = 0.5f;
        private bool _starved;
        private float _lowPressureSince = float.NegativeInfinity;

        // Opening shock cap, kept under the drood's gTolerance of 7.
        private const float MaxOpeningG = 6f;

        private const int ShroudLineCount = 16;

        // Lines are sized from the camera distance to stay about this many pixels wide.
        // A fixed world width breaks up into flickering dashes far away (sub-pixel) and
        // looks thick close by.
        private const float ShroudLinePixels = 1.2f;
        private const float ShroudLineMinWidth = 0.004f;
        private const float ShroudLineFarFraction = 0.6f;
        private const float ShroudLineFadeStart = 15f;
        private const float ShroudLineFadeEnd = 60f;
        private const string CanopyResource = "Craft/Parts/Prefabs/Parachutes/ParachuteStripedVertical";

        private enum CanopyKind
        {
            Round,
            RamAir,
        }

        private enum State
        {
            Packed,
            Armed,
            Deployed,
        }

        private EvaScript _eva;
        private IPartScript _part;
        private Transform _leftShoulder;
        private Transform _rightShoulder;

        private const float ArmedLogInterval = 2f;

        private State _state = State.Packed;
        private Drood2Data _saved;
        private bool _restored;
        private bool _lastActivationGroupOn;
        private bool _wasEvaActive;
        private bool _seenSeated;
        private bool _loggedPhysicsRunning;
        private bool _loggedMissingReferences;
        private bool _loggedSeated;
        private float _nextArmedLogTime;
        private float _extend;
        private float _inflate;
        private bool _inflating;
        private float _collapse;
        private float _visualCollapse;
        private bool _wasGrounded = true;
        private float _lastGroundedTime = float.NegativeInfinity;
        private float _altitudeAboveGround = float.PositiveInfinity;
        private float _feetHeight = float.PositiveInfinity;

        /// <summary>The feet's height above the ground (m; infinite when far).</summary>
        internal float FeetAboveGround => _feetHeight;
        private Transform _leftAnkle;
        private Transform _rightAnkle;

        // The sole is this far (m) below the ankle bone; feet raycasts reach FeetRayRange (m).
        private const float SoleBelowAnkle = 0.08f;
        private const float FeetRayRange = 5f;
        private Vector3 _lastAirborneVelocity;
        private Vector3 _canopyDirection;
        private Vector3 _canopyForward;
        private float _leftPull;
        private float _rightPull;
        private float _frontPull;

        // Turning on the front risers (-1 left .. 1 right), see FrontPullOf.
        private float _frontTurn;
        private float _turnRate;
        private float _flarePull;
        private CanopyKind _canopyKind;
        private bool _jetpackSuspendedHere;

        private Transform _canopyRoot;
        private Transform _canopyMesh;
        private Renderer _canopyRenderer;

        // Plain-fabric copies of the part materials the game puts on the canopy (see
        // UpdateCanopyFabric), keyed by the game's material they were copied from.
        private readonly Dictionary<Material, Material> _fabricMaterials = new Dictionary<Material, Material>();

        // The other way round (a copy to its game material), and a buffer for the renderers'
        // materials (read every frame without allocating).
        private readonly Dictionary<Material, Material> _fabricSources = new Dictionary<Material, Material>();
        private readonly List<Material> _materialBuffer = new List<Material>();
        private float _nextFabricRefreshTime;
        private const float FabricRefreshInterval = 0.5f;
        private LineRenderer[] _lines;
        private LineRenderer[] _toggleLines = new LineRenderer[0];
        private Material _lineMaterial;

        // Where the landed canopy lies (world position, moved along with world recentring
        // and teleports, see UpdateLandedAnchor).
        private bool _hasLandedAnchor;
        private bool _landedInWater;
        private Vector3 _landedAnchor;
        private Vector3 _landedGround;
        private Vector3 _landedNormal;
        private float _groundUnderDrood;
        // Raycast hits come back unsorted, and beyond the buffer's size some are dropped (the
        // nearest among them), so it is sized for busy sites (buildings, pads, crafts).
        private readonly RaycastHit[] _groundHits = new RaycastHit[64];
        private int _meshHeightAxis;
        private float _meshApexSign;
        private float _meshRadius;
        private float _meshHeight;
        private float _meshRimCoordinate;
        private float _meshSkirtShare = 1f;

        // The round canopy's hem points (its gores' lowest corners, measured on the mesh): their
        // angles around the canopy (radians, from its +X toward +Z) and radii (shares of the
        // dome's widest); a line to each. Null: lines evenly round the skirt.
        private float[] _hemAngles;
        private float[] _hemShares;
        private bool _autoDeployed;
        private float _flareStartHeight = float.NaN;
        private float _flareStartSpeed = float.NaN;
        private float _flareStartTime = float.NegativeInfinity;
        private DroodLimbFollower _limbs;
        private bool _visualsBuilt;
        private float _rimRadius;
        private Vector3 _canopyRootLocal;

        // Where the lines or risers leave the harness, from the shoulder bone (m): see RiserAttach.
        private const float RiserForward = 0f;
        private const float RiserUp = 0.09f;
        private const float RiserInward = 0.02f;
        private DroodHangingPose _hangingPose;
        private DroodDeploySound _deploySound;
        private DroodRunOut _runOut;
        private Mesh _wingMesh;

        /// <summary>
        /// Hanging in the harness: chute deployed (from the moment it opens, not only once
        /// inflated), not collapsed, drood airborne and not seated. Drives the calm animator state
        /// and the hanging pose (<see cref="DroodCalmAnimator"/>, <see cref="DroodHangingPose"/>):
        /// the jumper holds the steering lines until touchdown (blending into the falling
        /// animation earlier would turn the body), then lands hard or runs out
        /// (DetectLandingFall).
        /// </summary>
        public bool IsHanging =>
            _state == State.Deployed && _collapse < 0.5f && !_starved && !RecentlyTouchedDown && _eva.EvaActive
            && (!IsRamAir || _extend >= 1f);

        /// <summary>
        /// The canopy is out (from deployment until it lies folded after landing): the chase
        /// camera views are offered and chase it (<see cref="DroodChaseCameraMode"/>).
        /// </summary>
        internal bool CanopyOut => _state == State.Deployed && _collapse < 1f && _eva.EvaActive;

        /// <summary>Standing on something, or in the water: the jumper's weight is off the canopy.</summary>
        private bool TouchedDown => (_eva.IsGrounded || _eva.IsInWater) && !_groundIgnored;

        // After an open start (StartOpen) the ground the drood stood on before the teleport
        // doesn't count until the game reports it off the ground (its grounded state lags the
        // teleport, and a touchdown would fold the canopy and reopen it from empty).
        private bool _groundIgnored;

        // The lines' direction last physics step (the swing's damping against them).
        private Vector3 _lastLines;
        private bool _hasLastLines;
        private const float OpenStartAirSeconds = 1f;
        private float _openStartAt = float.NegativeInfinity;

        // For OpenStartHoldSeconds after an open start the canopy and the drood are held in
        // steady trim each physics step (the teleport's position and speed reach the drood
        // unevenly over its first steps, which set the canopy swinging): the canopy straight
        // above, no yaw or roll; the drood upright facing the heading, not turning; its speed
        // OpenStartForward forward and OpenStartSink down over the ground (the swoop wing's
        // steady glide: SwoopStartForward, SwoopStartSink).
        private const float OpenStartHoldSeconds = 1f;
        private const float OpenStartForward = 15f;
        private const float OpenStartSink = 5f;
        private const float SwoopStartForward = 17f;
        private const float SwoopStartSink = 6.7f;

        /// <summary>The chosen wing's steady glide (m/s): forward and down.</summary>
        internal Vector2 SteadyGlide => IsSwoopWing ? new Vector2(SwoopStartForward, SwoopStartSink) : new Vector2(OpenStartForward, OpenStartSink);
        private const float OpenStartSpeedShare = 0.5f;
        private Vector3 _openHeading;
        private Vector3 _openUp;

        /// <summary>
        /// Touched down now or within LandedGraceSeconds: the ground contact flickers while
        /// running out, and without the grace each flicker would count as hanging again and then
        /// as a new touchdown (the hanging pose and jetpack switching back and forth).
        /// </summary>
        private bool RecentlyTouchedDown => TouchedDown || Time.time - _lastGroundedTime < LandedGraceSeconds;

        /// <summary>
        /// Free look: hanging in first person. The mouse only moves the camera; the turn keys
        /// steer with the lines as in third person; see <see cref="FreeLookUnderCanopyPatch"/>.
        /// </summary>
        public bool FreeLook => IsHanging && _eva.IsFpsActive;

        /// <summary>
        /// The drood may fly the free-fall arch (<see cref="DroodFreeFall"/>): the chute is
        /// packed or armed, or the ram-air is still deploying (before line stretch).
        /// </summary>
        public bool AllowsFreeFall => _state != State.Deployed || (IsRamAir && _extend < 1f);

        /// <summary>
        /// Under the chute in the air: deployed (from the throw on, also deflated), not touched
        /// down, on EVA.
        /// </summary>
        public bool UnderChuteInAir => _state == State.Deployed && !RecentlyTouchedDown && _eva.EvaActive;

        /// <summary>
        /// The canopy's heading from the drood's body, across the lines (<paramref name="lines"/>):
        /// the way it faces when upright, the way its head points when lying (belly to earth
        /// in the free-fall arch, where its facing points along the lines): after opening it
        /// swings upright facing that way.
        /// </summary>
        private Vector3 HeadingFromBody(Vector3 lines)
        {
            var facing = Vector3.ProjectOnPlane(transform.forward, lines);
            var head = Vector3.ProjectOnPlane(transform.up, lines);
            var heading = facing.sqrMagnitude >= head.sqrMagnitude ? facing : head;
            return heading.sqrMagnitude > 1e-6f ? heading.normalized : transform.forward;
        }

        /// <summary>Unit vector from the harness toward the canopy.</summary>
        public Vector3 CanopyDirection => _canopyDirection;

        /// <summary>The canopy's heading (world, across the lines).</summary>
        internal Vector3 CanopyHeading => _canopyForward;

        /// <summary>A point <paramref name="fraction"/> of the way up the lines from the harness (world).</summary>
        internal Vector3 UpTheLines(float fraction) =>
            HarnessPosition + (_drawnDirection.sqrMagnitude > 0.5f ? _drawnDirection : _canopyDirection) * (CurrentLineLength * fraction);

        private float CurrentRadius
        {
            get
            {
                var radius = Mathf.Lerp(DeflatedRadius, InflatedRadius, _inflate);
                return Mathf.Lerp(radius, DeflatedRadius, _collapse);
            }
        }

        private Vector3 HarnessPosition
        {
            get
            {
                if (_leftShoulder != null && _rightShoulder != null)
                {
                    return (_leftShoulder.position + _rightShoulder.position) * 0.5f;
                }

                return transform.position + transform.up;
            }
        }

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _part = _eva.PartScript;
            _hangingPose = GetComponent<DroodHangingPose>();
            _leftShoulder = FindChild(transform, "LeftShoulder");
            _rightShoulder = FindChild(transform, "RightShoulder");
        }

        private void OnDestroy()
        {
            if (_canopyRenderer != null)
            {
                _part?.PartMaterialScript?.RemoveRenderer(_canopyRenderer);
            }

            foreach (var renderer in _extraFabricRenderers)
            {
                if (renderer != null)
                {
                    _part?.PartMaterialScript?.RemoveRenderer(renderer);
                }
            }

            if (_ramAirExtras != null)
            {
                Destroy(_ramAirExtras);
            }

            if (_sliderMesh != null)
            {
                Destroy(_sliderMesh);
            }

            if (_pilotChuteMesh != null)
            {
                Destroy(_pilotChuteMesh);
            }

            if (_bagMesh != null)
            {
                Destroy(_bagMesh);
            }

            DestroyRiserStraps();

            foreach (var fabric in _fabricMaterials.Values)
            {
                if (fabric != null)
                {
                    Destroy(fabric);
                }
            }

            _fabricMaterials.Clear();
            _fabricSources.Clear();

            if (_wingMesh != null)
            {
                Destroy(_wingMesh);
            }

            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
            }

            if (_canopyRoot != null)
            {
                Destroy(_canopyRoot.gameObject);
            }

            if (_lines != null)
            {
                foreach (var line in _lines)
                {
                    if (line != null)
                    {
                        Destroy(line.gameObject);
                    }
                }
            }

            foreach (var line in _toggleLines)
            {
                if (line != null)
                {
                    Destroy(line.gameObject);
                }
            }
        }

        /// <summary>Where the state is kept across quicksave and quickload (the part modifier's data).</summary>
        internal void SetSavedState(Drood2Data data)
        {
            _saved = data;
        }

        /// <summary>
        /// First physics step: continue from the saved state, so a quickload under an open
        /// canopy keeps hanging (no second opening) and a landed canopy stays on the ground.
        /// </summary>
        private void RestoreState()
        {
            _restored = true;

            // The canopy is built now, hidden, so opening it doesn't stall a frame.
            if (!_visualsBuilt)
            {
                _canopyKind = _saved != null && _saved.RamAir ? CanopyKind.RamAir : CanopyKind.Round;
                BuildVisuals();
                SetVisualsActive(false);
            }

            if (_saved == null || _saved.ChuteState == (int)State.Packed)
            {
                return;
            }

            _state = _saved.ChuteState == (int)State.Deployed ? State.Deployed : State.Armed;
            if (_state != State.Deployed)
            {
                return;
            }

            _extend = Mathf.Clamp01(_saved.ChuteExtend);
            _inflate = Mathf.Clamp01(_saved.ChuteInflate);
            _inflating = _extend >= 1f;
            _pilotCollapse = _inflate >= 1f ? 1f : 0f;
            _collapse = Mathf.Clamp01(_saved.ChuteCollapse);
            _visualCollapse = _collapse;
            _canopyDirection = transform.up;
            _canopyForward = HeadingFromBody(_canopyDirection);
            _canopyKind = _saved != null && _saved.RamAir ? CanopyKind.RamAir : CanopyKind.Round;
            _pilotOut = 1f;
            _handThrow = 1f;
            _bagRelease = _extend >= 1f ? 1f : 0f;
            _sliderDown = _extend >= 1f ? _inflate : 0f;
            BuildVisuals();
            SetVisualsActive(true);
            Log($"Parachute restored from the save: extend {_extend:0.00}, inflate {_inflate:0.00}, collapse {_collapse:0.00}.");
        }

        private void SaveState()
        {
            if (_saved == null)
            {
                return;
            }

            _saved.ChuteState = (int)_state;
            _saved.ChuteExtend = _extend;
            _saved.ChuteInflate = _inflate;
            _saved.ChuteCollapse = _collapse;
        }

        private void Deploy(Vector3 airVelocity, float airspeed)
        {
            Log($"Parachute deployed at {airspeed:0} m/s ({(IsSwoopWing ? "swoop wing" : _saved != null && _saved.RamAir ? "ram-air" : "round")} canopy).");
            _state = State.Deployed;
            _pilotCollapse = 0f;
            _pilotTrail = Vector3.zero;
            _extend = 0f;
            _inflate = 0f;
            _inflating = false;
            _collapse = 0f;
            _canopyDirection = airspeed > 1f ? -airVelocity / airspeed : transform.up;
            _canopyForward = HeadingFromBody(_canopyDirection);

            _canopyKind = _saved != null && _saved.RamAir ? CanopyKind.RamAir : CanopyKind.Round;
            _pilotOut = IsRamAir ? 0f : 1f;
            _handThrow = IsRamAir ? 0f : 1f;
            _bagRelease = 0f;
            _sliderDown = 0f;
            _brakesStowed = IsRamAir;
            _keysTaken = false;
            _stallHold = false;
            _onRisers = false;
            _flareReach = 0f;
            BuildVisuals();
            SetVisualsActive(true);
        }

        /// <summary>
        /// The canopy at once open and flying (the swoop course's start, <see cref="DroodSwoopCourse"/>):
        /// AG1 on, the canopy full straight above along <paramref name="up"/>, heading along
        /// <paramref name="heading"/> (directions in the drood's frame), in steady flight (no
        /// turn, swing, surge or lag left from before), hands off (the brakes already unstowed,
        /// nothing pulled); no opening, no malfunction. Only the ram-air.
        /// </summary>
        internal void StartOpen(Vector3 heading, Vector3 up)
        {
            var pod = _part?.CommandPod;
            if (!IsRamAirChosen || pod == null)
            {
                return;
            }

            // From a clean pack (a landed wing's state cleared), then open.
            Repack();
            pod.SetActivationGroupState(ActivationGroup, true);
            _lastActivationGroupOn = true;
            _autoDeployed = true;
            _lastGroundedTime = float.NegativeInfinity;
            _groundIgnored = true;
            _openStartAt = Time.time;
            _openHeading = heading;
            _openUp = up;
            _restored = true;
            _state = State.Deployed;
            _starved = false;
            _extend = 1f;
            _inflate = 1f;
            _inflating = true;
            _collapse = 0f;
            _visualCollapse = 0f;
            _pilotCollapse = 1f;
            _pilotOut = 1f;
            _handThrow = 1f;
            _bagRelease = 1f;
            _sliderDown = 1f;
            _openingPitchShare = 0f;
            _closedSide = 0;
            _canopyDirection = up;
            _canopyForward = heading;
            _stepDirection = up;
            _stepForward = heading;
            _drawnDirection = up;
            _attitudePitch = WingRigging;
            _stepPitch = WingRigging;
            _linesRollRate = 0f;
            _surgeDrawn = 0f;
            _surgePitchApplied = 0f;
            _frontSurge = 0f;
            _frontSurgeHeld = 0f;
            _frontRelease = 0f;
            _lastFrontPull = 0f;
            _openingLagHold = 0f;
            _leftReserveUsed = 0f;
            _rightReserveUsed = 0f;
            _lastPath = Vector3.zero;
            _pathRate = 0f;
            _hasVisualAirVelocity = false;
            _visualAcceleration = Vector3.zero;
            _canopyKind = CanopyKind.RamAir;
            _brakesStowed = false;
            _keysTaken = false;
            _stallHold = false;
            _onRisers = false;
            _flareReach = 0f;
            BuildVisuals();
            SetVisualsActive(true);
            Log("Parachute open at the start (swoop course).");
        }

        /// <summary>The open start's steady hold (see OpenStartHoldSeconds), each physics step.</summary>
        private void HoldOpenStart(ModApi.Craft.ICraftScript craft)
        {
            if (Time.time - _openStartAt >= OpenStartHoldSeconds)
            {
                return;
            }

            var body = _part?.BodyScript?.RigidBody;
            if (body == null)
            {
                return;
            }

            _canopyDirection = _openUp;
            _canopyForward = _openHeading;
            _yawRate = 0f;
            _turnRate = 0f;
            _linesRollRate = 0f;
            body.rotation = Quaternion.LookRotation(_openHeading, _openUp);
            body.angularVelocity = Vector3.zero;
            // OpenStartSpeedShare of the difference each step: the craft's surface velocity may lag
            // the body's by a step.
            var glide = SteadyGlide;
            var wanted = _openHeading * glide.x - _openUp * glide.y;
            body.velocity += (wanted - (Vector3)craft.SurfaceVelocity) * OpenStartSpeedShare;
        }

        /// <summary>The ram-air is the chosen canopy (the designer's setting).</summary>
        internal bool IsRamAirChosen => _saved != null && _saved.RamAir;

        private void Repack()
        {
            if (_state == State.Deployed)
            {
                Log("Parachute repacked.");
            }

            _state = State.Packed;
            _starved = false;
            _lowPressureSince = float.NegativeInfinity;
            _leftPull = 0f;
            _rightPull = 0f;
            _frontPull = 0f;
            _frontTurn = 0f;
            _yawRate = 0f;
            _turnRate = 0f;
            _linesRolling = false;
            _frontRollForce = 0f;
            _leftStall = 0f;
            _rightStall = 0f;
            _closedSide = 0;
            _hasLastLines = false;
            _leftDeepStall = 0f;
            _rightDeepStall = 0f;
            _lastAirspeed = -1f;
            _airspeedChange = 0f;
            _flarePull = 0f;
            _leftLiftDeflection = 0f;
            _brakeHold = 0f;
            _rightLiftDeflection = 0f;
            _extend = 0f;
            _inflate = 0f;
            _inflating = false;
            _collapse = 0f;
            _visualCollapse = 0f;
            _hasLandedAnchor = false;
            _landedInWater = false;
            SetVisualsActive(false);
        }

        private void SetVisualsActive(bool active)
        {
            if (!_visualsBuilt)
            {
                return;
            }

            _canopyRoot.gameObject.SetActive(active);
            if (_ramAirExtras != null)
            {
                _ramAirExtras.SetActive(active);
            }

            foreach (var line in _lines)
            {
                line.gameObject.SetActive(active);
            }

            foreach (var line in _toggleLines)
            {
                line.gameObject.SetActive(active);
            }
        }

        private static void Log(string message)
        {
            DroodLog.Debug(message);
        }

        private static void LogOnce(ref bool logged, string message)
        {
            if (!logged)
            {
                logged = true;
                Log(message);
            }
        }

        private static string Found(object reference)
        {
            return reference != null ? "ok" : "missing";
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
