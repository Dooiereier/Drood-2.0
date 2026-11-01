using UnityEngine;

namespace Drood2
{
    // Physics: the parachute state machine per physics step, aerodynamic forces, hanging
    // attitude and steering torque, landing detection and the status log.
    public partial class DroodParachute
    {
        /// <summary>Physics step: called from the modifier's game-loop FixedUpdate, after EvaScript's.</summary>
        internal void PhysicsStep()
        {
            var commandPod = _part?.CommandPod;
            var craft = _part?.CraftScript;
            var body = _part?.BodyScript?.RigidBody;
            if (commandPod == null || craft == null || body == null)
            {
                LogOnce(ref _loggedMissingReferences, $"Parachute can't run: command pod {Found(commandPod)}, craft {Found(craft)}, rigidbody {Found(body)}.");
                return;
            }

            LogOnce(ref _loggedPhysicsRunning, "Parachute physics update is running.");
            _stepDirection = _canopyDirection;
            _stepForward = _canopyForward;
            _stepPitch = _attitudePitch;
            _stepRoot = body.position;
            if (!_restored)
            {
                RestoreState();
            }

            Step(commandPod, craft, body);
            UpdateJetpack();
            SaveState();
        }

        private void Step(ModApi.Craft.Parts.ICommandPod commandPod, ModApi.Craft.ICraftScript craft, Rigidbody body)
        {
            // Leaving a crew seat (EVA button): the drood carries the craft's AG1 state
            // along, which would deploy the chute at once. Start every exit with AG1 off. Only
            // after the drood was seen seated, so a save loaded mid-jump keeps its chute.
            var evaActive = _eva.EvaActive;
            if (!evaActive)
            {
                _seenSeated = true;
            }
            else if (!_wasEvaActive && _seenSeated && commandPod.GetActivationGroupState(ActivationGroup))
            {
                commandPod.SetActivationGroupState(ActivationGroup, false);
                Log($"Left the crew seat with AG{ActivationGroup} on; switched it off so the parachute doesn't deploy.");
            }

            _wasEvaActive = evaActive;

            // The round canopy: deployed automatically after RoundAutoDeploySeconds in the arch,
            // once per fall: packed again in the air (AG1 off), it stays packed until the drood
            // is down again or the drop zone takes it up.
            if (_eva.IsGrounded || _eva.IsInWater)
            {
                _autoDeployed = false;
            }
            else if (_groundIgnored)
            {
                _groundIgnored = false;
                Log("Open start: off the ground.");
            }

            _freeFallState = _freeFallState != null ? _freeFallState : GetComponent<DroodFreeFall>();
            if (!IsRamAir && evaActive && !_autoDeployed && _state == State.Packed && _freeFallState != null
                && _freeFallState.ActiveSeconds >= RoundAutoDeploySeconds && !commandPod.GetActivationGroupState(ActivationGroup))
            {
                commandPod.SetActivationGroupState(ActivationGroup, true);
                _autoDeployed = true;
                Log($"Round canopy: deployed automatically after {RoundAutoDeploySeconds:0} s of free fall.");
            }

            // Anchored to something larger (Attached Parts), not seated: no parachute; AG1 is switched
            // back off.
            _limbs = _limbs != null ? _limbs : GetComponent<DroodLimbFollower>();
            if (_eva.EvaActive && _limbs != null && _limbs.Anchored && commandPod.GetActivationGroupState(ActivationGroup))
            {
                commandPod.SetActivationGroupState(ActivationGroup, false);
                Log($"AG{ActivationGroup} switched off: the drood is anchored, no parachute.");
            }

            var activationGroupOn = commandPod.GetActivationGroupState(ActivationGroup);
            if (activationGroupOn != _lastActivationGroupOn)
            {
                _lastActivationGroupOn = activationGroupOn;
                _loggedSeated = false;
                Log($"AG{ActivationGroup} {(activationGroupOn ? "on" : "off")}.");
            }

            // Inside a pod or chair, or AG1 off: packed.
            if (!_eva.EvaActive || !activationGroupOn)
            {
                if (activationGroupOn)
                {
                    LogOnce(ref _loggedSeated, "AG1 is on, but the drood is seated in a crew compartment. The parachute stays packed.");
                }

                if (_state != State.Packed)
                {
                    Repack();
                }

                return;
            }

            HoldOpenStart(craft);
            _altitudeAboveGround = HeightAboveGround(craft);
            _feetHeight = FeetHeight(craft);
            PathToGround(craft);
            var density = craft.AtmosphereSample.AirDensity;
            var airVelocity = craft.SurfaceVelocity;
            // The gravity the canopy feels: gravity minus the drood's own acceleration (smoothed;
            // capped, so a teleport's jump in velocity doesn't count). ~1 g in steady flight,
            // ~0 in free fall or pushing over.
            var step = Time.fixedDeltaTime;
            if (_hasVisualAirVelocity && step > 0f)
            {
                var acceleration = Vector3.ClampMagnitude(((Vector3)airVelocity - _visualAirVelocity) / step, 30f);
                _visualAcceleration = Vector3.Lerp(_visualAcceleration, acceleration, 1f - Mathf.Exp(-step / ApparentGravitySmoothing));
            }

            _hasVisualAirVelocity = true;
            _visualAirVelocity = airVelocity;
            _visualDensity = (float)density;
            _visualDown = craft.FlightData.GravityFrameNormalized;
            _visualGravity = (Vector3)craft.FlightData.GravityFrameNormalized * (float)craft.FlightData.GravityMagnitude;
            var airspeed = airVelocity.magnitude;

            if (_state == State.Packed)
            {
                _state = State.Armed;
                _nextArmedLogTime = 0f;
                Log("Parachute armed.");
            }

            if (_state == State.Armed)
            {
                var altitude = craft.FlightData.AltitudeAboveSeaLevel;
                if (altitude > MaxDeploymentAltitude || airspeed > MaxDeploymentSpeed)
                {
                    if (Time.time >= _nextArmedLogTime)
                    {
                        _nextArmedLogTime = Time.time + ArmedLogInterval;
                        var reason = altitude > MaxDeploymentAltitude
                            ? $"altitude {altitude:0} m > {MaxDeploymentAltitude:0} m"
                            : $"speed {airspeed:0} > {MaxDeploymentSpeed:0} m/s";
                        Log($"Armed, waiting: {reason} (altitude {craft.FlightData.AltitudeAboveSeaLevel:0} m ASL).");
                    }

                    return;
                }

                Deploy(airVelocity, airspeed);
            }

            var dt = Time.fixedDeltaTime;
            // Ram-air: first the right hand reaches back for the pilot chute and throws it out to
            // the side (HandThrowSeconds, DroodPilotThrow), then its bridle pays out
            // (PilotThrowSeconds), then it pulls the bag out and the lines extend.
            if (_handThrow < 1f)
            {
                _handThrow = Mathf.Clamp01(_handThrow + dt / HandThrowSeconds);
            }
            else if (_pilotOut < 1f)
            {
                _pilotOut = Mathf.Clamp01(_pilotOut + dt / PilotThrowSeconds);
            }
            else
            {
                _extend = Mathf.Clamp01(_extend + dt / LineExtendSeconds);
            }

            if (!_inflating && _extend >= 1f)
            {
                // Line stretch: the canopy starts to open (restored saves set _inflating directly),
                // heading where the drood's head points now (it may have turned since the throw).
                _inflating = true;
                _canopyForward = HeadingFromBody(_canopyDirection);
                _yawRate = 0f;
                var stretchForward = Vector3.Dot((Vector3)airVelocity, Vector3.ProjectOnPlane(_canopyForward, -_visualDown).normalized);
                _openingPitchShare = IsRamAir
                    ? OpeningPitchShare * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(OpeningPitchFromForward, OpeningPitchFullForward, stretchForward))
                    : 0f;
                Log($"Line stretch at {stretchForward:0.0} m/s forward: opening pitch share {_openingPitchShare:0.00}.");
                RollClosure();
                _deploySound = _deploySound != null ? _deploySound : GetComponent<DroodDeploySound>();
                _deploySound?.PlayOpening();
            }
            if (_inflating)
            {
                // The canopy opens in a fixed time: InflateSeconds, the ram-air
                // RamAirInflateSeconds, slowed by its slider while the drood is fast (a time from
                // the dynamic pressure would make fast openings quicker and harder). It can't fill
                // without airflow (fading out below StarvedPressure).
                var rate = IsRamAir
                    ? Mathf.Lerp(RamAirSliderRate, 1f, Mathf.InverseLerp(RamAirSliderSpeed, RamAirSliderFullSpeed, airspeed))
                    : 1f;
                rate *= Mathf.InverseLerp(StarvedPressure * 0.5f, StarvedPressure, DynamicPressure(density, airspeed));
                _inflate = _starved
                    ? Mathf.MoveTowards(_inflate, 0f, dt / StarvedDeflateSeconds)
                    : Mathf.Clamp01(_inflate + rate * dt / (IsRamAir ? RamAirInflateSeconds : InflateSeconds));
            }

