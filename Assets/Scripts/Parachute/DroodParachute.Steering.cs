using UnityEngine;

namespace Drood2
{
    // Steering: the steering lines (toggles), the canopy heading they turn, and the jetpack
    // being off while hanging.
    public partial class DroodParachute
    {
        /// <summary>
        /// How far (m) the hand on <paramref name="shoulder"/>'s side has pulled its steering line
        /// straight down, for the hanging pose's arms (at most PullTravel); the flare is
        /// <see cref="FlareFraction"/>.
        /// </summary>
        internal float PullDistance(Vector3 shoulder)
        {
            var right = Vector3.Dot(shoulder - HarnessPosition, transform.right) >= 0f;
            var pull = right ? _rightPull : _leftPull;
            return Mathf.Max(pull, FrontPullOf(right ? 1 : -1)) * PullTravel;
        }

        /// <summary>
        /// One side's front riser (W) pull (side -1 left, +1 right). Turning on the front risers
        /// pulls the turn side's riser deeper, toward full, and lets the other one up, both by up
        /// to FrontRiserTurnEase of the way: with full W a turn only lets the outside one up (the
        /// canopy comes back a little toward normal flight), in a partial dive it is a mix.
        /// </summary>
        internal float FrontPullOf(int side)
        {
            var share = Mathf.Abs(_frontTurn) * FrontRiserTurnEase;
            var turnSide = side * _frontTurn > 0f;
            return turnSide ? _frontPull + (1f - _frontPull) * share : _frontPull * (1f - share);
        }

        /// <summary>
        /// The flare's depth beyond full brakes on one side (side -1 left, +1 right; 0..1), as the
        /// hands pull it (<see cref="FlareFractionAt"/>).
        /// </summary>
        internal float FlareOf(int side)
        {
            if (!IsRamAir || _flarePull <= 0f)
            {
                return 0f;
            }

            var deeper = Mathf.Max(_leftPull, _rightPull);
            var own = side > 0 ? _rightPull : _leftPull;
            return _flarePull * (deeper > 1e-3f ? Mathf.Clamp01(own / deeper) : 1f);
        }

        /// <summary>The steering lines' pull (0..1): left, right.</summary>
        internal float LeftPull => _leftPull;

        internal float RightPull => _rightPull;

        /// <summary>Where a line pulled this deep stalls its half when slow (0..1).</summary>
        internal static float StallPullShare => StallPull;

        /// <summary>The mouse's forward dead band is this many times the player's.</summary>
        internal static float MouseForwardDeadband => MouseForwardDeadzoneScale;

        /// <summary>The two front risers' pull on average: the whole wing's nose-down.</summary>
        internal float FrontPullAverage => (FrontPullOf(-1) + FrontPullOf(1)) * 0.5f;

        /// <summary>
        /// The ram-air hands hold the front risers (W) rather than the steering toggles.
        /// </summary>
        internal bool HoldsFrontRisers => IsRamAir && _frontPull > Mathf.Max(_leftPull, _rightPull);

        /// <summary>
        /// Where the steering line on <paramref name="shoulder"/>'s side comes down to the toggle
        /// in the hand from: the ram-air's guide ring at the top of the rear riser, or the back of
        /// the round canopy's rim. As placed last frame, relative to the drood.
        /// </summary>
        internal bool TryGetToggleLineTop(Vector3 shoulder, out Vector3 top)
        {
            top = default;
            if (_state != State.Deployed || !_visualsBuilt || _visualCollapse > 0f)
            {
                return false;
            }

            var side = Vector3.Dot(shoulder - HarnessPosition, transform.right) >= 0f ? 1f : -1f;
            top = IsRamAir
                ? transform.TransformPoint(_riserTops[(side > 0f ? 2 : 0) + 1])
                : transform.TransformPoint(_canopyRootLocal) + _canopyRoot.rotation * RoundToggleRim(side);
            return true;
        }

        /// <summary>
        /// The round canopy's steering line leaves the rim here (canopy-root space): at the back,
        /// on that side.
        /// </summary>
        private Vector3 RoundToggleRim(float side) => new Vector3(side * _rimRadius * 0.7f, 0f, -_rimRadius * 0.7f);

        /// <summary>
        /// How far into the ram-air flare the hands are (0..1): the hanging pose brings them down
        /// to the hips (a flare pulls the toggles to arm's length).
        /// </summary>
        internal float FlareFraction => IsRamAir ? _flarePull : 0f;

