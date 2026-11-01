using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The free-fall arch: belly to the relative wind, flown like a real skydiver.
    ///
    /// Physics (game-loop FixedUpdate, after EvaScript's; the game has no free-fall
    /// stabilization of its own: in the air EvaScript only turns the drood by the player's
    /// jetpack input, EvaScript.UpdateMovement):
    /// - the arch's drag acts at a centre of pressure behind the centre of mass (on the back
    ///   side), which turns the belly toward the airflow, but only weakly;
    /// - the limbs damp rotation, and turbulence pushes the body around;
    /// - an "autopilot" (the skydiver) aims the belly at the relative wind with limb
    ///   deflections: pitch with the legs and arms, roll with more drag on the side that must
    ///   come up (arm wider, leg extended, hand flat), yaw by turning both hands. Their force is
    ///   aerodynamic (limited, and scaled by the dynamic
    ///   pressure), with a reaction delay and some noise, so the body settles but never sits
    ///   perfectly still. The pose shows these commands (<see cref="DroodFreeFallPose"/>);
    /// - the air pushes on the body like on a plate, along the belly's normal, so the glide
    ///   (track, backslide, side slide) comes from the body's attitude; the game's own body
    ///   drag is counted once.
    ///
    /// With the corrections the arch settles in 1-4 s (from a plane exit, a jump from rest or a
    /// tumble) and then wobbles a few degrees, the limbs using ~30% of their range; terminal
    /// speed ~56 m/s. Without them the body drifts and never settles (offline simulation).
    ///
    /// Only in air (dynamic pressure), falling, not too low (time to impact), before the
    /// canopy's line stretch; it doesn't start while the player is flying the jetpack. In the
    /// arch the jetpack is off (DroodParachute.UpdateJetpack) and the EVA keys steer the body.
    /// </summary>
    public class DroodFreeFall : MonoBehaviour
    {
        // When: falling at least MinAirborneSeconds (counted from leaving the ground, and again
        // when the chute stops holding the drood, e.g. stowed), dynamic pressure above
        // EnterPressure (Pa; ~10 m/s at sea level; leaves below ExitPressure), at least
        // EnterImpactSeconds to impact (leaves below ExitImpactSeconds) and above MinHeight (m).
        private const float MinAirborneSeconds = 2f;
        private const float EnterPressure = 60f;
        private const float ExitPressure = 30f;
        private const float EnterImpactSeconds = 3f;
        private const float ExitImpactSeconds = 2f;
        private const float MinHeight = 30f;

        // Into and out of the arch over this long (s).
        private const float BlendSeconds = 0.4f;

        // The arch's plate drag (BellyDragArea, EdgeDragArea) taken this many times: at 1 the
        // drood fell faster than a belly-to-earth skydiver (68 m/s) and kept a plane's speed too
        // long (16 m/s level 9 s after a 40 m/s exit).
        private const float ArchDragScale = 1.4f;

        // The body (a ~115 kg, 1.8 m jumper): moments of inertia (kg m²) about its right axis
        // (pitch), its spine (roll) and its belly axis (yaw), scaled with the drood's mass.
        private static readonly Vector3 Inertia = new Vector3(31f, 3.3f, 34f);
        private const float ReferenceMass = 115f;

        // Arch aerodynamics: drag area (m²) belly-on and edge-on; the centre of pressure this far
        // (m) behind the centre of mass; the limbs' rate damping (sum of C·A·r², m⁴); turbulence
        // (N·m at 1,800 Pa, ~55 m/s at sea level, weaker below and no stronger above: the
        // skydiver's corrections don't grow with speed, so stronger gusts would rock a fast
        // track).
        private const float BellyDragArea = 0.6f;
        private const float EdgeDragArea = 0.15f;

        // A tumbling body's drag area (m²) outside the arch: the belly-on and edge-on ones averaged.
        private const float TumbleDragArea = (BellyDragArea + EdgeDragArea) / 2f;
        private const float PressureCentreBehind = 0.08f;
        private const float LimbDamping = 0.3f;
        private const float Turbulence = 12f;

        // Gusts push less about the spine (roll, bank): the body is narrow there, so their lever
        // arms are short, and it is ~10x easier to roll than to pitch or turn. A third of the push
        // keeps the bank at ~2° typical (offline simulation).
        private static readonly Vector3 TurbulenceAxes = new Vector3(1f, 0.33f, 1f);
        private const float TurbulenceReferencePressure = 1800f;
        // ... and in a track the gusts' yaw (about the belly) is TrackYawTurbulence of it and
        // their roll (about the spine) TrackRollTurbulence: the heading wandered left and right,
        // turned directly and through the bank (banked, a track curves its path).
        private const float TrackYawTurbulence = 0f;
        private const float TrackRollTurbulence = 0.2f;

        // The skydiver: aims the belly at the relative wind, as the steering turns it (proportional
        // gain, 1/s², and rate damping, 1/s), reacting within ReactionSeconds, with Noise
        // (fraction) in each correction. The limbs' authority per axis (pitch, roll, yaw; m³: area
        // x lever x force coefficient at full deflection) limits what they can do.
        private const float AimGain = 4f;
        private const float RateDamping = 2f;
        private const float ReactionSeconds = 0.2f;
        private const float Noise = 0.4f;
        private static readonly Vector3 LimbAuthority = new Vector3(0.048f, 0.024f, 0.04f);

        // Steering by body position (the player's EVA keys, the jetpack is off in the arch):
        // - turn keys: a turn about the belly axis at up to MaxTurnRate (rad/s), reached with
        //   TurnResponse (1/s) within the limbs' authority;
        // - forward / back: the autopilot aims the belly at the relative wind turned head-down by
        //   up to TrackPitch (a track; arms back, legs straight) or head-up by up to
        //   BackslidePitch (a backslide) (degrees). The glide comes from that attitude: the air
        //   pushes along the belly's normal, which tilts with the body (steady glide a little
        //   under tan of the angle: ~0.75:1 in a full track, the body about level to the
        //   horizon);
        // - strafe: the same about the spine, tilted by up to SideTilt (a side slide);
        // - in a track the turn keys bank instead, like an aeroplane: the roll is held against
        //   the horizon, level or banked by up to TrackBank (turn-side shoulder down), so the
        //   glide force pushes into the turn and the path curves; the heading follows the path
        //   (CoordinationGain, 1/s per radian of sideslip: the head turned toward the glide
        //   direction), a coordinated turn with no turn of its own, fading in with the glide
        //   across the belly (none below CoordinationFromGlide, all from CoordinationFullGlide, as
        //   the sine of its angle: flat, its direction is noise). Between the arch and a full
        //   track the two blend.
        // The keys ease in and out over InputSeconds, forward/back (track, backslide: the body
        // and limbs change their whole shape) over TrackInputSeconds.
        private const float MaxTurnRate = 1.6f;
        private const float TurnResponse = 2f;
        private const float TrackPitch = 40f;
        private const float BackslidePitch = 10f;
        private const float SideTilt = 10f;
        private const float TrackBank = 30f;
        private const float CoordinationGain = 2f;
        private const float CoordinationFromGlide = 0.05f;
        private const float CoordinationFullGlide = 0.25f;
        private const float InputSeconds = 0.3f;
        private const float TrackInputSeconds = 0.6f;
        private float _track;
        private float _side;
        private float _turn;

        private EvaScript _eva;
        private DroodParachute _parachute;
        private DroodPlaneRide _planeRide;
        private DroodLimbFollower _limbs;
        private Drood2Script _drood;
        private float _airborneSince = float.NegativeInfinity;
        private bool _stepped;
        private bool _chuteAllowed = true;
        private bool _active;
        private float _activeSince = float.PositiveInfinity;
        private float _weight;
        private Vector3 _command;
        private Vector3 _commandFraction;
        private float _noiseSeed;

        /// <summary>
        /// Before the first physics step (e.g. right after a load): the arch may still resume,
        /// so the parachute doesn't hand the jetpack back yet (<see cref="DroodParachute"/>'s
        /// UpdateJetpack runs before this component's step).
        /// </summary>
        internal bool Resuming => !_stepped;

        /// <summary>How far into the arch (0..1): the pose and the physics blend by it.</summary>
        public float Weight => _weight;

        /// <summary>The arch is on (the pose may still be blending).</summary>
        public bool Active => _active;

        /// <summary>How long the arch has been on (s; 0 while it's off).</summary>
        public float ActiveSeconds => _active ? Mathf.Max(0f, Time.time - _activeSince) : 0f;

        /// <summary>
        /// The skydiver's corrections as fractions of the limbs' reach (-1..1): x pitch (legs
        /// and arms along the body), y roll (the side that must come up: arm wider, leg extended,
        /// hand flat), z yaw (both hands turned).
        /// </summary>
        public Vector3 LimbCommand => _commandFraction;

        /// <summary>The track (1, forward) or backslide (-1) the drood is flying, eased.</summary>
        public float Track => _track;

        /// <summary>
        /// In a track, the roll still to make toward the wanted bank (-1 left .. 1 right, a full
        /// TrackBank's worth): the arms show it like ailerons, out while the bank builds, back
        /// once it's reached, out the other way rolling level again.
        /// </summary>
        public float RollDemand => _rollDemand;

        private float _rollDemand;

        /// <summary>The turn the drood is flying (-1 left .. 1 right), eased.</summary>
        public float Turn => _turn;

        /// <summary>In the arch in first person: the mouse looks around (FreeLookUnderCanopyPatch).</summary>
        public bool FreeLook => _active && _eva != null && _eva.IsFpsActive;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _parachute = GetComponent<DroodParachute>();
            _noiseSeed = Random.value * 100f;
        }

        /// <summary>Game-loop FixedUpdate, after EvaScript's.</summary>
        internal void PhysicsStep()
        {
            var dt = Time.fixedDeltaTime;
            var part = _eva != null ? _eva.PartScript : null;
            var craft = part?.CraftScript;
            var body = part?.BodyScript?.RigidBody;
            if (craft == null || body == null || dt <= 0f)
            {
                return;
            }

            var grounded = _eva.IsGrounded || _eva.IsInWater || !_eva.EvaActive;
            if (grounded)
            {
                _airborneSince = float.NegativeInfinity;
            }
            else if (float.IsNegativeInfinity(_airborneSince))
            {
                _airborneSince = Time.time;
            }

            // Loaded in the arch (the jetpack still switched off for it, saved): no new
            // MinAirborneSeconds wait, otherwise the jetpack would come back on and the drood
            // tumble.
            if (!_stepped)
            {
                _stepped = true;
                if (!grounded && _parachute != null && _parachute.JetpackSuspended && _parachute.AllowsFreeFall)
                {
                    _airborneSince = Time.time - MinAirborneSeconds;
                }
            }

            var density = (float)craft.AtmosphereSample.AirDensity;
            var velocity = (Vector3)craft.SurfaceVelocity;
            var speed = velocity.magnitude;
            var pressure = 0.5f * density * speed * speed;
            var up = -(Vector3)craft.FlightData.GravityFrameNormalized;
            var sink = -Vector3.Dot(velocity, up);
            var height = (float)craft.FlightData.AltitudeAboveGroundLevel;
            var impactSeconds = sink > 0.5f ? height / sink : float.PositiveInfinity;

            // The fall (and its MinAirborneSeconds) starts again when the chute stops holding the
            // drood, e.g. stowed in the air (AG1 off).
            var chuteAllows = _parachute == null || _parachute.AllowsFreeFall;
            if (chuteAllows && !_chuteAllowed && !grounded)
            {
                _airborneSince = Time.time;
            }

            _chuteAllowed = chuteAllows;
            _planeRide = _planeRide != null ? _planeRide : GetComponent<DroodPlaneRide>();
            // Anchored to what it is attached to (Attached Parts), the drood flies with that
            // craft: its body is the craft's, which the arch would steer.
            _limbs = _limbs != null ? _limbs : GetComponent<DroodLimbFollower>();
            _drood = _drood != null ? _drood : GetComponent<Drood2Script>();
            var allowed = !grounded && chuteAllows && !PlayerFlying() && (_planeRide == null || !_planeRide.InPlane) && (_limbs == null || !_limbs.Anchored)
                && (_drood == null || !_drood.Releasing);
            var wasActive = _active;
            _active = allowed && height > MinHeight && (_active
                ? pressure >= ExitPressure && impactSeconds >= ExitImpactSeconds
                : pressure >= EnterPressure && impactSeconds >= EnterImpactSeconds && Time.time - _airborneSince >= MinAirborneSeconds);
            if (_active != wasActive)
            {
                _activeSince = _active ? Time.time : float.PositiveInfinity;
                DroodLog.Debug(_active
                    ? $"Free fall: arching ({speed:0} m/s, {pressure:0} Pa, {impactSeconds:0} s to impact; releasing {(_drood != null && _drood.Releasing)}, anchored {(_limbs != null && _limbs.Anchored)})."
                    : "Free fall: out of the arch.");
            }

            _weight = Mathf.MoveTowards(_weight, _active ? 1f : 0f, dt / BlendSeconds);

            // Falling but not (fully) in the arch, e.g. in the wait after a release or while the
            // arch eases in: the game gives a drood almost no air drag, so a tumbling body's (the
            // arch's plate drag averaged over belly-on and edge-on), as much as the arch isn't on.
            var falling = !grounded && chuteAllows && !PlayerFlying() && (_planeRide == null || !_planeRide.InPlane)
                && (_limbs == null || !_limbs.Anchored);
            if (falling && _weight < 1f && speed > 0.1f)
            {
                body.AddForce(-velocity / speed * (pressure * TumbleDragArea * ArchDragScale * ModApi.Constants.MassScale * (1f - _weight)), ForceMode.Force);
            }

            ReadSteering(dt);
            if (_weight <= 0f || speed < 0.1f)
            {
                _command = Vector3.zero;
                _commandFraction = Vector3.zero;
                return;
            }

            Fly(body, velocity / speed, up, speed, pressure, density, dt);
            LogArch(speed);
        }

        // Debug Logging, with each arch line: settings a seat (EvaChairScript, cockpit) changes
        // and leaves behind (the IK head mapping's maintained rotation, the Animator's culling)
        // and the body's physics, to compare the arch after a seat with the arch without one.
        private RootMotion.FinalIK.FullBodyBipedIK _ik;
        private Animator _archAnimator;

        private void LogSetup()
        {
            _ik = _ik != null ? _ik : GetComponentInChildren<RootMotion.FinalIK.FullBodyBipedIK>(true);
            _archAnimator = _archAnimator != null ? _archAnimator : GetComponentInChildren<Animator>(true);
            var mappings = _ik != null ? _ik.solver.boneMappings : null;
            var head = mappings != null && mappings.Length > 0 ? mappings[0].maintainRotationWeight : -1f;
            var body = _eva.PartScript?.BodyScript?.RigidBody;
            DroodLog.Debug($"Arch setup: IK head maintains rotation {head:0.00} ({mappings?.Length ?? 0} mappings), " +
                $"animator culling {_archAnimator?.cullingMode}, first person {_eva.IsFpsActive}, " +
                (body != null
                    ? $"body mass {body.mass:0.0}, inertia {body.inertiaTensor}, inertia rotation {body.inertiaTensorRotation.eulerAngles}, centre of mass {body.centerOfMass}, angular drag {body.angularDrag:0.00}, drag {body.drag:0.00}, max angular {body.maxAngularVelocity:0.0}, constraints {body.constraints}, interpolation {body.interpolation}."
                    : "no body."));
        }

        // Debug Logging: the arch's attitude, rates, steering and corrections once a second.
        private float _nextArchLog;
        private Vector3 _lastError;
        private Vector3 _lastRate;
        private float _lastHorizonBank;

        private void LogArch(float speed)
        {
            if (!_active || !DroodLog.DebugEnabled || Time.time < _nextArchLog)
            {
                return;
            }

            _nextArchLog = Time.time + 1f;
            LogSetup();
            DroodLog.Debug($"Arch: {speed:0.0} m/s, bank {_lastError.y * Mathf.Rad2Deg:0.0}°, pitch {_lastError.x * Mathf.Rad2Deg:0.0}° off the aim, " +
                $"bank to the horizon {_lastHorizonBank * Mathf.Rad2Deg:0.0}° (+ right side up), " +
                $"rates pitch {_lastRate.x * Mathf.Rad2Deg:0}°/s roll {_lastRate.y * Mathf.Rad2Deg:0}°/s yaw {_lastRate.z * Mathf.Rad2Deg:0}°/s, " +
                $"turn {_turn:0.00} track {_track:0.00} side {_side:0.00}, corrections pitch {_commandFraction.x:0.00} roll {_commandFraction.y:0.00} yaw {_commandFraction.z:0.00}.");
        }

        /// <summary>
        /// The player's EVA keys (and mouse steering), eased: turn, forward/back (track,
        /// backslide), strafe (side slide). Only the player-controlled drood, only in the arch.
        /// </summary>
        private void ReadSteering(float dt)
        {
            var controls = _eva.PartScript?.CommandPod?.Controls;
            var steer = _active && _eva.IsPlayerCraft && controls != null;
            var step = dt / InputSeconds;
            // Mouse steering (DroodMouseSteering): left/right turns, up tracks, down backslides.
            var mouse = steer && DroodMouseSteering.IsOn(_eva) ? DroodMouseSteering.Stick() : Vector2.zero;
            var turn = steer ? DroodMouseSteering.Combine(controls.EvaTurn, mouse.x) : 0f;
            var track = steer ? DroodMouseSteering.Combine(controls.EvaMoveFwdAft, mouse.y) : 0f;
            _turn = Mathf.MoveTowards(_turn, Mathf.Clamp(turn, -1f, 1f), step);
            _track = Mathf.MoveTowards(_track, Mathf.Clamp(track, -1f, 1f), dt / TrackInputSeconds);
            _side = Mathf.MoveTowards(_side, steer ? Mathf.Clamp(controls.EvaStrafe, -1f, 1f) : 0f, step);
        }

        /// <summary>The player is flying the jetpack (any EVA movement or rotation input).</summary>
        private bool PlayerFlying()
        {
            var controls = _eva.PartScript?.CommandPod?.Controls;
            if (controls == null || !_eva.JetpackEnabled)
            {
                return false;
            }

            return Mathf.Abs(controls.EvaMoveFwdAft) > 0.05f || Mathf.Abs(controls.EvaStrafe) > 0.05f
                || Mathf.Abs(controls.EvaMoveUpDown) > 0.05f || Mathf.Abs(controls.EvaPitch) > 0.05f
                || Mathf.Abs(controls.EvaRoll) > 0.05f || Mathf.Abs(controls.EvaTurn) > 0.05f;
        }

        /// <summary>The arch's torques, the skydiver's corrections and the plate force (see the class comment).</summary>
        private void Fly(Rigidbody body, Vector3 motion, Vector3 up, float speed, float pressure, float density, float dt)
        {
            var massScale = ModApi.Constants.MassScale;
            var inertia = Inertia * Mathf.Max(body.mass / massScale / ReferenceMass, 0.2f);
            var right = transform.right;
            var head = transform.up;
            var belly = transform.forward;
            var cos = Vector3.Dot(belly, motion);
            var tracking = Mathf.Max(_track, 0f);
            var backsliding = Mathf.Max(-_track, 0f);

            // The body as a plate: the air pushes along the belly's normal (toward the back),
            // growing with how squarely it faces the air (a plate's normal force goes about with
            // the cosine), plus drag against the motion as far as it doesn't (edge-on). Never
            // along the flight path (no thrust). Full track (40°): ~0.75:1, ~37 m/s forward and
            // ~49 m/s down; belly flat ~56 m/s.
            var facing = Mathf.Max(cos, 0f);
            var aero = (-belly * (pressure * BellyDragArea * facing) - motion * (pressure * EdgeDragArea * (1f - facing))) * ArchDragScale;
            var alongPath = Vector3.Dot(aero, motion);
            if (alongPath > 0f)
            {
                aero -= motion * alongPath;
            }

            // Body-frame angular velocity (x right/pitch, y head/roll, z belly/yaw).
            var spin = body.angularVelocity;
            var rate = new Vector3(Vector3.Dot(spin, right), Vector3.Dot(spin, head), Vector3.Dot(spin, belly));

            // The arch's drag at the centre of pressure behind the centre of mass turns the belly
            // toward the airflow; the limbs damp rotation; turbulence.
            var archTorque = Vector3.Cross(-belly * PressureCentreBehind, aero);
            var torque = new Vector3(Vector3.Dot(archTorque, right), Vector3.Dot(archTorque, head), Vector3.Dot(archTorque, belly));
            torque -= 0.5f * density * speed * LimbDamping * rate;
            var t = Time.time;
            torque += Turbulence * Mathf.Min(pressure / TurbulenceReferencePressure, 1f) * Vector3.Scale(TurbulenceAxes, new Vector3(
                Mathf.PerlinNoise(t * 0.6f, _noiseSeed) * 2f - 1f,
                (Mathf.PerlinNoise(t * 0.7f, _noiseSeed + 30f) * 2f - 1f) * Mathf.Lerp(1f, TrackRollTurbulence, tracking),
                (Mathf.PerlinNoise(t * 0.5f, _noiseSeed + 60f) * 2f - 1f) * Mathf.Lerp(1f, TrackYawTurbulence, tracking)));

            // The skydiver: belly at the relative wind, turned head-down (track) or head-up
            // (backslide) and tilted about the spine (side slide) as the player asks, turning at
            // the rate the player asks for about the belly axis (in a track: banked, the heading
            // following the path), within the limbs' authority,
            // after a reaction delay. A positive angle about the right axis turns the belly
            // toward the feet (the head goes down); a negative one about the spine turns it to
            // the left, so the air pushes the body to the right.
            var pitch = TrackPitch * tracking - BackslidePitch * backsliding;
            var bank = SideTilt * _side;
            var aim = Quaternion.AngleAxis(-bank, head) * (Quaternion.AngleAxis(pitch, right) * motion);
            var errorWorld = Vector3.Cross(belly, aim);
            var angle = Mathf.Atan2(errorWorld.magnitude, Vector3.Dot(belly, aim));
            var error = new Vector3(Vector3.Dot(errorWorld, right), Vector3.Dot(errorWorld, head), 0f);
            error = error.sqrMagnitude > 1e-8f ? error.normalized * angle : Vector3.zero;
            // In a track the roll is held against the horizon instead, like an aeroplane's: wings
            // level, or banked by the turn keys (turn-side shoulder down), so the glide force
            // pushes into the turn and the path curves. (Against the relative wind alone, any lean
            // would curve the path, the wind and the heading with it: a spiral.)
            var horizonBank = Mathf.Asin(Mathf.Clamp(Vector3.Dot(right, up), -1f, 1f));
            var levelError = -TrackBank * Mathf.Deg2Rad * _turn - horizonBank;
            _rollDemand = Mathf.Clamp(-levelError / (TrackBank * Mathf.Deg2Rad), -1f, 1f) * tracking;
            error.y = Mathf.Lerp(error.y, levelError, tracking);
            _lastHorizonBank = horizonBank;
            _lastError = error;
            _lastRate = rate;
            // A right turn (belly down, head forward) is a negative rate about the belly axis. In a
            // track the heading follows the glide (the motion across the belly axis) instead.
            // Only with a real glide across the belly: flat (the track just starting), its
            // direction is noise, and following it would turn the body anywhere.
            var glide = Vector3.ProjectOnPlane(motion, belly);
            var sideslip = glide.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(head, glide, belly) * Mathf.Deg2Rad : 0f;
            var glideShare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CoordinationFromGlide, CoordinationFullGlide, glide.magnitude));
            var keyTurn = -_turn * MaxTurnRate;
            var turnRate = Mathf.Lerp(keyTurn, Mathf.Lerp(keyTurn, CoordinationGain * sideslip, glideShare), tracking);
            var want = Vector3.Scale(inertia, new Vector3(
                AimGain * error.x - RateDamping * rate.x,
                AimGain * error.y - RateDamping * rate.y,
                TurnResponse * (turnRate - rate.z)));
            want = Vector3.Scale(want, Vector3.one + Noise * 0.3f * new Vector3(
                Mathf.PerlinNoise(t * 3f, _noiseSeed + 90f) * 2f - 1f,
                Mathf.PerlinNoise(t * 3f, _noiseSeed + 120f) * 2f - 1f,
                Mathf.PerlinNoise(t * 3f, _noiseSeed + 150f) * 2f - 1f));
            var reach = LimbAuthority * pressure;
            want = new Vector3(
                Mathf.Clamp(want.x, -reach.x, reach.x),
                Mathf.Clamp(want.y, -reach.y, reach.y),
                Mathf.Clamp(want.z, -reach.z, reach.z));
            _command = Vector3.Lerp(_command, want, 1f - Mathf.Exp(-dt / ReactionSeconds));
            _commandFraction = new Vector3(
                reach.x > 1e-4f ? _command.x / reach.x : 0f,
                reach.y > 1e-4f ? _command.y / reach.y : 0f,
                reach.z > 1e-4f ? _command.z / reach.z : 0f);
            torque += _command;

            // As an angular acceleration (the drood's rigidbody inertia isn't a human's).
            var accel = new Vector3(torque.x / inertia.x, torque.y / inertia.y, torque.z / inertia.z) * _weight;
            body.AddTorque(right * accel.x + head * accel.y + belly * accel.z, ForceMode.Acceleration);

            // The plate force, less what the game already applies to the body
            // (IBodyScript.DragForce, counted once), never pushing along the flight path.
            var gameDrag = _eva.PartScript.BodyScript.DragForce / massScale;
            var added = aero - gameDrag;
            var addedAlong = Vector3.Dot(added, motion);
            if (addedAlong > 0f)
            {
                added -= motion * addedAlong;
            }

            body.AddForce(added * (massScale * _weight), ForceMode.Force);
        }
    }
}