            DetectLandingFall(airVelocity);

            // The canopy deflates once the jumper's weight is off it, on ground or water
            // contact, so it stops pulling (and dragging the drood) during the landing fall.
            if (TouchedDown)
            {
                _lastGroundedTime = Time.time;
                _landedInWater = _eva.IsInWater;
            }

            var landed = Time.time - _lastGroundedTime < LandedGraceSeconds;
            UpdateStarved(landed, DynamicPressure(density, airspeed));

            // A bounce after a landing clip began (BounceHoldSeconds): the canopy stays down and
            // the clip plays on; only after that does it reopen in the air.
            _landingFall = _landingFall != null ? _landingFall : GetComponent<DroodLandingFall>();
            var bouncing = _landingFall != null && _landingFall.Active && _landingFall.Elapsed < BounceHoldSeconds;
            if (landed || bouncing)
            {
                _collapse = Mathf.MoveTowards(_collapse, 1f, dt / CollapseSeconds);
            }
            else if (_collapse > 0f && _altitudeAboveGround > ReinflateAltitude && !_eva.IsInWater)
            {
                // Airborne again after a landing (teleport, walking off an edge): the canopy
                // opens again from its packed-out state, like a fresh deployment, instead of
                // playing the landing backwards. Small hops keep it down.
                _collapse = 0f;
                _visualCollapse = 0f;
                _inflate = 0f;
                _hasLandedAnchor = false;
                _landedInWater = false;
                _canopyDirection = airspeed > 1f ? -airVelocity / airspeed : transform.up;

                // Heading from the drood as it is now (it may have turned on the ground; the
                // landed heading would turn the wing against it).
                _canopyForward = HeadingFromBody(_canopyDirection);
                _yawRate = 0f;
                _turnRate = 0f;
                if (_landingFall != null)
                {
                    _landingFall.Abort();
                }

                Log("Airborne again after landing: the canopy reinflates.");
            }