        /// <summary>
        /// The flare's depth for the hand at <paramref name="shoulder"/>: its share follows its own
        /// line against the deeper one, so a turn in the flare (the other line let up,
        /// BrakeTurnLetUp) lifts that hand.
        /// </summary>
        internal float FlareFractionAt(Vector3 shoulder)
        {
            if (!IsRamAir || _flarePull <= 0f)
            {
                return 0f;
            }

            var deeper = Mathf.Max(_leftPull, _rightPull);
            var own = Vector3.Dot(shoulder - HarnessPosition, transform.right) >= 0f ? _rightPull : _leftPull;
            return _flarePull * (deeper > 1e-3f ? Mathf.Clamp01(own / deeper) : 1f);
        }

        /// <summary>
        /// How ready for the ground the legs are (0..1): building from LegsReadyFrom (m, feet
        /// above the ground) to all at LegsReadyFull, under either canopy.
        /// </summary>
        internal float LegsReady => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LegsReadyFrom, LegsReadyFull, _feetHeight));

        // A full turn with full brakes lets the other toggle up by this much (0..1 of its pull;
        // less with less brakes): ~10-15°/s of turn in a full flare.
        private const float BrakeTurnLetUp = 0.4f;

        // Held, the ram-air's brakes go on down to the flare's depth from DeepPullFromSpeed to
        // DeepPullRange below it (m/s, equivalent airspeed), see UpdateSteering; the reach shrinks to
        // half at BrakeHalfReachSpeed. Held, the wing mushes rather than stalls (StallSpeed); a
        // full pull held from 3-6 m up runs it out of speed before the ground (a flare begun too
        // high).
        // Low and fast the flare comes sooner: with the ground LateFlareSeconds away (the sooner
        // of the feet height over the sink speed and the ground ahead along the flight path,
        // PathToGround: a slope rising ahead) the range starts moving up, reaching
        // DeepPullFromSpeedLate (and DeepPullRange below it) at LateFlareFullSeconds. Climbing,
        // it stays at DeepPullFromSpeed.
        private const float DeepPullFromSpeed = 12.5f;
        private const float DeepPullFromSpeedLate = 15.5f;
        private const float DeepPullRange = 3.5f;
        private const float LateFlareSeconds = 1f;
        private const float LateFlareFullSeconds = 0.4f;

        private const float BrakeHalfReachSpeed = 28f;

        // The flare's depth (beyond full brakes) is pulled in over FlarePullSeconds and let up
        // over FlareReleaseSeconds (s, all of it), so key taps add it in small steps and letting
        // go stops a balloon within about a quarter of a second.
        private const float FlareReleaseSeconds = 0.5f;
        private const float BrakeReleaseSeconds = 0.5f;
        private const float StowedBrakes = 0.45f;
        private bool _brakesStowed;

        // The steering keys reach the canopy once it is fully open and they have been let go
        // since (the hands take the toggles after the opening): keys held from free fall (track,
        // turns) don't pull the lines while it opens.
        private bool _keysTaken;

        // A full pull's flare depth, kept from going back up (see UpdateSteering).
        private const float FullPullFrom = 0.95f;
        private const float FlareFromBrakes = 0.8f;

        private float _flareReach;

        // Held in a stall (see UpdateSteering).
        private const float StallHoldFrom = 0.95f;
        private const float StallHoldReleaseBelow = 0.1f;
        private bool _stallHold;

        // The hands are on the toggles or on the front risers, never pulling both: changing over,
        // the ones in hand are let go (quickly, RamAirToggleReleaseSeconds) and the others only
        // come in once those are up to HandChangeFrom. With the front risers in hand a turn is
        // theirs alone.
        private const float HandChangeFrom = 0.1f;
        private bool _onRisers;
        private float _brakeHold;

        // This step's raw inputs: the keys' back and turn (for the log when the brakes are
        // unstowed), and whether the mouse joystick steers (full authority, see UpdateSteering).
        private float _inputBack;
        private float _inputTurn;
        private bool _inputMouseOn;
        private const float FlarePullSeconds = 0.35f;

        private const float LegsReadyFrom = 2f;
        private const float LegsReadyFull = 0.5f;

        /// <summary>Down (toward the planet's centre), as of this frame.</summary>
        internal Vector3 WorldDown => _visualDown;

        /// <summary>
        /// The steering lines, from the game's own EVA controls (so the player's bindings apply):
        /// turn left/right pulls that side's line (with the brakes on it also lets the other side
        /// up), back pulls both (brakes, going on into the flare as the wing slows), and on the ram-air canopy forward pulls the front risers (a dive). A line
        /// takes ToggleSeconds to pull all the way (the ram-air: RamAirToggleSeconds, and it lets
        /// go in RamAirToggleReleaseSeconds). Pulling one side more than the other turns the
        /// canopy's heading (the ram-air's in RamAirAttitudeForce); the lines then twist the
        /// drood after it (ApplyCanopyPull). Only the player-controlled drood steers.
        /// </summary>
        private void UpdateSteering(ModApi.Craft.Parts.ICommandPod commandPod, bool landed, float equivalentAirspeed, float sinkSpeed, float dt)
        {
            var controls = commandPod.Controls;
            var steer = !landed && _eva.IsPlayerCraft && controls != null;
            // Mouse steering (DroodMouseSteering) while hanging: left/right turns, up the front
            // risers, down brakes and flare.
            if (steer && !_keysTaken && _inflate >= 1f && Mathf.Abs(controls.EvaTurn) < 0.1f && Mathf.Abs(controls.EvaMoveFwdAft) < 0.1f)
            {
                _keysTaken = true;
            }

            var keys = steer && _keysTaken;
            var mouse = steer && _inflate >= 1f && IsHanging && DroodMouseSteering.IsOn(_eva) ? DroodMouseSteering.Stick(MouseForwardDeadzoneScale) : Vector2.zero;
            var turnInput = steer ? DroodMouseSteering.Combine(keys ? controls.EvaTurn : 0f, mouse.x) : 0f;
            var forwardInput = steer ? DroodMouseSteering.Combine(keys ? controls.EvaMoveFwdAft : 0f, mouse.y) : 0f;
            var turn = Mathf.Clamp(turnInput, -1f, 1f);
            var brakes = Mathf.Clamp01(-forwardInput);
            _inputBack = steer ? -controls.EvaMoveFwdAft : 0f;
            _inputTurn = steer ? controls.EvaTurn : 0f;
            _inputMouseOn = steer && IsHanging && DroodMouseSteering.IsOn(_eva);
            var front = IsRamAir ? Mathf.Clamp01(forwardInput) : 0f;
            var step = dt / (IsRamAir ? RamAirToggleSeconds : ToggleSeconds);
            var release = IsRamAir ? dt / RamAirToggleReleaseSeconds : step;
            // On the front risers (W) the hands are off the toggles: a turn eases the outside front
            // riser instead (FrontPullOf) rather than pulling a toggle.
            var toggleTurn = turn * (1f - front);
            // Mouse steering gives the pilot full authority: the brakes follow the input all the way
            // and the flare's depth the last of it, at any speed and height (pulled at speed, the
            // wing climbs). The keys (full input or nothing) are helped (assisted): the brakes reach
            // less far the faster it flies, all the way at trim speed and below, half way at
            // BrakeHalfReachSpeed (equivalent airspeed; the front riser dive's top speed) and above,
            // all the way while a half is closed (pumping it open); and the flare comes with the
            // speed and the ground, below.
            var assisted = IsRamAir && !_inputMouseOn;
            var reach = assisted && !HalfClosed ? Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(RamAirTrimSpeed * WingSpeedScale, BrakeHalfReachSpeed * WingSpeedScale, equivalentAirspeed)) : 1f;

            // Ram-air on the keys, held: the back key pulls the lines on down the slower the wing flies, from
            // nothing at the range's start to the flare's depth DeepPullRange below it (equivalent
            // airspeed), evenly: the wing keeps its speed through the float and has it left for
            // the last of the flare. The range starts higher the sooner the ground comes.
            var timeByHeight = sinkSpeed > 0.1f ? _feetHeight / sinkSpeed : float.PositiveInfinity;
            var toGround = Mathf.Min(timeByHeight, _pathTime);
            var late = Mathf.InverseLerp(LateFlareSeconds, LateFlareFullSeconds, toGround);
            var deepPullFrom = Mathf.Lerp(DeepPullFromSpeed * WingFlareScale, IsSwoopWing ? SwoopDeepPullFromLate : DeepPullFromSpeedLate * WingFlareScale, late);
            var slowness = Mathf.InverseLerp(deepPullFrom, deepPullFrom - DeepPullRange * WingFlareScale, equivalentAirspeed);
            var brakePull = brakes * reach;
            // The flare's depth comes only from the last of the brakes' input (FlareFromBrakes on):
            // a part pull (the mouse a little down) stays a part pull at any speed, instead of
            // making a little flare, which holds the toggles at full brakes (below).
            var flareInput = Mathf.InverseLerp(FlareFromBrakes, 1f, brakes);
            var flare = IsRamAir ? flareInput * (assisted ? slowness : 1f) : 0f;

            // Held all the way (FullPullFrom), the flare only goes deeper: once the speed has let
            // the hands down they stay down when it picks up again (letting the flare up by
            // itself as the canopy swung faster pumped its fore-and-aft swing); letting go of
            // the key resets it.
            if (assisted && brakes >= FullPullFrom)
            {
                _flareReach = Mathf.Max(_flareReach, slowness);
                flare = flareInput * _flareReach;
            }
            else
            {
                _flareReach = 0f;
            }

            // Stalled with the toggles held all the way down (the back key, or the mouse, from
            // StallHoldFrom), they stay all the way down, the flare's depth too, whatever the
            // speed, until let go (below StallHoldReleaseBelow); then the usual reach again.
            if (IsRamAir)
            {
                if (brakes < StallHoldReleaseBelow)
                {
                    _stallHold = false;
                }
                else if (brakes >= StallHoldFrom && Mathf.Max(_leftStall, _rightStall) > 0f && !HalfClosed)
                {
                    _stallHold = true;
                }

                if (_stallHold)
                {
                    brakePull = 1f;
                    flare = 1f;
                }
            }

            // While the flare's depth is let up (slowly, FlareReleaseSeconds) the toggles hold at
            // full brakes, so a short pull keeps its effect for a while (the keys are modulated
            // like the hands on the toggles).
            if (IsRamAir && _flarePull > 0.01f)
            {
                brakePull = Mathf.Max(brakePull, reach);
            }

            // The ram-air opens with its brakes stowed (StowedBrakes of the lines' travel), so it
            // opens slow and nose-up instead of diving; the first press of the back or a turn key
            // (or forward) unstows them, and they let up from there like the brakes.
            if (IsRamAir && _brakesStowed)
            {
                if (steer && (brakes > 0.2f || Mathf.Abs(turn) > 0.2f || front > 0.2f))
                {
                    _brakesStowed = false;
                    Log($"Brakes unstowed (back {brakes:0.00}, turn {turn:0.00}, forward {front:0.00}; keys back {_inputBack:0.00} turn {_inputTurn:0.00}, mouse {(_inputMouseOn ? $"on ({mouse.x:0.00}, {mouse.y:0.00})" : "off")}).");
                }
                else
                {
                    // A fixed length of line, at any speed.
                    brakePull = Mathf.Max(brakePull, StowedBrakes);
                }
            }

            // The brakes (both lines together) let up over BrakeReleaseSeconds, so a short let-go
            // eases them a little rather than dropping them (a steering line let go on its own is
            // quick, RamAirToggleReleaseSeconds).
            var handChange = false;
            var riserTurn = 0f;
            if (IsRamAir)
            {
                var wantRisers = front > 0f && !_brakesStowed;
                var togglesIn = Mathf.Max(Mathf.Max(_leftPull, _rightPull), _flarePull);
                if (wantRisers && !_onRisers && togglesIn <= HandChangeFrom)
                {
                    _onRisers = true;
                }
                else if (!wantRisers && _onRisers && _frontPull <= HandChangeFrom)
                {
                    _onRisers = false;
                }

                if (_onRisers)
                {
                    // On the risers: the toggles stay up; letting go for them is quick.
                    brakePull = 0f;
                    flare = 0f;
                    toggleTurn = 0f;
                    riserTurn = wantRisers ? turn : 0f;
                    front = wantRisers ? front : 0f;
                    handChange = !wantRisers && (brakes > 0f || Mathf.Abs(turn) > 0f);
                }
                else
                {
                    // On the toggles: the risers stay up; for them the toggles go up first, quickly.
                    front = 0f;
                    if (wantRisers)
                    {
                        brakePull = 0f;
                        flare = 0f;
                        toggleTurn = 0f;
                        handChange = true;
                    }
                }
            }

            _brakeHold = IsRamAir ? MovePull(_brakeHold, brakePull, step, dt / (handChange ? RamAirToggleReleaseSeconds : BrakeReleaseSeconds)) : brakePull;
            brakePull = _brakeHold;

            // With the brakes on a turn also lets the other toggle up (BrakeTurnLetUp), so the
            // canopy still steers in deep brakes and the flare, where its own line has no further
            // to go.
            var letUp = (IsSwoopWing ? SwoopBrakeTurnLetUp : BrakeTurnLetUp) * brakePull;
            _leftPull = MovePull(_leftPull, Mathf.Clamp01(Mathf.Max(brakePull, -toggleTurn) - Mathf.Max(toggleTurn, 0f) * letUp), step, release);
            _rightPull = MovePull(_rightPull, Mathf.Clamp01(Mathf.Max(brakePull, toggleTurn) - Mathf.Max(-toggleTurn, 0f) * letUp), step, release);
            _frontPull = MovePull(_frontPull, front, dt / RamAirFrontRiserSeconds, dt / (handChange ? RamAirToggleReleaseSeconds : RamAirFrontRiserReleaseSeconds));
            // On the front risers a turn is theirs.
            _frontTurn = Mathf.MoveTowards(_frontTurn, riserTurn, step);

            // Where the flare began (for the landing record): its latest start's height and speed.
            if (flare > 0.01f && _flarePull <= 0.01f)
            {
                _flareStartHeight = _feetHeight;
                _flareStartSpeed = equivalentAirspeed;
                _flareStartTime = Time.time;
            }

            _flarePull = MovePull(_flarePull, flare, dt / FlarePullSeconds, dt / (handChange ? RamAirToggleReleaseSeconds : FlareReleaseSeconds));


            // The ram-air turns the canopy itself, from the forces on its halves
            // (RamAirAttitudeForce).
            if (IsRamAir)
            {
                return;
            }

            // Round canopy: the turn rate follows the lines with the canopy's own inertia.
            var targetRate = (_rightPull - _leftPull) * RoundMaxTurnRate * _inflate * (1f - _collapse);
            _turnRate = Mathf.Lerp(_turnRate, targetRate, 1f - Mathf.Exp(-TurnResponse * dt));
            var turnRate = _turnRate;
            var forward = Vector3.ProjectOnPlane(_canopyForward, _canopyDirection);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.ProjectOnPlane(transform.forward, _canopyDirection);
            }

            // Positive angle about the (up-pointing) lines turns the heading to the right.
            _canopyForward = (Quaternion.AngleAxis(turnRate * Mathf.Rad2Deg * dt, _canopyDirection) * forward).normalized;
        }

        /// <summary>
        /// A line pulled toward <paramref name="target"/>: pulling down at one rate, letting go at
        /// another.
        /// </summary>
        private static float MovePull(float pull, float target, float pullStep, float releaseStep) =>
            Mathf.MoveTowards(pull, target, target > pull ? pullStep : releaseStep);

        /// <summary>
        /// The jetpack is off while hanging (the canopy steers with its lines) and in the
        /// free-fall arch (the body steers, <see cref="DroodFreeFall"/>). Uses the game's
        /// own switch (EvaScript.JetpackEnabled, the flight inspector's toggle), and switches it
        /// back on only if this code switched it off; that is saved (Drood2Data), so a quickload
        /// while hanging still gives the jetpack back after landing.
        /// </summary>
        private DroodFreeFall _freeFall;

        /// <summary>The jetpack is switched off by this code (hanging or arching; saved).</summary>
        internal bool JetpackSuspended => _saved != null ? _saved.JetpackSuspended : _jetpackSuspendedHere;

        /// <summary>
        /// A new jump (the drop zone's): the jetpack's state is the player's from now on (the
        /// drop zone switched it off): this code no longer gives it back; and the round canopy
        /// may deploy by itself again.
        /// </summary>
        internal void ForgetJetpackSuspension()
        {
            _autoDeployed = false;
            _jetpackSuspendedHere = false;
            if (_saved != null)
            {
                _saved.JetpackSuspended = false;
            }
        }

        private void UpdateJetpack()
        {
            var suspended = _saved != null ? _saved.JetpackSuspended : _jetpackSuspendedHere;
            _freeFall = _freeFall != null ? _freeFall : GetComponent<DroodFreeFall>();
            // Right after a load the arch may still resume: keep a saved suspension until then.
            var arching = _freeFall != null && (_freeFall.Active || (_freeFall.Resuming && suspended));
            if (IsHanging || arching)
            {
                if (_eva.JetpackEnabled)
                {
                    _eva.JetpackEnabled = false;
                    suspended = true;
                    Log(arching ? "Free-fall arch: jetpack off; the body steers." : "Hanging: jetpack off; the steering lines steer.");
                }
            }
            else if (suspended)
            {
                _eva.JetpackEnabled = true;
                suspended = false;
                Log("No longer hanging or arching: jetpack back on.");
            }

            _jetpackSuspendedHere = suspended;
            if (_saved != null)
            {
                _saved.JetpackSuspended = suspended;
            }
        }
    }
}
