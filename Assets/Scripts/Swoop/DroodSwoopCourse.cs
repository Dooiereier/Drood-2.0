using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft.Parts;
using ModApi.Flight.GameView;
using ModApi.Flight.Sim;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The swoop course (the designer's Challenge "Swoop"): a lane on the ground with an entry
    /// gate, and a start under an open ram-air above it.
    /// - Launched with the Swoop chosen, the drood takes the launch spot as the entry gate and the
    ///   way it faces as the lane's direction, and is teleported beside the course, StartBeforeLine
    ///   before the lane's line at the designer's start height and distance back from the starting
    ///   line, flying across it toward the turn point under its open
    ///   canopy (DroodParachute.StartOpen); the panel counts down to the turn (a 270° turn to the
    ///   right from there lines up on the lane),
    ///   at launch in first person with the steering display full on (AG4); a restart keeps the
    ///   view and AG4 as they are.
    /// - AG2 does the same again (standing; in the air a second press within
    ///   RestartConfirmSeconds restarts the run); without a course the spot and facing where the
    ///   drood stands become one. Its key works too, as the drop zone's.
    /// - A run counts when the feet pass between the gate's poles (GateWidth apart) below their tops
    ///   (GateHeight above the gate's ground) without a touchdown before; its score is how far
    ///   down the lane the feet first touch the ground, however the landing goes. Leaving the
    ///   lane (LaneWidth) before that scores nothing. The best is kept like the drop
    ///   zone's (<see cref="DroodDropChallengeBests"/>, as "Swoop", in centimetres).
    /// - The marks (<see cref="DroodSwoopCourseMarks"/>) show while the course exists.
    ///
    /// The gate's spot (surface coordinates) and the lane's direction (a surface vector) are
    /// saved with the drood (<see cref="Drood2Data"/>). A quickload keeps the course; a run in
    /// progress isn't saved, so it ends unscored.
    /// </summary>
    public class DroodSwoopCourse : MonoBehaviour
    {
        private const string Title = "Swoop";


        // The course (m): the gate's width and its height limit (the poles' tops) above the gate's ground, the
        // lane's width and its marked length.
        internal const float GateWidth = 22.5f;
        internal const float GateHeight = 3f;
        internal const float LaneWidth = 22.5f;
        internal const float LaneLength = 200f;

        // The start, set up for a swooper's 270° turn: the designer's start distance back from the
        // starting line (Drood2Data.SwoopStartDistance) and its height above the course's ground
        // (SwoopStartHeight), flying across the lane at its wing's steady glide
        // (DroodParachute.SteadyGlide) toward the turn point (unmarked: as far back, TurnPointBeforeLine
        // before the lane's line and TurnEarlySeconds of that glide more), StartBeforeLine -
        // TurnPointBeforeLine before it; a 270° turn to the right from there comes round onto the lane.
        // The panel counts down the seconds to the turn point, then shows "TURN" for TurnCueSeconds.
        private const double StartBeforeLine = 115;
        private const float TurnCueSeconds = 1.5f;
        internal const float TurnPointBeforeLine = 48f;
        private const float TurnEarlySeconds = 1f;

        /// <summary>
        /// The turn point in the course's frame: before the lane's line (on the start's side),
        /// before the gate (m): the designer's start distance back from the starting line.
        /// </summary>
        private Vector2 TurnPoint()
        {
            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            var glide = _parachute != null ? _parachute.SteadyGlide.x : 0f;
            return new Vector2(-(TurnPointBeforeLine + glide * TurnEarlySeconds), -(_data != null ? _data.SwoopStartDistance : 0f));
        }

        // On the approach the panel shows the height above the gate's ground, the distance to the
        // gate and the speed (a swooper's altimeter), every AltimeterSeconds.
        private const float AltimeterSeconds = 0.25f;
        private float _nextAltimeter;
        private bool _turnReached;
        private float _turnCueUntil = float.NegativeInfinity;

        private const float LaunchDelay = 1f;

        // After the launch's start the view is first person, with the steering display full on
        // (AG4); after a restart (AG2) the view and AG4 stay as they are (the repack takes the
        // chase views out of the list for a moment, so a chase view that was selected is
        // selected again; any other view stays by itself). Selected again each frame until it
        // has held ChaseHeldFrames in a row (a chase view joins the list with the canopy), for at
        // most ChaseSelectSeconds.
        private const int ChaseHeldFrames = 10;
        private const float ChaseSelectSeconds = 3f;
        private float _chaseUntil = float.NegativeInfinity;
        private int _chaseFrames;
        private DroodChaseCameraMode _chaseView;
        private bool _firstPersonView;
        private const float RestartConfirmSeconds = 1.5f;

        // Down this long (s) after the gate without a landing report (the canopy away, water):
        // the run ends unscored.
        private const float NoLandingAfter = 1f;

        private const string ScoreColour = "#2E9E4A";

        // The panel stays this long after a run's result (s).
        private const float ResultShownSeconds = 30f;
        private float _hudUntil = float.PositiveInfinity;

        private enum Run
        {
            None,
            Approach,
            Lane,
        }

        private EvaScript _eva;
        private IPartScript _part;
        private Drood2Data _data;

        // The horizontal speed (m/s) through the gate, for the run's result.
        private float _gateHorizontal;

        // After the gate, the feet BelowGateTolerance (m) below the gate's ground (off an edge,
        // down a slope) end the distance there, before any touchdown (the landing still grades).
        private const float BelowGateTolerance = 1f;
        private bool _hasBelowGate;
        private float _belowGateDistance;
        private DroodParachute _parachute;
        private DroodLandingFall _landingFall;
        private DroodSwoopCourseMarks _marks;
        private Transform _leftAnkle;
        private Transform _rightAnkle;

        private bool _hasCourse;
        private string _planet;
        private Vector3d _gate;
        private Vector3d _heading;

        private Run _run;
        private bool _hasLast;
        private float _lastAlong;
        private float _lastSide;
        private float _lastHeight;
        private float _downSince = float.NegativeInfinity;
        private float _sinceStart;
        private float _restartAsked = float.NegativeInfinity;
        private bool _loadChecked;
        private bool _hudShown;
        private bool _marksShown;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _part = _eva.PartScript;
            _landingFall = GetComponent<DroodLandingFall>();
        }

        private void OnDestroy()
        {
            if (_hudShown)
            {
                DroodDropChallengeHud.Hide();
            }

            _marks?.Destroy();
            _marks = null;
        }

        /// <summary>The saved choice and course (from the modifier at flight start).</summary>
        internal void SetData(Drood2Data data)
        {
            _data = data;
            DroodLog.Debug($"Challenge: '{data?.Challenge ?? "no data"}', the drood {(_eva != null && _eva.EvaActive ? "on EVA" : "in a crew compartment")}.");
            _hasCourse = DroodDropZone.TryParse(data?.DropTarget, out _planet, out _gate)
                && DroodDropZone.TryParse(data?.SwoopHeading, out _, out _heading);
        }

        /// <summary>Game-loop Update: the launch teleport and AG2. Only with the Swoop chosen.</summary>
        internal void Step()
        {
            var pod = _part?.CommandPod;
            var craft = _part?.CraftScript;
            if (pod == null || craft == null || _data == null || !_data.Swoop)
            {
                return;
            }

            if (!_loadChecked)
            {
                // The group names once more, now the data is in; the player's panel shown again.
                _loadChecked = true;
                _marksShown = _hasCourse;
                Drood2ActivationGroups.Ensure(_part);
                if (_eva.IsPlayerCraft)
                {
                    Drood2ActivationGroups.RefreshPanel();
                }
            }

            _sinceStart += Time.deltaTime;
            if (!_data.DropStarted && _sinceStart >= LaunchDelay)
            {
                _data.DropStarted = true;
                if (_eva.IsPlayerCraft && _eva.EvaActive && _eva.IsGrounded && DroodChallenge.CanStartHere(craft, _part, Title) && TakeCourse(craft))
                {
                    DroodLog.Debug("Swoop: launched; the launch spot is the entry gate.");
                    Teleport(pod, craft, true);
                }
            }

            if (DroodChallenge.Ag2KeyPressed(_eva, CanStart() || CanRestart()))
            {
                pod.SetActivationGroupState(DroodDropZone.ActivationGroup, true);
            }

            if (pod.GetActivationGroupState(DroodDropZone.ActivationGroup))
            {
                pod.SetActivationGroupState(DroodDropZone.ActivationGroup, false);
                if (CanStart())
                {
                    if (DroodChallenge.CanStartHere(craft, _part, Title))
                    {
                        if (!_hasCourse && TakeCourse(craft))
                        {
                            DroodLog.Debug("Swoop: the drood's spot is the entry gate.");
                        }

                        if (_hasCourse)
                        {
                            Teleport(pod, craft, false);
                        }
                    }
                }
                else if (CanRestart())
                {
                    if (Time.time - _restartAsked <= RestartConfirmSeconds)
                    {
                        _restartAsked = float.NegativeInfinity;
                        DroodLog.Debug("Swoop: the run restarted in the air.");
                        Teleport(pod, craft, false);
                    }
                    else
                    {
                        _restartAsked = Time.time;
                        ShowHud("Press AG2 again to restart", string.Empty);
                    }
                }
            }

            if (_hudShown && _run == Run.None && Time.time > _hudUntil)
            {
                DroodDropChallengeHud.Hide();
                _hudShown = false;
            }

            if (Time.time < _chaseUntil && _eva.IsPlayerCraft)
            {
                var selected = _firstPersonView ? _eva.IsFpsActive : _chaseView != null && _chaseView.Selected;
                _chaseFrames = selected ? _chaseFrames + 1 : 0;
                if (_chaseFrames >= ChaseHeldFrames)
                {
                    _chaseUntil = float.NegativeInfinity;
                }
                else if (_chaseFrames == 0)
                {
                    if (_firstPersonView)
                    {
                        SelectFirstPerson();
                    }
                    else
                    {
                        _chaseView?.Select();
                    }
                }
            }

            if (_run == Run.Approach && _eva.IsPlayerCraft && Time.time >= _nextAltimeter && Time.time - _restartAsked > RestartConfirmSeconds && CourseFrame(out var origin, out var courseUp, out _, out _))
            {
                _nextAltimeter = Time.time + AltimeterSeconds;
                var feet = Feet();
                var height = Vector3.Dot(feet - origin, courseUp);
                var toGate = Vector3.ProjectOnPlane(origin - feet, courseUp).magnitude;
                var speed = _part?.CraftScript?.SurfaceVelocity.magnitude ?? 0.0;

                var main = $"Height {height:0} m";
                var turnIcon = false;
                if (!_data.SwoopStraight)
                {
                    // Before the turn point: the seconds to it along the way it's flown; then "TURN".
                    CourseFrame(out _, out _, out var courseForward, out var courseRight);
                    var turnPointNow = TurnPoint();
                    var turnAt = origin + courseRight * turnPointNow.x + courseForward * turnPointNow.y;
                    var level = Vector3.ProjectOnPlane((Vector3)(_part?.CraftScript?.SurfaceVelocity ?? Vector3d.zero), courseUp);
                    var ahead = Vector3.Dot(Vector3.ProjectOnPlane(turnAt - feet, courseUp), courseRight);
                    if (!_turnReached && ahead <= 0f)
                    {
                        _turnReached = true;
                        _turnCueUntil = Time.time + TurnCueSeconds;
                    }

                    var toTurn = !_turnReached && level.magnitude > 1f ? ahead / Mathf.Max(Vector3.Dot(level, courseRight), 1f) : 0f;
                    main = !_turnReached ? $"swooping turn in {Mathf.CeilToInt(toTurn)}" : Time.time < _turnCueUntil ? "TURN" : $"Height {height:0} m";
                    turnIcon = !_turnReached || Time.time < _turnCueUntil;
                }

                ShowHud(main, $"height {height:0} m  ·  gate {toGate:0} m  ·  {speed:0} m/s", turnIcon);
            }

            // Down after the gate with no landing report: no score.
            var down = _eva.IsGrounded || _eva.IsInWater;
            if (_run != Run.None && down)
            {
                if (float.IsNegativeInfinity(_downSince))
                {
                    _downSince = Time.time;
                }
                else if (Time.time - _downSince > NoLandingAfter)
                {
                    EndWithoutScore(_run == Run.Approach ? "touched down before the gate" : "a landing counts under an open canopy, on solid ground");
                }
            }
            else
            {
                _downSince = float.NegativeInfinity;
            }
        }

        /// <summary>
        /// Game-loop FixedUpdate (after the parachute's): the feet against the gate and the lane.
        /// </summary>
        internal void PhysicsStep()
        {
            if (_run == Run.None || !CourseFrame(out var origin, out var up, out var forward, out var right))
            {
                return;
            }

            var feet = Feet();
            var along = Vector3.Dot(feet - origin, forward);
            var side = Vector3.Dot(feet - origin, right);
            var height = Vector3.Dot(feet - origin, up);
            if (_run == Run.Approach && _hasLast && _lastAlong < 0f && along >= 0f)
            {
                // Through the gate's line between this step and the last: where exactly.
                var t = _lastAlong / (_lastAlong - along);
                var crossSide = Mathf.Lerp(_lastSide, side, t);
                var crossHeight = Mathf.Lerp(_lastHeight, height, t);
                if (Mathf.Abs(crossSide) > GateWidth * 0.5f)
                {
                    EndWithoutScore($"missed the gate, {Mathf.Abs(crossSide) - GateWidth * 0.5f:0} m to the {(crossSide < 0f ? "left" : "right")} of it, {crossHeight:0} m up");
                }
                else if (crossHeight > GateHeight)
                {
                    EndWithoutScore($"higher than the gate's poles ({crossHeight:0.0} m up, below {GateHeight:0.0} m counts)");
                }
                else
                {
                    _run = Run.Lane;
                    var speed = _part?.CraftScript?.SurfaceVelocity.magnitude ?? 0.0;
                    _gateHorizontal = (float)(_part?.CraftScript?.FlightData?.LateralSurfaceVelocity ?? 0.0);
                    DroodLog.Debug($"Swoop: through the gate {crossHeight:0.00} m up, {crossSide:0.0} m off the middle, at {speed:0.0} m/s ({_gateHorizontal:0.0} m/s horizontal).");
                    ShowHud("In the lane", $"Gate at {speed * 3.6:0} km/h, {crossHeight:0.0} m up");
                }
            }
            else if (_run == Run.Lane && !_hasBelowGate && height < -BelowGateTolerance)
            {
                // Below the gate's ground (off an edge, down a slope): the distance is taken here,
                // where it crossed that level; the landing still gives the grade.
                var t = (_lastHeight + BelowGateTolerance) / Mathf.Max(_lastHeight - height, 1e-4f);
                _belowGateDistance = Mathf.Lerp(_lastAlong, along, Mathf.Clamp01(t));
                _hasBelowGate = true;
                DroodLog.Debug($"Swoop: below the gate's ground {_belowGateDistance:0.0} m down the lane; the distance is taken there.");
            }
            else if (_run == Run.Lane && !_hasBelowGate && Mathf.Abs(side) > LaneWidth * 0.5f)
            {
                EndWithoutScore("left the lane");
            }

            _lastAlong = along;
            _lastSide = side;
            _lastHeight = height;
            _hasLast = true;
        }

        /// <summary>
        /// The landing (from DroodParachute, after its Flight Log line): the run's score on the
        /// next line (true: scored).
        /// </summary>
        internal bool Landed(int grade, DroodLandingFall.Kind kind)
        {
            if (_run == Run.None)
            {
                return false;
            }

            if (_run == Run.Approach)
            {
                EndWithoutScore("touched down before the gate");
                return false;
            }

            if (!CourseFrame(out var origin, out _, out var forward, out _))
            {
                _run = Run.None;
                return false;
            }

            _run = Run.None;
            var distance = _hasBelowGate ? _belowGateDistance : Vector3.Dot(Feet() - origin, forward);
            var centimetres = Mathf.RoundToInt(distance * 100f);
            // Kept per variant, difficulty and wing ("Swoop 270 turn, Practice, swoop wing").
            var bestKey = $"{_data.Challenge}, {_data.SwoopDifficulty}{(_data.SwoopWing ? ", swoop wing" : string.Empty)}";
            var previous = DroodDropChallengeBests.Best(bestKey);
            var newBest = DroodDropChallengeBests.Record(bestKey, centimetres, distance, grade, 0f);
            var best = newBest
                ? (previous >= 0 ? $"New best! (was {previous / 100f:0.0} m)" : "New best!")
                : $"Best: {previous / 100f:0.0} m";
            var gate = $"{_gateHorizontal * 3.6f:0} km/h horizontal through the gate";
            var text = $"<color={ScoreColour}>Swoop: <b>{distance:0.0} m</b> (landing grade {grade}, {gate}){(newBest ? " --- New best!" : string.Empty)}</color>";
            Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.FlightLog?.AddLog(text, ModApi.Flight.UI.FlightLogEntryCategory.Default, false, _part);
            DroodLog.Debug(text);
            if (_eva.IsPlayerCraft)
            {
                DroodDropChallengeHud.ShowLines("Swoop Distance", $"{distance:0.0}<size=60%> m</size>", $"landing grade {grade}  ·  gate {_gateHorizontal * 3.6f:0} km/h horizontal", best);
                _hudShown = true;
                _hudUntil = Time.time + ResultShownSeconds;
            }

            return true;
        }

        /// <summary>Late chain: the course's marks placed as the frame is drawn.</summary>
        internal void PlaceMarks()
        {
            var show = _marksShown && _hasCourse && _data != null && _data.Swoop && _eva.IsPlayerCraft
                && CourseFrame(out _, out _, out _, out _);
            if (!show)
            {
                _marks?.Hide();
                return;
            }

            CourseFrame(out var origin, out var up, out var forward, out _);
            _marks = _marks ?? new DroodSwoopCourseMarks(gameObject.layer, _part.BodyScript?.RigidBody);
            _marks.Place(origin, up, forward);
        }

        private bool CanStart() => DroodChallenge.Down(_eva);

        private bool CanRestart() => _hasCourse && _eva.EvaActive && !_eva.IsGrounded && !_eva.IsInWater && DroodChallenge.NormalTime();

        /// <summary>The ground under the drood becomes the entry gate, the way it faces the lane's direction.</summary>
        private bool TakeCourse(ModApi.Craft.ICraftScript craft)
        {
            if (!(craft.CraftNode?.Parent is IPlanetNode planet))
            {
                return false;
            }

            var frame = craft.ReferenceFrame;
            var up = -(Vector3)craft.FlightData.GravityFrameNormalized;
            var facing = Vector3.ProjectOnPlane(transform.forward, up);
            if (facing.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            var ground = transform.position;
            var hits = Physics.RaycastAll(transform.position + up, -up, 5f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.distance < best && hit.collider.GetComponentInParent<EvaScript>() != _eva && hit.rigidbody != _part.BodyScript?.RigidBody)
                {
                    best = hit.distance;
                    ground = hit.point;
                }
            }

            _gate = planet.PlanetVectorToSurfaceVector(frame.FrameToPlanetPosition(ground));
            var heading = planet.PlanetVectorToSurfaceVector(frame.FrameToPlanetVector(facing.normalized));
            var gateUp = _gate.normalized;
            _heading = (heading - gateUp * Vector3d.Dot(heading, gateUp)).normalized;
            _planet = planet.Name;
            _hasCourse = true;
            _data.DropTarget = DroodDropZone.Format(_planet, _gate);
            _data.SwoopHeading = DroodDropZone.Format(_planet, _heading);
            return true;
        }

        /// <summary>
        /// StartBeforeLine before the lane's line, at the designer's start height and distance, flying across the lane toward the turn point
        /// under the open ram-air.
        /// </summary>
        private void Teleport(ICommandPod pod, ModApi.Craft.ICraftScript craft, bool launch)
        {
            var node = craft.CraftNode;
            if (!(node?.Parent is IPlanetNode planet) || planet.Name != _planet)
            {
                DroodLog.Debug("Swoop: the course is on another planet; no teleport.");
                return;
            }

            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            if (_parachute == null)
            {
                return;
            }

            if (DroodChallenge.TooMuchAttached(craft, _part, "Swoop"))
            {
                return;
            }

            // The chase view selected now, kept through a restart.
            var chaseView = DroodChaseCameraMode.SelectedMode;
            DroodChallenge.ReadyForJump(_eva, _landingFall);

            // The lane's right in surface coordinates, as the frame (and the marks) have it.
            var up = _gate.normalized;
            var right = Vector3d.Cross(up, _heading).normalized;
            var frame0 = craft.ReferenceFrame;
            var rightInFrame0 = (Vector3)frame0.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(right));
            var unityRight = Vector3.Cross((Vector3)frame0.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(up)), (Vector3)frame0.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(_heading)));
            if (Vector3.Dot(rightInFrame0, unityRight) < 0f)
            {
                right = -right;
            }

            var glide = _parachute.SteadyGlide;
            // The designer's start height; the turn after the glide to it.
            var lead = StartBeforeLine - TurnPointBeforeLine;
            double startHeight = _data.SwoopStartHeight;
            var turnHeight = startHeight - glide.y * lead / glide.x;
            var turnInCourse = TurnPoint();
            var turnPoint = _gate + right * turnInCourse.x + _heading * turnInCourse.y;
            var start = (turnPoint - right * lead).normalized * (_gate.magnitude + startHeight);
            var flyTo = right;
            if (_data.SwoopStraight)
            {
                // Straight in: behind the starting line on the lane's line, facing down the lane.
                startHeight = _data.StraightStartHeight;
                start = (_gate - _heading * _data.StraightStartDistance).normalized * (_gate.magnitude + startHeight);
                flyTo = _heading;
            }

            var startUp = start.normalized;
            var heading = (flyTo - startUp * Vector3d.Dot(flyTo, startUp)).normalized;
            var velocity = planet.CalculateSurfaceVelocity(start) + heading * glide.x - startUp * glide.y;
            DroodChallenge.MoveTo(node, planet.SurfaceVectorToPlanetVector(start), planet.SurfaceVectorToPlanetVector(velocity));

            // Upright, facing across the lane toward the turn point, under the canopy flying the same way.
            var frame = craft.ReferenceFrame;
            var forwardInFrame = ((Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(heading))).normalized;
            var upInFrame = ((Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(up))).normalized;
            var body = _part.BodyScript?.RigidBody;
            if (body != null)
            {
                body.rotation = Quaternion.LookRotation(forwardInFrame, upInFrame);
                body.angularVelocity = Vector3.zero;
            }

            _parachute.StartOpen(forwardInFrame, upInFrame);

            _run = Run.Approach;
            _hasLast = false;
            _hasBelowGate = false;
            _firstPersonView = launch;
            _chaseView = launch ? null : chaseView;
            _chaseUntil = _firstPersonView || _chaseView != null ? Time.time + ChaseSelectSeconds : float.NegativeInfinity;
            if (launch && _eva.IsPlayerCraft)
            {
                pod.SetActivationGroupState(DroodSteeringDisplay.ActivationGroup, true);
            }
            _chaseFrames = 0;
            _downSince = float.NegativeInfinity;
            _marksShown = true;
            ShowHud($"Through the gate below {GateHeight:0.#} m", string.Empty);
            _nextAltimeter = Time.time;
            _turnCueUntil = float.NegativeInfinity;
            _turnReached = false;
            DroodLog.Debug(_data.SwoopStraight
                ? $"Swoop: teleported {startHeight:0} m up, {_data.StraightStartDistance:0} m straight behind the starting line, under the open canopy."
                : $"Swoop: teleported {startHeight:0} m up, {lead:0} m before the turn point (turn from {turnHeight:0} m, {_data.SwoopStartDistance:0} m back from the starting line), under the open canopy.");
        }

        /// <summary>The drood's first-person view selected (the camera view on its first-person camera).</summary>
        private void SelectFirstPerson()
        {
            var manager = Assets.Scripts.Flight.GameView.Cameras.CameraManagerScript.Instance;
            if (manager == null || !(manager.CameraModes is System.Collections.IList modes))
            {
                return;
            }

            foreach (var item in modes)
            {
                if (item is Assets.Scripts.Flight.GameView.Cameras.CameraMode mode
                    && mode.CameraController is Assets.Scripts.Flight.GameView.Cameras.FirstPersonCameraController)
                {
                    if (!mode.IsSelected)
                    {
                        manager.SelectCameraMode(mode);
                    }

                    return;
                }
            }
        }

        // The Course Camera's spot (DroodCourseCamera): CameraPastGate down the lane from the
        // gate and CameraToLeft to its left, at the gate's ground level.
        private const float CameraPastGate = 20f;
        private const float CameraToLeft = 50f;

        /// <summary>The Course Camera's spot (world, the gate's ground level) and up, while there is a course.</summary>
        internal bool CameraSpot(out Vector3 spot, out Vector3 up)
        {
            spot = up = Vector3.zero;
            if (_data == null || !_data.Swoop || !CourseFrame(out var origin, out up, out var forward, out var right))
            {
                return false;
            }

            spot = origin + forward * CameraPastGate - right * CameraToLeft;
            return true;
        }

        /// <summary>The course's frame (world): the gate's ground point, up, down the lane, and to its right.</summary>
        private bool CourseFrame(out Vector3 origin, out Vector3 up, out Vector3 forward, out Vector3 right)
        {
            origin = up = forward = right = Vector3.zero;
            var craft = _part?.CraftScript;
            if (!_hasCourse || craft == null || !(craft.CraftNode?.Parent is IPlanetNode planet) || planet.Name != _planet)
            {
                return false;
            }

            var frame = craft.ReferenceFrame;
            origin = (Vector3)frame.PlanetToFramePosition(planet.SurfaceVectorToPlanetVector(_gate));
            up = ((Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(_gate.normalized))).normalized;
            forward = Vector3.ProjectOnPlane((Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(_heading)), up).normalized;
            right = Vector3.Cross(up, forward);
            return true;
        }

        /// <summary>Between the ankles (the drood's position without them).</summary>
        private Vector3 Feet()
        {
            _leftAnkle = _leftAnkle != null ? _leftAnkle : DroodChallenge.FindBone(this, "LeftAnkle");
            _rightAnkle = _rightAnkle != null ? _rightAnkle : DroodChallenge.FindBone(this, "RightAnkle");
            return _leftAnkle != null && _rightAnkle != null ? (_leftAnkle.position + _rightAnkle.position) * 0.5f : transform.position;
        }

        private void EndWithoutScore(string reason)
        {
            _run = Run.None;
            var text = $"<color={ScoreColour}>Swoop: no score ({reason}).</color>";
            Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.FlightLog?.AddLog(text, ModApi.Flight.UI.FlightLogEntryCategory.Default, false, _part);
            DroodLog.Debug(text);
            if (_eva.IsPlayerCraft)
            {
                DroodDropChallengeHud.ShowLines(Title, "No score", char.ToUpperInvariant(reason[0]) + reason.Substring(1) + ".", string.Empty);
                _hudShown = true;
                _hudUntil = Time.time + ResultShownSeconds;
            }
        }

        private void ShowHud(string main, string detail, bool turnIcon = false)
        {
            if (_eva.IsPlayerCraft)
            {
                DroodDropChallengeHud.ShowLines(Title, $"<size=60%>{main}</size>", detail, string.Empty, turnIcon);
                _hudShown = true;
                _hudUntil = float.PositiveInfinity;
            }
        }
    }
}