            var sinkSpeed = (float)Vector3d.Dot(airVelocity, craft.FlightData.GravityFrameNormalized);
            UpdateSteering(commandPod, landed, airspeed * Mathf.Sqrt((float)density / SeaLevelDensity), sinkSpeed, dt);
            var brakes = Mathf.Min(_leftPull, _rightPull);

            // Total aerodynamic force of the canopy (drag + lift).
            var aeroForce = Vector3.zero;
            if (_starved)
            {
                // Starved of air (held still in the air): only the drag of the fabric bundle.
                if (airspeed > 0.01f && density > 0f)
                {
                    aeroForce = -airVelocity / airspeed * (0.5f * (float)density * airspeed * airspeed * StarvedDragArea * ModApi.Constants.MassScale);
                }
            }
            else if (IsRamAir && airspeed > 0.01f && density > 0f)
            {
                aeroForce = Vector3.ClampMagnitude(
                    RamAirAttitudeForce((float)density, airVelocity, airspeed, landed, dt),
                    body.mass * MaxOpeningG * 9.81f);
            }
            else if (airspeed > 0.01f && density > 0f)
            {
                var radius = CurrentRadius;
                var area = Mathf.PI * radius * radius;
                // Juno stores rigidbody masses scaled by Constants.MassScale (0.01: 115 kg is
                // mass 1.15), so real-world Newtons must be scaled the same way. The cap is
                // already in rigidbody units (body.mass).
                var drag = 0.5f * density * airspeed * airspeed * DragCoefficient * area * _extend * (1f - _collapse) * ModApi.Constants.MassScale
                    * (1f + RoundBrakeDragGain * brakes);
                drag = Mathf.Min(drag, body.mass * MaxOpeningG * 9.81f);
                var airDirection = airVelocity / airspeed;
                aeroForce = -airDirection * drag;

                // Lift toward the canopy's heading, perpendicular to the airflow. Only once
                // inflated, and not while collapsed on the ground. The steering lines turn the
                // heading (UpdateSteering); brakes cut the drive.
                var drive = Vector3.ProjectOnPlane(_canopyForward, airDirection);
                var lift = landed ? 0f : drag * GlideRatio * _inflate * (1f - _collapse) * (1f - RoundBrakeDriveLoss * brakes);
                if (lift > 0f && drive.sqrMagnitude > 1e-4f)
                {
                    aeroForce += drive.normalized * lift;
                }
            }

            // Soft touchdown: the canopy stops pulling at once (its lift and drag would lift the
            // drood off its feet again, a skip before the run-out).
            _runOut = _runOut != null ? _runOut : GetComponent<DroodRunOut>();
            if (_runOut != null && _runOut.Active)
            {
                aeroForce = Vector3.zero;
            }

            ApplyCanopyPull(body, commandPod, aeroForce, airspeed, airVelocity, landed, dt);
        }

