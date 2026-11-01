using System.Globalization;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft.Parts;
using ModApi.Flight.GameView;
using ModApi.Flight.Sim;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The drop zone: a target on the ground and a teleport up to jump at it.
    /// - Launched with "Drop Zone Challenge" switched on in the designer, the drood takes the launch spot
    ///   as its target and is teleported ExitHeight above it, ExitOffset to the side in a
    ///   random direction, at rest over the ground, the canopy packed; with the plane, onto a run
    ///   passing that point (its closest approach) ApproachSeconds later.
    /// - AG2, standing (or landed, at once, also during a landing animation, which ends), does
    ///   the same again over the target; without a target the spot where it stands becomes one. AG2 switches itself off again. Its key works too
    ///   (droods ignore activation-group keys, see <see cref="DroodKeys"/>), only then.
    /// - The target's rings (<see cref="DroodDropZoneRings"/>) show from each teleport until
    ///   RingsAfterLanding seconds after the landing.
    /// - Once the drood is in the free-fall arch after a teleport, the view switches to Drood
    ///   Chase (once per jump; the player can switch away).
    /// - The landing after a teleport is scored (<see cref="Landed"/>): a second Flight Log line
    ///   under the landing's own, out of 1000 for near, soft and quick.
    /// - The panel at the top of the screen (<see cref="DroodDropChallengeHud"/>) shows the
    ///   running timer during the jump, then the score and the personal best
    ///   (<see cref="DroodDropChallengeBests"/>) as long as the target shows.
    ///
    /// The target is a point fixed to the planet's surface (surface coordinates, turning with
    /// the planet), saved with the drood (<see cref="Drood2Data"/>). The teleport is the game's
    /// own (FlightSceneScript.TeleportPlayerToTarget): the craft node's state vectors are set
    /// and its frame state recalculated; DroodParachute.OnFrameShift and the modifier's body
    /// sync follow as for any teleport.
    /// </summary>
    public class DroodDropZone : MonoBehaviour
    {
        public const int ActivationGroup = 2;

        // The exit point: this high above the target and this far to the side (m): the ram-air
        // glides about 3:1. With the plane, that's
        // its run's closest approach to the target, ApproachSeconds after the start (the run
        // passes it on its left, the door's side, square to the direction toward the target).
        private const double ExitHeight = 3000;
        private const double ExitOffset = 1300;
        private const double ApproachSeconds = 30;

        // The rings go this long after the landing (s).
        private const float RingsAfterLanding = 30f;

        // A landing counts after at least this long in the air (s): a teleport's first frames
        // can still report the drood grounded.
        private const float AirborneBeforeLanding = 1f;

        // The launch teleport waits this long after the flight starts (s), for the craft and the
        // ground under it to settle.
        private const float LaunchDelay = 1f;

        // The score (out of 1000) of the landing after a teleport:
        // - near (NearPoints): the feet's middle at touchdown from the target's centre, all of it
        //   exactly on the centre, falling with the square of the distance to none at NearNone
        //   (m, the target's edge): a close miss costs little, a far one a lot;
        // - soft (SoftPointsPerGrade per landing grade, 1..10);
        // - quick (QuickPoints): from the teleport to touchdown, all of it within QuickFull (s),
        //   none from QuickNone; times the square root of the near and soft shares' product (each
        //   0..1), so speed only pays with a landing on the target (a fast crash beside it scores
        //   nothing for speed) without taking a good jump's lost points twice over.
        private const float NearPoints = 350f;
        private const float NearNone = 50f;
        private const float SoftPointsPerGrade = 40f;
        private const float QuickPoints = 250f;
        private const float QuickFull = 70f;
        private const float QuickNone = 180f;
        // The score line's colour (rich text): a darker green.
        private const string ScoreColour = "#2E9E4A";

        // A teleport during the jump (any but the challenge's own) ends it without a score: a
        // move between two frames of more than TeleportMinJump (m) and TeleportSpeedShare times
        // what the drood's speed covers in that frame, measured on the planet's surface (the
        // game's shifts of its coordinate origin don't count).
        private const double TeleportMinJump = 50;
        private const double TeleportSpeedShare = 3;
        private Vector3d _lastSurface;
        private bool _hasLastSurface;

        // In the air during a jump AG2 restarts it on a second press within RestartConfirmSeconds
        // (the 2 key is next to the parachute's 1), unscored.
        private const float RestartConfirmSeconds = 1.5f;
        private const string RestartPrompt = "Press AG2 again to restart";
        private const string JumpPrompt = "Press W to jump";
        private float _restartAsked = float.NegativeInfinity;

        // A touchdown with no landing report by this long after it (s) scores nothing.
        private const float NoScoreAfter = 1f;

        // Drood Chase is selected again each frame of the arch until it has stayed selected this
        // many frames in a row (a restart in the air takes the chase views out at the repack and
        // back in with the arch, and a single select may not hold then).
        private const int ChaseHeldFrames = 10;

        // The chase view selected when a jump restarts (AG2): the repack takes it out of the
        // list, so it's selected again once it's back (Drood Chase with the arch, Canopy Chase
        // with the canopy), until it has held ChaseHeldFrames.
        private DroodChaseCameraMode _keptView;
        private int _keptFrames;

        private EvaScript _eva;
        private IPartScript _part;
        private Transform _leftAnkle;
        private Transform _rightAnkle;

        // The jump being flown: from the teleport (its time) until its landing is scored; and
        // whether its view has switched to Drood Chase.
        private bool _jumping;

        // Loaded in the air during a jump: the ride, the exit time and the score aren't saved, so
        // it scores nothing, but AG2 can start it again.
        private bool _loadedMidJump;
        private bool _loadedPromptShown;
        private float _exitTime;
        private bool _chaseSelected;
        private int _chaseFrames;
        private int _chaseSelects;
        private bool _hudShown;
        private DroodFreeFall _freeFall;
        private DroodPlaneRide _planeRide;
        private Drood2Data _data;
        private DroodLandingFall _landingFall;
        private DroodDropZoneRings _rings;

        private bool _hasTarget;
        private Vector3d _target;
        private string _targetPlanet;

        // Time since the flight started, time in the air since the last teleport, and when the
        // drood landed after it (s).
        private float _sinceStart;
        private float _airborne;
        private float _landedAt = float.NegativeInfinity;
        private bool _ringsShown;
        private bool _loadChecked;

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

            if (_rings != null)
            {
                _rings.Destroy();
                _rings = null;
            }
        }

        /// <summary>The saved switch and target (from the modifier at flight start).</summary>
        internal void SetData(Drood2Data data)
        {
            _data = data;
            _hasTarget = TryParse(data?.DropTarget, out _targetPlanet, out _target);
        }

        /// <summary>
        /// Game-loop Update: the launch teleport, AG2, and when the rings show. Only with the
        /// designer's Drop Zone Challenge switch on; off, AG2 is left to the player's own parts.
        /// </summary>
        internal void Step()
        {
            var pod = _part?.CommandPod;
            var craft = _part?.CraftScript;
            if (_data != null && _data.DropZone)
            {
                DroodPlaneScenery.Want();
            }

            if (pod == null || craft == null || _data == null || !_data.DropZone)
            {
                return;
            }

            // On the plane's floor isn't down.
            _planeRide = _planeRide != null ? _planeRide : GetComponent<DroodPlaneRide>();
            var down = (_eva.IsGrounded || _eva.IsInWater) && (_planeRide == null || !_planeRide.InPlane);
            if (!_loadChecked)
            {
                // The groups' names and AutoCam's default once more, now the challenge's data is
                // in; the player's panel shown again (it may have read the names before they were
                // set).
                Drood2ActivationGroups.Ensure(_part);
                if (_eva.IsPlayerCraft)
                {
                    Drood2ActivationGroups.RefreshPanel();
                }

                // Loaded mid-jump: the rings show again until the landing.
                _loadChecked = true;
                _ringsShown = _hasTarget && _data.DropStarted && !down;
                _loadedMidJump = _ringsShown;
            }

            _sinceStart += Time.deltaTime;
            if (!_data.DropStarted && _sinceStart >= LaunchDelay)
            {
                // Once per launch: seated in a crew compartment it starts nothing.
                _data.DropStarted = true;
                if (_eva.EvaActive && _eva.IsGrounded && DroodChallenge.CanStartHere(craft, _part, "Drop Zone") && TakeTarget(craft))
                {
                    DroodLog.Debug("Drop zone: launched; the launch spot is the target.");
                    Teleport(pod, craft);
                }
            }

            DroodAutoCam.HandleKey(_eva);

            if (DroodChallenge.Ag2KeyPressed(_eva, CanJump() || CanRestart()))
            {
                pod.SetActivationGroupState(ActivationGroup, true);
            }

            if (pod.GetActivationGroupState(ActivationGroup))
            {
                pod.SetActivationGroupState(ActivationGroup, false);
                if (CanJump())
                {
                    if (DroodChallenge.CanStartHere(craft, _part, "Drop Zone"))
                    {
                        if (!_hasTarget && TakeTarget(craft))
                        {
                            DroodLog.Debug("Drop zone: the drood's spot is the target.");
                        }

                        if (_hasTarget)
                        {
                            Teleport(pod, craft);
                        }
                    }
                }
                else if (CanRestart())
                {
                    // In the air a second press within RestartConfirmSeconds restarts the jump
                    // (unscored); the first only asks.
                    if (Time.time - _restartAsked <= RestartConfirmSeconds)
                    {
                        _restartAsked = float.NegativeInfinity;
                        DroodLog.Debug("Drop zone: the jump restarted in the air.");
                        Teleport(pod, craft);
                    }
                    else
                    {
                        _restartAsked = Time.time;
                    }
                }
                else
                {
                    DroodLog.Debug("Drop zone: AG2 ignored (only on the ground, out of a crew compartment and time warp).");
                }
            }

            // In the arch after the teleport: Drood Chase, unless in first person (the view is added with the arch,
            // DroodCameraViews, so this waits until it is in the list), until it holds. Taken out
            // of the list during the jump (a restart from the arch: out of it at rest, then in
            // again from ~19 m/s), it is selected again with the next arch.
            if (_chaseSelected && !DroodChaseCameraMode.Drood.Exists)
            {
                _chaseSelected = false;
                _chaseFrames = 0;
                _chaseSelects = 0;
            }

            _freeFall = _freeFall != null ? _freeFall : GetComponent<DroodFreeFall>();
            if (_jumping && !_chaseSelected && _eva.IsPlayerCraft && _freeFall != null && _freeFall.Weight > 0f
                && (_eva.IsFpsActive || !DroodAutoCam.IsOn(_eva)))
            {
                // In first person, or with AutoCam off, the view stays.
                _chaseSelected = true;
                DroodLog.Debug($"Drop zone: in free fall {(_eva.IsFpsActive ? "in first person" : "with AutoCam off")}; the view stays.");
            }

            if (_jumping && !_chaseSelected && _eva.IsPlayerCraft && _freeFall != null && _freeFall.Weight > 0f)
            {
                _chaseFrames = DroodChaseCameraMode.Drood.Selected ? _chaseFrames + 1 : 0;
                if (_chaseFrames >= ChaseHeldFrames)
                {
                    _chaseSelected = true;
                    DroodLog.Debug($"Drop zone: in free fall; the view is Drood Chase (selected {_chaseSelects} times).");
                }
                else if (_chaseFrames == 0 && DroodChaseCameraMode.SelectDroodChase())
                {
                    _chaseSelects++;
                }
            }

            if (_keptView != null && _eva.IsPlayerCraft && _keptView.Exists)
            {
                _keptFrames = _keptView.Selected ? _keptFrames + 1 : 0;
                if (_keptFrames >= ChaseHeldFrames)
                {
                    _keptView = null;
                }
                else if (_keptFrames == 0)
                {
                    _keptView.Select();
                }
            }

            // Shown until RingsAfterLanding after a landing.
            if (!down)
            {
                _airborne += Time.deltaTime;
            }
            else if (_ringsShown && float.IsNegativeInfinity(_landedAt) && _airborne >= AirborneBeforeLanding)
            {
                _landedAt = Time.time;
            }

            if (_jumping && Teleported(craft))
            {
                EndWithoutScore("teleported during the jump");
            }

            // Down without a landing report (no open canopy, or in water): no score.
            if (_jumping && !float.IsNegativeInfinity(_landedAt) && Time.time - _landedAt > NoScoreAfter)
            {
                EndWithoutScore("a landing counts under an open canopy, on solid ground");
            }

            if (_ringsShown && !float.IsNegativeInfinity(_landedAt) && Time.time - _landedAt > RingsAfterLanding)
            {
                _ringsShown = false;
            }

            // The panel: the timer while jumping (asking for the second press while a restart waits
            // for it), the score while the target shows after it.
            if (_eva.IsPlayerCraft)
            {
                if (_jumping && float.IsNegativeInfinity(_landedAt))
                {
                    // In the plane the timer waits at 0:00 for the exit.
                    var asking = Time.time - _restartAsked <= RestartConfirmSeconds;
                    var inPlane = _planeRide != null && _planeRide.InPlane;
                    DroodDropChallengeHud.ShowTimer(inPlane ? 0f : Time.time - _exitTime, asking ? RestartPrompt : inPlane ? JumpPrompt : string.Empty);
                }
                else if (_loadedMidJump && Time.time - _restartAsked <= RestartConfirmSeconds)
                {
                    DroodDropChallengeHud.ShowTimer(0f, RestartPrompt);
                    _loadedPromptShown = true;
                }
                else if (_loadedPromptShown)
                {
                    _loadedPromptShown = false;
                    DroodDropChallengeHud.Hide();
                }
                else if (!_ringsShown && _hudShown)
                {
                    DroodDropChallengeHud.Hide();
                }

                _hudShown = _ringsShown;
            }
        }

        // The Course Camera's spot (DroodCourseCamera): CameraFromTarget east of the target, at
        // its ground level.
        private const float CameraFromTarget = 50f;

        /// <summary>The Course Camera's spot (world, the target's ground level) and up, while there is a target.</summary>
        internal bool CameraSpot(out Vector3 spot, out Vector3 up)
        {
            spot = up = Vector3.zero;
            var craft = _part?.CraftScript;
            if (_data == null || !_data.DropZone || !_hasTarget || craft == null
                || !(craft.CraftNode?.Parent is IPlanetNode planet) || planet.Name != _targetPlanet)
            {
                return false;
            }

            var frame = craft.ReferenceFrame;
            var centre = (Vector3)frame.PlanetToFramePosition(planet.SurfaceVectorToPlanetVector(_target));
            up = ((Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(_target.normalized))).normalized;
            var east = ((Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(Tangent(_target)))).normalized;
            spot = centre + east * CameraFromTarget;
            return true;
        }

        /// <summary>Late chain: the rings placed on the target as the frame is drawn.</summary>
        internal void PlaceRings()
        {
            var craft = _part?.CraftScript;
            var show = _ringsShown && _hasTarget && _eva.IsPlayerCraft && craft != null
                && craft.CraftNode?.Parent is IPlanetNode planet && planet.Name == _targetPlanet;
            if (!show)
            {
                _rings?.Hide();
                return;
            }

            if (_rings == null)
            {
                _rings = new DroodDropZoneRings(gameObject.layer, _part.BodyScript?.RigidBody);
            }

            var body = (IPlanetNode)craft.CraftNode.Parent;
            var frame = craft.ReferenceFrame;
            var up = body.SurfaceVectorToPlanetVector(_target.normalized);
            var east = body.SurfaceVectorToPlanetVector(Tangent(_target));
            var centre = frame.PlanetToFramePosition(body.SurfaceVectorToPlanetVector(_target));
            _rings.Place(centre, frame.PlanetToFrameVector(up).normalized, frame.PlanetToFrameVector(east).normalized);
        }

        private bool CanJump()
        {
            return DroodChallenge.Down(_eva) && (_planeRide == null || !_planeRide.InPlane);
        }

        /// <summary>In the air during a challenge jump, out of a crew compartment and time warp.</summary>
        private bool CanRestart()
        {
            var time = Assets.Scripts.Game.Instance?.FlightScene?.TimeManager?.CurrentMode;
            return (_jumping || _loadedMidJump) && _hasTarget && float.IsNegativeInfinity(_landedAt) && _eva.EvaActive
                && !_eva.IsGrounded && !_eva.IsInWater && (time == null || time.TimeMultiplier <= 1.0);
        }

        /// <summary>The ground under the drood becomes the target.</summary>
        private bool TakeTarget(ModApi.Craft.ICraftScript craft)
        {
            if (!(craft.CraftNode?.Parent is IPlanetNode planet))
            {
                return false;
            }

            var ground = transform.position;
            var hits = Physics.RaycastAll(transform.position + transform.up, -transform.up, 5f, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.distance < best && hit.collider.GetComponentInParent<EvaScript>() != _eva && hit.rigidbody != _part.BodyScript?.RigidBody)
                {
                    best = hit.distance;
                    ground = hit.point;
                }
            }

            _target = planet.PlanetVectorToSurfaceVector(craft.ReferenceFrame.FrameToPlanetPosition(ground));
            _targetPlanet = planet.Name;
            _hasTarget = true;
            _data.DropTarget = Format(_targetPlanet, _target);
            return true;
        }

        /// <summary>
        /// Up to ExitHeight above the target, ExitOffset to the side in a random direction, at
        /// rest over the ground; AG1 off (the canopy packs).
        /// </summary>
        private void Teleport(ICommandPod pod, ModApi.Craft.ICraftScript craft)
        {
            var node = craft.CraftNode;
            if (!(node?.Parent is IPlanetNode planet) || planet.Name != _targetPlanet)
            {
                DroodLog.Debug("Drop zone: the target is on another planet; no teleport.");
                return;
            }

            if (DroodChallenge.TooMuchAttached(craft, _part, "Drop Zone"))
            {
                return;
            }

            _keptView = DroodChaseCameraMode.SelectedMode;
            _keptFrames = 0;
            pod.SetActivationGroupState(DroodParachute.ActivationGroup, false);

            DroodChallenge.ReadyForJump(_eva, _landingFall);

            var up = _target.normalized;
            var east = Tangent(_target);
            var north = Vector3d.Cross(up, east);
            var angle = Random.Range(0f, 2f * Mathf.PI);
            var side = east * Mathf.Cos(angle) + north * Mathf.Sin(angle);
            var closest = _target + side * ExitOffset;

            // With the plane (DroodPlaneRide), from its cabin on a run past the target: it starts
            // ApproachSeconds of flight before the closest approach.
            _planeRide = _planeRide != null ? _planeRide : GetComponent<DroodPlaneRide>();
            var withPlane = _planeRide != null && DroodPlaneScenery.Ready;
            // The run's direction: the target on its left, where the door is (the plane's
            // left as drawn, Unity's: left of LookRotation(forward, up)).
            var along = Vector3d.Cross(up, side);
            var frame = craft.ReferenceFrame;
            var forwardInFrame = (Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(along));
            var upInFrame = (Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(up));
            var toTargetInFrame = (Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(-side));
            if (Vector3.Dot(toTargetInFrame, Vector3.Cross(upInFrame, forwardInFrame)) > 0f)
            {
                along = -along;
            }
            var start = withPlane ? closest - along * (DroodPlaneRide.PlaneSpeed * ApproachSeconds) : closest;
            var direction = start.normalized;
            var exit = direction * (_target.magnitude + ExitHeight);
            var position = planet.SurfaceVectorToPlanetVector(exit);
            var velocity = planet.SurfaceVectorToPlanetVector(planet.CalculateSurfaceVelocity(exit));
            var heading = Vector3d.zero;
            if (withPlane)
            {
                var toClosest = closest - start;
                heading = (toClosest - direction * Vector3d.Dot(toClosest, direction)).normalized;
                velocity += planet.SurfaceVectorToPlanetVector(heading * DroodPlaneRide.PlaneSpeed);
            }

            DroodChallenge.MoveTo(node, position, velocity);

            _ringsShown = true;
            _landedAt = float.NegativeInfinity;
            _airborne = 0f;
            _jumping = true;
            _loadedMidJump = false;
            _loadedPromptShown = false;
            _exitTime = Time.time;
            _hasLastSurface = false;
            _chaseSelected = false;
            _chaseFrames = 0;
            _chaseSelects = 0;
            if (heading != Vector3d.zero)
            {
                _planeRide.Begin(planet, exit, heading);
            }
            else
            {
                _planeRide?.End("a teleport without the plane");
            }

            DroodLog.Debug($"Drop zone: teleported {ExitHeight:0} m up, {(start - _target).magnitude:0} m from the target{(withPlane ? $", the plane passing it {ExitOffset:0} m to the side in {ApproachSeconds:0} s" : $" ({angle * Mathf.Rad2Deg:0}°)")}.");
        }

        /// <summary>Out of the plane's door: the jump's timer starts.</summary>
        internal void ExitedPlane()
        {
            if (_jumping)
            {
                _exitTime = Time.time;
            }
        }

        /// <summary>
        /// The landing after a teleport (from DroodParachute, right after its Flight Log line
        /// with <paramref name="grade"/>): the score on the next line (true: scored). Not after a quickload
        /// mid-jump (the exit time isn't saved).
        /// </summary>
        internal bool Landed(int grade)
        {
            if (!_jumping)
            {
                return false;
            }

            _jumping = false;
            var craft = _part?.CraftScript;
            if (craft == null || !_hasTarget || !(craft.CraftNode?.Parent is IPlanetNode planet) || planet.Name != _targetPlanet)
            {
                return false;
            }

            _leftAnkle = _leftAnkle != null ? _leftAnkle : DroodChallenge.FindBone(this, "LeftAnkle");
            _rightAnkle = _rightAnkle != null ? _rightAnkle : DroodChallenge.FindBone(this, "RightAnkle");
            var feet = _leftAnkle != null && _rightAnkle != null ? (_leftAnkle.position + _rightAnkle.position) * 0.5f : transform.position;
            var frame = craft.ReferenceFrame;
            var centre = frame.PlanetToFramePosition(planet.SurfaceVectorToPlanetVector(_target));
            var up = frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(_target.normalized)).normalized;
            var distance = Vector3.ProjectOnPlane(feet - centre, up).magnitude;
            var seconds = Time.time - _exitTime;

            var off = Mathf.Clamp01(distance / NearNone);
            var near = NearPoints * (1f - off * off);
            var soft = SoftPointsPerGrade * grade;
            var quick = QuickPoints * (1f - Mathf.InverseLerp(QuickFull, QuickNone, seconds))
                * Mathf.Sqrt(near / NearPoints * (soft / (SoftPointsPerGrade * 10f)));
            var total = Mathf.RoundToInt(near + soft + quick);
            var time = $"{(int)(seconds / 60f)}:{(int)(seconds % 60f):00}";
            var canopy = _data.SwoopWing ? "SwoopWing" : "RamAir";
            var previousBest = DroodDropChallengeBests.Best(canopy);
            var newBest = DroodDropChallengeBests.Record(canopy, total, distance, grade, seconds);
            var bestText = newBest
                ? (previousBest >= 0 ? $"New best! (was {previousBest})" : "New best!")
                : $"Best: {previousBest}";
            var text = $"<color={ScoreColour}>Drop Challenge Score: <b>{total}/1000</b>  (Distance {distance:0.00} m --- Landing grade {grade} --- Timer: {time}){(newBest ? " --- New best!" : string.Empty)}</color>";
            if (_eva.IsPlayerCraft)
            {
                DroodDropChallengeHud.ShowScore(total, distance, grade, seconds, bestText);
            }

            Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.FlightLog?.AddLog(text, ModApi.Flight.UI.FlightLogEntryCategory.Default, false, _part);
            DroodLog.Debug($"{text} (near {near:0}, soft {soft:0}, quick {quick:0}).");
            LastDistance = distance;
            LastScore = total;
            return true;
        }

        /// <summary>The last scored landing's distance from the target's centre (m) and score.</summary>
        internal float LastDistance { get; private set; }

        internal int LastScore { get; private set; }

        /// <summary>The drood moved further since the last frame than it could fly (see TeleportMinJump).</summary>
        private bool Teleported(ModApi.Craft.ICraftScript craft)
        {
            var node = craft.CraftNode;
            if (!(node?.Parent is IPlanetNode planet))
            {
                _hasLastSurface = false;
                return false;
            }

            var surface = planet.PlanetVectorToSurfaceVector(node.Position);
            var jumped = false;
            if (_hasLastSurface)
            {
                var moved = (surface - _lastSurface).magnitude;
                var flown = craft.SurfaceVelocity.magnitude * Time.deltaTime * TeleportSpeedShare;
                jumped = moved > System.Math.Max(TeleportMinJump, flown);
                if (jumped)
                {
                    DroodLog.Debug($"Drop zone: moved {moved:0} m in one frame (teleport).");
                }
            }

            _lastSurface = surface;
            _hasLastSurface = true;
            return jumped;
        }

        /// <summary>The jump ends without a score: a Flight Log line and the panel say why.</summary>
        private void EndWithoutScore(string reason)
        {
            _jumping = false;
            var text = $"<color={ScoreColour}>Drop Challenge: no score ({reason}).</color>";
            Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.FlightLog?.AddLog(text, ModApi.Flight.UI.FlightLogEntryCategory.Default, false, _part);
            if (_eva.IsPlayerCraft)
            {
                DroodDropChallengeHud.ShowNoScore(char.ToUpperInvariant(reason[0]) + reason.Substring(1) + ".");
            }
        }

        /// <summary>A unit vector square to <paramref name="point"/>'s up (any, but always the same).</summary>
        internal static Vector3d Tangent(Vector3d point)
        {
            var up = point.normalized;
            var reference = System.Math.Abs(up.y) < 0.9 ? new Vector3d(0, 1, 0) : new Vector3d(1, 0, 0);
            return Vector3d.Cross(reference, up).normalized;
        }

        // SAVE FORMAT: "planet;x;y;z" (surface coordinates, invariant culture).
        internal static string Format(string planet, Vector3d point) =>
            string.Join(";", planet, point.x.ToString("R", CultureInfo.InvariantCulture), point.y.ToString("R", CultureInfo.InvariantCulture), point.z.ToString("R", CultureInfo.InvariantCulture));

        internal static bool TryParse(string text, out string planet, out Vector3d point)
        {
            planet = null;
            point = default;
            var parts = string.IsNullOrEmpty(text) ? null : text.Split(';');
            if (parts == null || parts.Length != 4
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                return false;
            }

            planet = parts[0];
            point = new Vector3d(x, y, z);
            return true;
        }
    }
}