        /// <summary>
        /// The hanging pendulum. The canopy is pushed around by the air, so the lines trail the
        /// direction of its total pull (drag + lift) by about line length / airspeed; a gust or
        /// a turn swings them. The pull, as strong as the canopy's, acts along the lines at the
        /// harness (HarnessHeight above the centre of mass, fixed in the drood's
        /// frame, not the animated shoulders), which keeps the body under the lines like a real
        /// harness. With glide, drag (back/up) plus lift (forward/up) balance the weight, so in
        /// a steady descent the canopy is right overhead.
        ///
        /// Swing damping is a damping ratio against the tilt about the centre of mass, scaled
        /// from the actual line pull, harness height and the drood's inertia about that axis, so
        /// it behaves the same with heavy held parts. Twist about the lines is only lightly
        /// damped (or steered by the turn keys in free look), plus small gusts.
        /// </summary>
        private void ApplyCanopyPull(Rigidbody body, ModApi.Craft.Parts.ICommandPod commandPod,
            Vector3 aeroForce, float airspeed, Vector3 airVelocity, bool landed, float dt)
        {
            // Starved of air: the lines hang down with the fabric bundle below the drood, whose
            // small drag acts on the drood directly; no swing or twist from the canopy.
            if (_starved)
            {
                // The fabric bundle trails in the airflow, sagging by the gravity it feels: above
                // the drood in a falling deflation, hanging down when held still.
                var bundleDrag = 0.5f * _visualDensity * airspeed * airspeed * StarvedDragArea / StarvedBundleMass;
                var trail = (airspeed > 0.1f ? -airVelocity / airspeed * bundleDrag : Vector3.zero) + (_visualGravity - _visualAcceleration);
                trail = trail.sqrMagnitude > 1e-6f ? trail.normalized : _visualDown;
                _canopyDirection = Vector3.Slerp(_canopyDirection, trail, 1f - Mathf.Exp(-dt / StarvedHangSeconds)).normalized;
                _linesRolling = false;
                _hasLastLines = false;
                body.AddForce(aeroForce, ForceMode.Force);
                return;
            }

            var targetDirection = airspeed > 1f && aeroForce.sqrMagnitude > 1e-10f
                ? aeroForce.normalized
                : airspeed > 1f ? -airVelocity / airspeed : transform.up;
            var lag = CurrentLineLength / Mathf.Max(airspeed, 1f);
            if (IsRamAir)
            {
                // Faster than RamAirSteeringSpeed the canopy banks (and pitches) quicker: up to
                // RollRateAtSpeed faster at twice that.
                var quicker = 1f + RollRateAtSpeed * Mathf.Clamp01(airspeed / (RamAirSteeringSpeed * WingSpeedScale) - 1f);
                lag = Mathf.Max(lag, Mathf.Lerp(CanopyPitchLag / quicker, FlareLinesLag, _flarePull));
                // On the front risers (pulled, or let up lately) the lines swing no faster than
                // FrontLinesLag (going out ahead: FrontPullLinesLag; coming back after letting them
                // up: FrontReleaseBackLag, a gentle pull-out), so the canopy
                // goes out ahead and comes back over the drood smoothly; not in a front riser turn
                // (its bank stays quick). While the dive starts (the pull's surge) they swing back
                // no faster than FrontSurgeBackLag: the nose-down cuts the lift before the speed
                // builds, which would pull the canopy back first.
                var headingNow = Vector3.ProjectOnPlane(_canopyForward, _canopyDirection);
                var swingingBack = Vector3.Dot(targetDirection - _canopyDirection, headingNow) < 0f;
                var frontLag = !swingingBack ? FrontPullLinesLag : _frontRelease > 0f ? FrontReleaseBackLag : FrontLinesLag;
                lag = Mathf.Max(lag, frontLag * Mathf.Max(FrontPullAverage * (1f - Mathf.Abs(_frontTurn)), _frontRelease));
                if (swingingBack)
                {
                    lag = Mathf.Max(lag, FrontSurgeBackLag * Mathf.Clamp01(_frontSurge * FrontSurgeBackFull));
                }
                // Letting the front risers up (and FrontReleaseHoldSeconds after): the lines swing
                // forward no faster than FrontReleaseForwardLag; while the wing opens, no faster than
                // OpeningForwardLag, easing out after (pulling out of a steep path, the wing's lift
                // points forward).
                _frontRelease = _frontPull < _lastFrontPull - 1e-5f ? 1f : Mathf.MoveTowards(_frontRelease, 0f, dt / FrontReleaseHoldSeconds);
                _openingFullFor = _inflate < 1f ? 0f : _openingFullFor + dt;
                var flying = Vector3.Dot(airVelocity, Vector3.ProjectOnPlane(_canopyForward, _canopyDirection).normalized) >= OpeningHoldUntilForward * WingSpeedScale;
                _openingLagHold = _inflate < 1f || (_openingLagHold >= 1f && !flying && _openingFullFor < OpeningHoldMaxSeconds)
                    ? 1f
                    : Mathf.MoveTowards(_openingLagHold, 0f, dt / OpeningLagEaseSeconds);
                _lastFrontPull = _frontPull;
                var heading = Vector3.ProjectOnPlane(_canopyForward, _canopyDirection);
                if (Vector3.Dot(targetDirection - _canopyDirection, heading) > 0f)
                {
                    if (_frontRelease > 0f)
                    {
                        lag = Mathf.Lerp(lag, Mathf.Max(lag, FrontReleaseForwardLag), _frontRelease);
                    }

                    if (_openingLagHold > 0f)
                    {
                        lag = Mathf.Max(lag, OpeningForwardLag * Mathf.SmoothStep(0f, 1f, _openingLagHold));
                    }
                }

                if (!landed)
                {
                    targetDirection = CanopySway(targetDirection);
                }
            }
            if (IsRamAir && landed && _hasLandedAnchor)
            {
                // Down, the wing loses its air and sinks back toward where it folds on the ground
                // behind the drood, steadily at LandedSwingRate.
                var toFold = _landedGround - CanopyAnchor(HarnessPosition);
                if (toFold.sqrMagnitude > 1e-4f)
                {
                    var swingRate = Mathf.Lerp(LandedSwingRate, StalledSwingRate, Mathf.Max(_leftStall, _rightStall));
                    _canopyDirection = Vector3.RotateTowards(_canopyDirection, toFold.normalized, swingRate * Mathf.Deg2Rad * dt, 0f).normalized;
                }

                _linesRolling = false;
            }
            else
            {
                var lagged = Vector3.Slerp(_canopyDirection, targetDirection, 1f - Mathf.Exp(-dt / lag)).normalized;
                _canopyDirection = IsRamAir ? RollLines(lagged, targetDirection, aeroForce.magnitude / body.mass, _frontRollForce / body.mass, airspeed, landed, dt) : lagged;
            }

            var pull = aeroForce.magnitude;
            var harnessHeight = HarnessHeight;
            if (pull > 0f)
            {
                // The lines carry the canopy's pull along their own (lagging) direction. It must
                // never push the drood along its flight path (a strong pull along lines still
                // pointing forward from a dive would add energy). Gravity is the only source of
                // speed.
                var linePull = _canopyDirection * pull;
                if (airspeed > 0.1f)
                {
                    var motion = airVelocity / airspeed;
                    var along = Vector3.Dot(linePull, motion);
                    if (along > 0f)
                    {
                        linePull -= motion * along;
                    }
                }

                // A stalled canopy jerks its lines sideways: quick tugs across the lines, up to
                // StallTug of the pull.
                var stall = IsRamAir ? Mathf.Max(_leftStall, _rightStall) : 0f;
                if (stall > 0f)
                {
                    var t = Time.time * StallTugRate;
                    var tug = new Vector3(
                        Mathf.PerlinNoise(t, 401.3f) * 2f - 1f,
                        Mathf.PerlinNoise(t, 443.7f) * 2f - 1f,
                        Mathf.PerlinNoise(t, 487.9f) * 2f - 1f);
                    linePull += Vector3.ProjectOnPlane(tug, _canopyDirection) * (pull * StallTug * stall);
                }

                body.AddForceAtPosition(linePull, body.worldCenterOfMass + transform.up * harnessHeight, ForceMode.Force);
            }

            // The swing is damped from line stretch on (the pull, which swings the drood upright
            // from the arch, is strongest while the canopy fills), at OpeningSwingDamping then,
            // easing to SwingDamping as it fills.
            var swingHold = landed ? 0f : _extend * (1f - _collapse);
            var angularVelocity = body.angularVelocity;
            var twist = Vector3.Project(angularVelocity, _canopyDirection);
            var tilt = angularVelocity - twist;

            // Once the wing is full the swing is damped against the lines, as the harness does:
            // when the lines roll (a quick front riser turn) the drood rolls with them, and only
            // its swinging relative to them is damped (damped against the world it lagged, folding
            // away from the turn).
            if (_hasLastLines && dt > 0f && _inflate >= 1f)
            {
                var linesRate = Vector3.Cross(_lastLines, _canopyDirection) / dt;
                tilt -= Vector3.ProjectOnPlane(linesRate, _canopyDirection);
            }

            _lastLines = _canopyDirection;
            _hasLastLines = true;
            if (swingHold > 0f && tilt.sqrMagnitude > 1e-8f)
            {
                // Critical damping of a pendulum with stiffness pull x harness height.
                var inertia = InertiaAbout(body, tilt.normalized);
                var critical = 2f * Mathf.Sqrt(pull * harnessHeight * inertia);
                var damping = Mathf.Lerp(OpeningSwingDamping, SwingDamping, _inflate);
                body.AddTorque(-tilt * (damping * critical * swingHold), ForceMode.Force);
            }

            var hold = landed ? 0f : _extend * _inflate * (1f - _collapse);
            if (hold <= 0f)
            {
                return;
            }

            // Twist about the lines: the twisted lines turn the drood toward the canopy's
            // heading (the drood has no other way to turn: the jetpack is off), well damped.
            var droodForward = Vector3.ProjectOnPlane(transform.forward, _canopyDirection);
            var canopyForward = Vector3.ProjectOnPlane(_canopyForward, _canopyDirection);
            var twistError = droodForward.sqrMagnitude > 1e-4f && canopyForward.sqrMagnitude > 1e-4f
                ? Vector3.SignedAngle(droodForward, canopyForward, _canopyDirection) * Mathf.Deg2Rad
                : 0f;
            var twistDamping = 2f * LineTwistDampingRatio * Mathf.Sqrt(LineTwistStiffness);
            var twistTorque = _canopyDirection * (twistError * LineTwistStiffness) - (twist - _canopyDirection * _turnRate) * twistDamping;

            body.AddTorque(twistTorque * hold, ForceMode.Acceleration);
        }

        /// <summary>Dynamic pressure of the airflow (Pa).</summary>
        private static float DynamicPressure(double density, float airspeed) => 0.5f * (float)density * airspeed * airspeed;

        /// <summary>
        /// A canopy stays open only by the air ramming into it. Below StarvedPressure (dynamic
        /// pressure, Pa) for StarvedSeconds, held nearly still in the air (hanging from a tether,
        /// a crane, a craft), it deflates (<see cref="_starved"/>); above ReopenPressure it opens
        /// again. In pascals, so it holds in any air density; free flight and towing are far
        /// above it (~180 Pa at 17 m/s at sea level). Not on the ground: there it collapses on
        /// its own.
        /// </summary>
        private void UpdateStarved(bool landed, float pressure)
        {
            // Nor for OpenStartAirSeconds after an open start: the teleport's speed reaches the
            // drood a step or two later.
            if (landed || _extend < 1f || Time.time - _openStartAt < OpenStartAirSeconds)
            {
                _lowPressureSince = float.NegativeInfinity;
                _starved = false;
                return;
            }

            // The ram-air deflates: far too slow to fly, the wing loses its pressure at once and
            // drops back, and the drood falls until the speed builds and it reinflates. Not while it
            // stalls or its lines are
            // pulled that deep: the stall collapses it partly itself (StallCollapse), and the slow
            // moment of a full pull from speed is how a stall begins. The round canopy only
            // deflates when held still in the air (it descends ~5 m/s).
            var starvedPressure = IsRamAir ? RamAirDeflatePressure : StarvedPressure;
            var reopenPressure = IsRamAir ? RamAirReinflatePressure : ReopenPressure;
            var delay = IsRamAir ? 0f : StarvedSeconds;
            var stalling = IsRamAir && (Mathf.Max(_leftStall, _rightStall) > 0f || Mathf.Max(_leftPull, _rightPull) >= StallPull);
            if (pressure >= starvedPressure || (stalling && !_starved))
            {
                _lowPressureSince = float.NegativeInfinity;
            }
            else if (float.IsNegativeInfinity(_lowPressureSince))
            {
                _lowPressureSince = Time.time;
            }

            if (!_starved && !float.IsNegativeInfinity(_lowPressureSince) && Time.time - _lowPressureSince >= delay)
            {
                _starved = true;
                _yawRate = 0f;
                _turnRate = 0f;
                Log(IsRamAir
                    ? $"Deflated ({pressure:0.0} Pa): the wing loses its pressure and drops back."
                    : $"Canopy starved of air ({pressure:0.0} Pa for {StarvedSeconds:0.0} s): it deflates.");
            }
            else if (_starved && pressure > reopenPressure)
            {
                _starved = false;
                Log($"Airflow back ({pressure:0} Pa): the canopy reopens.");
            }
        }

        /// <summary>The body's moment of inertia about a world axis through its centre of mass.</summary>
        private static float InertiaAbout(Rigidbody body, Vector3 axis)
        {
            var local = Quaternion.Inverse(body.rotation * body.inertiaTensorRotation) * axis;
            var tensor = body.inertiaTensor;
            return local.x * local.x * tensor.x + local.y * local.y * tensor.y + local.z * local.z * tensor.z;
        }

        /// <summary>
        /// The ram-air's lines with roll momentum. Their roll about the canopy's heading (taken
        /// square to the lines, so it holds at any attitude, upside down too) swings toward the
        /// force's direction like the drood hanging <see cref="CurrentLineLength"/> plus
        /// <see cref="HarnessHeight"/> below it as a pendulum, under the apparent weight the lines
        /// carry (<paramref name="lineAcceleration"/>, their pull per unit of the drood's mass),
        /// damped by LinesRollDamping, rolled toward the deeper front riser (the sideways force
        /// <paramref name="rollForce"/> per unit of the drood's mass) and braked by the air against
        /// the roll (LinesRollDrag). Their swing fore and aft (about the axis square to the heading)
        /// follows <paramref name="lagged"/>. Vectors and rotations only, no angle decomposition.
        /// </summary>
        private Vector3 RollLines(Vector3 lagged, Vector3 targetDirection, float lineAcceleration, float rollForce, float airspeed, bool landed, float dt)
        {
            var lines = _canopyDirection;
            var heading = Vector3.ProjectOnPlane(_canopyForward, lines);
            if (landed || dt <= 0f || heading.sqrMagnitude < 1e-6f)
            {
                _linesRolling = false;
                return lagged;
            }

            heading.Normalize();
            if (!_linesRolling)
            {
                _linesRollRate = 0f;
                _linesRolling = true;
            }

            // Fore and aft: the lag's own swing, without its part about the heading.
            var side = Vector3.Cross(heading, lines);
            Quaternion.FromToRotation(lines, lagged).ToAngleAxis(out var lagAngle, out var lagAxis);
            if (lagAngle > 180f)
            {
                lagAngle -= 360f;
            }

            var foreAft = Vector3.Dot(lagAxis, side) * lagAngle;
            lines = Quaternion.AngleAxis(foreAft, side) * lines;

            // Sideways: the pendulum toward the force (positive rolls the canopy to its right, a
            // negative angle about the heading).
            var toward = -Vector3.SignedAngle(Vector3.ProjectOnPlane(lines, heading), Vector3.ProjectOnPlane(targetDirection, heading), heading) * Mathf.Deg2Rad;
            var frequencySquared = Mathf.Max(lineAcceleration, LinesRollMinAcceleration) / (CurrentLineLength + HarnessHeight);
            var frequency = Mathf.Sqrt(frequencySquared);
            var roll = rollForce / (CurrentLineLength + HarnessHeight);
            var speed = airspeed / (RamAirSteeringSpeed * WingSpeedScale);
            var rollDrag = LinesRollDrag * speed * speed * _linesRollRate * Mathf.Abs(_linesRollRate);
            var damping = LinesRollDamping + LinesRollSlowDamping * Mathf.Clamp01(1f - speed);
            _linesRollRate += (roll + frequencySquared * Mathf.Sin(toward) - 2f * damping * frequency * _linesRollRate - rollDrag) * dt;
            return (Quaternion.AngleAxis(-_linesRollRate * dt * Mathf.Rad2Deg, heading) * lines).normalized;
        }

        /// <summary>
        /// On touchdown under an inflated canopy: a hard one plays the hard-landing or the
        /// fall-flat clip (<see cref="DroodLandingFall.Classify"/>); a soft one runs to a stop
        /// (<see cref="DroodRunOut"/>). Uses the last airborne velocity, since contact has
        /// already slowed the drood on the frame it counts as grounded, measured against the
        /// ground under the drood: down along its normal, forward along it (on a slope the same
        /// as on the flat).
        /// </summary>
        private void DetectLandingFall(Vector3 airVelocity)
        {
            // Water counts as touchdown too, but the hard-landing clip is for solid ground.
            var grounded = RecentlyTouchedDown;
            if (!grounded)
            {
                _lastAirborneVelocity = airVelocity;
                _airborneSeconds += Time.fixedDeltaTime;
            }
            else if (!_wasGrounded && _inflate > 0.5f && !_eva.IsInWater && _airborneSeconds >= LandingMinFlightSeconds)
            {
                // Into the ground along its normal (on a slope: square to it), forward along it.
                var up = -_visualDown;
                var normal = SlopeNormal(PhysicsRoot, up);
                var slope = Vector3.Angle(normal, up);
                var sinkSpeed = -Vector3.Dot(_lastAirborneVelocity, normal);
                var groundVelocity = Vector3.ProjectOnPlane(_lastAirborneVelocity, normal);
                GetComponent<DroodRunOut>()?.Touchdown();
                var kind = DroodLandingFall.Classify(sinkSpeed, groundVelocity.magnitude);
                var hard = kind != DroodLandingFall.Kind.Soft;
                var steep = slope > DroodLandingFall.SteepSlope;
                if (steep)
                {
                    // Too steep for a run or the upright clips: a hard touchdown falls flat (lying,
                    // the pose tilts with the whole slope); a soft one is the game's own.
                    Log($"Touchdown on a {slope:0}° slope (steeper than {DroodLandingFall.SteepSlope:0}°): {(hard ? "falls flat" : "no run-out")}.");
                    if (hard)
                    {
                        kind = DroodLandingFall.Kind.Flat;
                        GetComponent<DroodLandingFall>()?.Begin(sinkSpeed, groundVelocity.magnitude, kind, normal);
                    }
                }
                else if (hard)
                {
                    // Hard touchdown: the hard-landing, collapse or fall-flat clip (DroodLandingFall.Classify).
                    GetComponent<DroodLandingFall>()?.Begin(sinkSpeed, groundVelocity.magnitude, kind, normal);
                }
                else
                {
                    // Soft touchdown with forward speed: run to a stop.
                    GetComponent<DroodRunOut>()?.Begin(groundVelocity);
                    GetComponent<DroodFirstPersonBody>()?.SoftTouchdown(sinkSpeed, groundVelocity.magnitude);
                }

                var grade = ReportLanding(sinkSpeed, groundVelocity.magnitude, kind);
                var dropZone = GetComponent<DroodDropZone>();
                var scored = dropZone != null && dropZone.Landed(grade);
                GetComponent<DroodSwoopCourse>()?.Landed(grade, kind);
                DroodLandingRecord.Add(IsSwoopWing ? "SwoopWing" : IsRamAir ? "RamAir" : "Round", sinkSpeed, groundVelocity.magnitude, grade, kind,
                    _flareStartHeight, _flareStartSpeed, Time.time - _flareStartTime,
                    scored ? dropZone.LastDistance : float.NaN, scored ? dropZone.LastScore : -1);
                _flareStartTime = float.NegativeInfinity;
                GetComponent<DroodVictory>()?.Landed(grade, slope);
            }

            if (grounded)
            {
                _airborneSeconds = 0f;
            }

            _wasGrounded = grounded;
        }

        // A touchdown counts as a landing (animation, report) only after at least this long in
        // the air (s): not the settling of a drood that a scene load puts down under its canopy.
        private const float LandingMinFlightSeconds = 2f;

        private float _airborneSeconds;

        // The ground's slope at a touchdown: the normals under the drood and SlopeProbe (m) ahead,
        // behind and to each side, averaged (a single ray's normal is the ground's local bump).
        private const float SlopeProbe = 0.5f;

        /// <summary>The ground's normal around <paramref name="point"/>, averaged (up where there is none).</summary>
        /// <summary>
        /// Physics step, after everything else: in the air under the canopy or in the free-fall
        /// arch the drood's body is drawn between physics steps (Rigidbody interpolation). The
        /// game interpolates a drood only on terrain (EvaScript.GetShouldInterpolate) and sets
        /// that each tick; in the air it was drawn where each step left it, in uneven jumps
        /// between frames (frames are often shorter than a step), and every camera following it
        /// juddered. Physics reads the body's own position (<see cref="PhysicsRoot"/>), not the
        /// drawn Transform, which then lags up to a step. Only for the player's drood (the one
        /// the camera follows): the game draws crafts where each step left them (CraftBuilder
        /// makes every body without interpolation), so a drood watched from another craft, e.g.
        /// let go from the player's plane, drawn between steps would judder against it.
        /// </summary>
        internal void InterpolateInAir()
        {
            var body = _part?.BodyScript?.RigidBody;
            _freeFallState = _freeFallState != null ? _freeFallState : GetComponent<DroodFreeFall>();
            if (body == null || body.isKinematic || _eva.IsGrounded || !_eva.IsPlayerCraft || body.interpolation != RigidbodyInterpolation.None)
            {
                return;
            }

            if (UnderChuteInAir || (_freeFallState != null && _freeFallState.Weight > 0f))
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        private DroodFreeFall _freeFallState;

        /// <summary>The drood's position as physics has it (the Transform is drawn between steps in the air).</summary>
        private Vector3 PhysicsRoot
        {
            get
            {
                var body = _part?.BodyScript?.RigidBody;
                return body != null ? body.position : transform.position;
            }
        }

        private Vector3 SlopeNormal(Vector3 point, Vector3 up)
        {
            var forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            var right = Vector3.Cross(up, forward);
            var sum = Vector3.zero;
            foreach (var offset in new[] { Vector3.zero, forward, -forward, right, -right })
            {
                if (FindGround(point + offset * SlopeProbe, up, out _, out var normal))
                {
                    sum += normal;
                }
            }

            return sum.sqrMagnitude > 1e-6f ? sum.normalized : up;
        }

        // The landing report's grade (1..10), on its own, gentler measure than the landing kind's
        // (which picks the clip): the touchdown's speeds as shares of GradeSinkSpeed down and
        // GradeForwardSpeed forward, combined (√(down² + forward²)); 10 at a touchdown of
        // LandingIdealSink (m/s) down and LandingIdealForward forward (or gentler), falling evenly
        // to 1 at GradeOneAt.
        private const float LandingIdealSink = 0.5f;
        private const float LandingIdealForward = 5f;
        private const float GradeSinkSpeed = 4f;
        private const float GradeForwardSpeed = 14f;
        private const float GradeOneAt = 1.5f;

        // Earlier landings in the Flight Log, greyed out (a rich-text colour).
        private const string LandingOlderColour = "#8C8C8C";

        // The latest landing's Flight Log entry (any drood), to grey it out at the next one.
        private static System.WeakReference<ModApi.Flight.UI.FlightLogEntry> _lastLandingEntry;
        private static string _lastLandingText;

        private static float GradeSeverity(float sinkSpeed, float forwardSpeed) =>
            new Vector2(Mathf.Max(sinkSpeed, 0f) / GradeSinkSpeed, Mathf.Max(forwardSpeed, 0f) / GradeForwardSpeed).magnitude;

        /// <summary>
        /// One entry in the game's flight log after each landing (the player's Flight Log panel):
        /// the touchdown speeds and a grade (returned).
        /// </summary>
        private int ReportLanding(float sinkSpeed, float forwardSpeed, DroodLandingFall.Kind kind)
        {
            var severity = GradeSeverity(sinkSpeed, forwardSpeed);
            var ideal = GradeSeverity(LandingIdealSink, LandingIdealForward);
            var score = 10f - 9f * Mathf.Clamp01((severity - ideal) / (GradeOneAt - ideal));
            var grade = Mathf.Clamp(Mathf.RoundToInt(score), 1, 10);
            var text = $"Landing: {sinkSpeed:0.0} m/s down, {forwardSpeed:0.0} m/s forward. Grade {grade}/10.";
            var log = Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.FlightLog;
            if (log != null)
            {
                // The previous landing greys out: only the latest stands out.
                if (_lastLandingEntry != null && _lastLandingEntry.TryGetTarget(out var previous) && previous.IsDynamic)
                {
                    previous.Text = $"<color={LandingOlderColour}>{_lastLandingText}</color>";
                }

                var entry = log.AddLog(text, ModApi.Flight.UI.FlightLogEntryCategory.Default, true, _part);
                _lastLandingEntry = entry != null ? new System.WeakReference<ModApi.Flight.UI.FlightLogEntry>(entry) : null;
                _lastLandingText = text;
            }

            Log(text);
            return grade;
        }
    }
}
