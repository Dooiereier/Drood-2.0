using UnityEngine;

namespace Drood2
{
    // Ram-air flight from the wing's own attitude.
    //
    // The canopy stays massless on its lines, which swing toward its total force (no faster than a
    // canopy can pitch). The wing sits at a rigging angle to the lines: nose-down, more with the
    // front risers, less with the brakes. Each half of the wing is a section with a lift and drag
    // polar: lift square to the airflow from its effective angle of attack (the angle plus the
    // chord's turn from a pulled trailing edge) up to a maximum, drag along the airflow. Like a
    // sled on a slope, the nose-down wing turns any downward airflow into a forward push, except
    // in a stall (past the maximum too long, see StallReserveSeconds). A pulled steering line adds
    // drag and lift on its side, which yaws the canopy (its own yaw state) and, through the arched
    // halves, pulls it into the turn; the bank follows from the lines tilting to carry the turn.
    public partial class DroodParachute
    {
        // The polar, per half: lift coefficient LiftSlope (per radian) x (effective angle -
        // ZeroLiftAngle, degrees; the camber), up to MaxLift, plus MaxLiftWithPull at the full
        // flare's deflection; the effective angle is the angle of attack plus ChordShift (degrees)
        // at the full flare's deflection, in proportion (the pulled trailing edge turns the chord
        // nose-up); and the pulled trailing edge turning the airflow down adds FlapLift of lift at
        // the full flare's deflection, growing with the deflection's share to the power
        // FlapLiftPower (small pulls little, so corrections at speed don't balloon), whatever the
        // angle (a flap's lift from the speed; it raises the maximum as much). Drag coefficient BaseDrag + InducedDrag x lift²
        // + PullDrag per unit of deflection, plus PastMaxDrag x (sine of the angle past the
        // maximum's)². The stall and
        // its collapse and buffet adjust both (below). With the game's own body drag (~0.43 m²)
        // and the drood's ~130 kg: 3.1:1 at ~15.6 m/s; half brakes from trim stop ~3 m/s of sink
        // within a second; full brakes held stall after ~3.5 s; front risers a quick dive toward ~30 m/s,
        // held ~25 m/s at ~1.4:1 (offline
        // simulation).
        private const float LiftSlope = 3.4f;
        private const float ZeroLiftAngle = -2.7f;
        private const float MaxLift = 1.1f;
        private const float MaxLiftWithPull = 0.3f;
        private const float ChordShift = 3f;
        private const float FlapLift = 0.8f;
        private const float FlapLiftPower = 2f;
        private const float BaseDrag = 0.085f;
        private const float InducedDrag = 0.177f;
        private const float PullDrag = 0.06f;
        private const float PastMaxDrag = 1.1f;

        // Past its maximum (not stalled) the wing mushes: its lift keeps what it had at the
        // maximum's angle instead of dropping with the cosine of the angle of attack, up to
        // LiftPlateauEnd degrees, fading to nothing as the air comes from straight below. A held
        // flare's end then settles at ~3.3 m/s down instead of dipping to ~4.9 m/s first (offline
        // simulation).
        private const float LiftPlateauEnd = 60f;

        // Yaw: a pulled line's drag for the yaw, per unit of pull (see YawDamping).
        private const float YawDragPerPull = 0.08f;

        // The wing's rigging to the lines (degrees nose-down).
        private const float AttitudeRigging = 9f;

        // Pitch stability: the wing pitches nose-up while the airspeed grows and nose-down while
        // it drops, PitchDamping degrees per m/s² (smoothed over PitchDampingSeconds, at most
        // PitchDampingMax), so the speed settles at trim instead of swinging (~11 s period) for
        // half a minute: within ~12 s (offline simulation).
        private const float PitchDamping = 0.8f;
        private const float PitchDampingSeconds = 1f;
        private const float PitchDampingMax = 4f;

        // ... fading in as the wing finishes opening, from this much open.
        private const float PitchDampingFromOpen = 0.95f;

        // Rigging changes (degrees): front risers nose-down (full, held: ~25 m/s at ~1.4:1, ~14 m/s
        // down), brakes and flare nose-up. The flare counts FlareDeflection toward each side's
        // deflection.
        private const float AttitudeFrontPitch = 19f;

        // Pulling the front risers the wing dives ahead of the drood before the pendulum swings
        // its nose back up: up to FrontSurgePitch (degrees) more nose-down for the newly pulled
        // part, easing off over FrontSurgeSeconds while held: a quick dive toward ~30 m/s,
        // settling at the held speed.
        private const float FrontSurgePitch = 20f;
        private const float SurgeMinAttack = 3f;
        private const float SurgeDrawnSeconds = 0.5f;
        private float _surgeDrawn;
        private float _surgePitchApplied;
        private const float FrontSurgeSeconds = 3f;
        private float _frontSurgeHeld;
        private float _frontSurge;

        // In a front riser turn each half pitches by this much (degrees) per unit of its own
        // riser against the wing's.
        private const float FrontTurnPitch = 15f;

        // Turning on the front risers (FrontPullOf): the turn side's riser goes deeper and the
        // outside one comes up, by up to FrontRiserTurnEase of the way; the wing's nose-down
        // follows their average, each half's its own riser. The deeper half loses lift and drops:
        // a roll toward it on the lines (RollLines), FrontTurnRoll of q x area per unit of riser
        // difference pulling the canopy sideways against the drood's weight (a full riser turn at
        // ~25 m/s leans the lines ~50-55°); the heading follows the curving
        // path: the weathercock, and the yaw damping counting the path's own turn
        // (FrontTurnCoordination of it per unit of riser difference), so the wing flies along its
        // heading instead of ~5° beside it. Its lower induced drag yaws it a little the other way
        // (FrontTurnAdverseYaw, drag for the yaw per unit of riser difference).
        private const float FrontRiserTurnEase = 0.6f;
        private const float FrontTurnRoll = 0.5f;
        private const float FrontTurnAdverseYaw = 0.01f;
        private const float FrontTurnCoordination = 1f;

        // The path's turn is smoothed over this long (s; one physics step's change is noisy).
        private const float PathRateSeconds = 0.2f;

        // The brakes' and the flare's nose-up (degrees, with the rigging changes above); the
        // flare's stands for the drood swinging forward under the canopy at the end of a flare,
        // which tips the wing up: the last of the speed then still lifts (offline simulation:
        // from 9 m/s and 1.8 m/s down, the sink falls to ~1 m/s within 3 s instead of ~1.4).
        private const float AttitudeBrakePitch = 2f;
        private const float AttitudeFlarePitch = 8f;

        // Brakes (both lines) count this much extra deflection on each side: the symmetric pull
        // (S) has double the effect, turns (the difference between the lines) are unchanged.
        // Outside the flare the brakes' nose-up and extra deflection count BrakeEffect. Pulled
        // from full flight, full brakes slow the drood with the lines swinging back ~30° (offline
        // simulation).
        private const float BrakeExtraDeflection = 1f;
        private const float BrakeEffect = 0.8f;
        private const float FlareDeflection = 1.5f;

        // The flare's deflection counts this share toward the drag (all of it toward the chord's
        // turn): it bleeds the speed as well as lifting.
        private const float FlareDragShare = 0.7f;

        // A pulled line turns the chord over this long (s, a lag's time constant): the wing
        // takes a moment to change its shape, so a quick pull lifts less before it is let up
        // again and a balloon in the flare builds gradually.
        private const float LiftResponseSeconds = 0.2f;
        private float _leftLiftDeflection;
        private float _rightLiftDeflection;

        // Stall, per half of the wing: with its line pulled at least StallPull and the equivalent
        // airspeed (at SeaLevelDensity) under StallSpeed, which steady flight never reaches (full
        // brakes and the flare held settle at ~5.5-6 m/s, mushing at ~3.3 m/s down; a quick hard pull
        // from fast flight bleeds to ~4.5 m/s, offline simulation), the half stalls over
        // StallOnsetSeconds and goes on stalling while its line stays pulled at least
        // StallRecoverPull, however fast it then falls; it flies again over StallRecoverSeconds once
        // its line is let up below that. Past its maximum lift the airflow still holds for a while:
        // the lift rises up to MaxLiftOvershoot more while a reserve lasts (so a flare gets its lift
        // for a moment; kept while the flare is held), the reserve running out over StallReserveSeconds (at full rate from
        // StallReserveFullRateDegrees past, quicker by one per StallReserveDegrees beyond) and
        // refilling over StallReserveRefillSeconds below it. Stalled, the half keeps StalledLift of
        // its lift, gains StallChordDrag of drag and partly collapses: StallCollapse of its shape,
        // growing to StallDeepCollapse over StallDeepSeconds while the full stall is held (a deep
        // stall), refilling as it recovers. Both halves: the canopy falls back and sinks steeply
        // at a few m/s forward, like a real stalled canopy; let up, it dives and surges before
        // settling.
        // One half (one line deep while that slow): its drag yaws the canopy hard toward it, a
        // spinning turn.
        private const float StallPull = 0.95f;
        private const float StallSpeed = 4.8f;
        private const float MaxLiftOvershoot = 0.2f;
        private const float StallReserveSeconds = 2.5f;
        private const float StallReserveFullRateDegrees = 4f;
        private const float StallReserveDegrees = 10f;
        private const float StallReserveRefillSeconds = 2f;
        private const float SeaLevelDensity = 1.225f;
        private const float StallOnsetSeconds = 0.8f;
        private const float StallRecoverPull = 0.5f;
        private const float StallRecoverSeconds = 1.5f;
        private const float StalledLift = 0.6f;
        private const float StallChordDrag = 0.2f;
        private const float StallCollapse = 0.4f;
        private const float StallDeepCollapse = 0.6f;
        private const float StallDeepSeconds = 5f;

        // A deep stall on both sides is lopsided: one half collapses up to StallAsymmetry more
        // than the other (the other as much less), changing slowly (StallAsymmetryRate per
        // second). The more collapsed half pulls less and drags more (StallCollapseDrag of chord
        // drag per unit of collapse, for the yaw), so the canopy banks and turns toward it, up to
        // ~90°/s.
        private const float StallAsymmetry = 1f;
        private const float StallCollapseDrag = 0.16f;

        // A one-sided stall spins the canopy toward the stalled half, steadily: the stall's extra
        // drag and the line pull together count for the yaw at most as StallYawMaxPulls full
        // line pulls (~115°/s at RamAirSteeringSpeed), and the stalled wing hardly weathercocks (the
        // airflow over it is separated): the weathercock fades to StallWeathercock at full
        // stall, so it doesn't slap the spin back and reverse it.
        private const float StallYawMaxPulls = 2f;
        private const float StallWeathercock = 0.3f;
        private const float StallAsymmetryRate = 0.15f;

        // The collapsed part of a half still holds some air: its force drops by StallForceLoss
        // of the collapse.
        private const float StallForceLoss = 0.5f;

        // Each half of the wing is tilted this much (degrees) outward by the arch.
        private const float HalfWingTilt = 15f;

        // Yaw: a pulled line's extra drag yaws the canopy; the yaw rate responds with
        // YawDamping (1/s at RamAirSteeringSpeed) toward FullPullTurnRate (rad/s) at full pull at
        // that speed (turns grow with the airspeed: ~50°/s at trim). YawWeathercock (1/s² per unit of
        // sideways airflow, about a radian of sideslip, at that speed) turns the heading toward the airflow, like a real canopy's (without it the
        // heading runs away from the flight path in a held full turn and the canopy tumbles). A
        // held full turn is a steady spiral.
        private const float YawDamping = 1.25f;
        private const float YawSlowDamping = 3f;

        // The turn rate grows with the airspeed up to YawMaxSpeedFactor times RamAirSteeringSpeed
        // (a front riser dive at ~25-29 m/s turns well over half again as fast as at trim).
        private const float YawMaxSpeedFactor = 2.2f;
        private const float FullPullTurnRate = 1f;
        private const float YawWeathercock = 8f;
        private const float WeathercockFromOpen = 0.7f;

        // The lines swing toward the force no faster than the canopy can pitch (s); in the flare
        // no faster than FlareLinesLag: the jumper swings forward under the canopy over about a
        // second, so the canopy goes back ~28° braking ~0.45 g, not ~53° at ~0.9 g (offline
        // simulation, with the flare's drag and nose-up below).
        private const float CanopyPitchLag = 0.3f;
        private const float FlareLinesLag = 1f;

        // After touchdown the lines swing back toward the wing's fold on the ground behind the
        // drood at this steady rate (degrees per second): the deflating wing sinks back over a
        // few seconds.
        private const float LandedSwingRate = 12f;

        // ... and a stalled wing, with no air holding it up, topples over that way at up to
        // StalledSwingRate (degrees/s at a full stall).
        private const float StalledSwingRate = 45f;

        // Letting the front risers up raises the diving wing's nose while it is still fast: its
        // extra lift, square to the steep path, points forward and would swing the canopy ~5°
        // further ahead before it comes back over the drood. While they are let up and for
        // FrontReleaseHoldSeconds after, the lines swing forward no faster than
        // FrontReleaseForwardLag (s, the lag's time constant): ~2° (offline simulation).
        private const float FrontReleaseForwardLag = 3.1f;
        private const float FrontLinesLag = 1f;
        private const float FrontPullLinesLag = 0.5f;
        private const float FrontReleaseBackLag = 1.2f;

        // ... and while the pull's surge lasts they swing back no faster than FrontSurgeBackLag
        // (fully from 1 / FrontSurgeBackFull of the surge).
        private const float FrontSurgeBackLag = 2f;
        private const float FrontSurgeBackFull = 4f;
        private const float FrontReleaseHoldSeconds = 1.5f;
        private float _frontRelease;

        // ... and while the wing opens (from free fall the drood still falls steeply as it fills:
        // unheld the lines swung ~55° ahead with a surge to ~30 m/s and a climb; held this much
        // (s, the lag's time constant) ~23° and ~19 m/s, picking up its forward speed as it opens;
        // offline simulation).
        private const float OpeningForwardLag = 1.5f;

        // ... easing out over OpeningLagEaseSeconds once it is full and flying at least
        // OpeningHoldUntilForward (m/s along its heading), at the latest OpeningHoldMaxSeconds
        // after it is full (dropping at once, the lines swung ~30° ahead within a second, a
        // forward jolt of ~1.1 g after a fast opening). Opened from a track, the filling wing
        // bleeds off all the forward speed and only dives once full: easing out then, the lines
        // swung 51° ahead within a second (measured); held until the dive has built the speed,
        // it dives like an opening from a vertical fall.
        private const float OpeningLagEaseSeconds = 2f;
        private const float OpeningHoldUntilForward = 12f;

        // A wing opened from a track flies while it fills: it pitches nose-down toward the
        // relative wind, OpeningPitchShare of the way to OpeningMaxAttack (degrees) at the middle
        // of the wing, so its lift comes in and it keeps part of the forward speed. Rigged square
        // to its lines it met the steep airflow broadside (~85°): pure drag, the forward speed
        // gone by the time it was full, then the dive (offline simulation: from 24 m/s forward at
        // line stretch, 1 m/s at full; with this 20 m/s, never below 9, the lines swinging 17°
        // instead of 32° after it, no zoom). By the forward speed at line stretch (none below
        // OpeningPitchFromForward m/s, all from OpeningPitchFullForward): from a vertical fall
        // the opening stays as it was. Eased out over OpeningPitchEaseSeconds once full.
        private const float OpeningMaxAttack = 40f;
        private const float OpeningPitchShare = 0.3f;
        private const float OpeningPitchFromForward = 5f;
        private const float OpeningPitchFullForward = 25f;
        private const float OpeningPitchEaseSeconds = 2f;
        private float _openingPitchShare;
        private const float OpeningHoldMaxSeconds = 6f;
        private float _openingLagHold;
        private float _openingFullFor;
        private float _lastFrontPull;

        // The air is never quite still: the canopy sways on its lines, up to SwayForeAft degrees
        // fore and aft and SwaySideways to the side, growing in as the wing opens: on each axis
        // two slow waves (SwayRate and SwayRate2 per second, never in step, so it wanders
        // irregularly but averages out: no side is favoured, which a held lean would turn into a
        // turn) and a quicker, smaller one on top (SwayQuickShare of it at SwayQuickRate). The
        // lines swing after it as they do after any change of the wing's pull (sideways as a
        // pendulum, RollLines).
        private const float SwayForeAft = 1.25f;
        private const float SwaySideways = 2f;
        private const float SwayRate = 0.17f;
        private const float SwayRate2 = 0.29f;
        private const float SwayQuickRate = 0.9f;
        private const float SwayQuickShare = 0.3f;

        // Above RamAirSteeringSpeed the lines swing (the canopy banks into a turn) quicker, by up to this
        // fraction at twice that.
        private const float RollRateAtSpeed = 0.3f;

        // The lines' lean to the side has roll momentum (RollLines): the drood swings under the
        // canopy as a pendulum on the lines, pulled back by its apparent weight (the lines' pull:
        // ~1.6 rad/s in straight flight, quicker in a hard turn), with damping ratio
        // LinesRollDamping. Without pull (slack lines) it still counts LinesRollMinAcceleration
        // (m/s²).
        private const float LinesRollDamping = 0.3f;
        private const float LinesRollMinAcceleration = 2.5f;

        // The air brakes the roll, more the faster it rolls: LinesRollDrag (1/rad) x roll rate²,
        // growing with (airspeed / RamAirSteeringSpeed)² like the roll force, so the fastest roll
        // stays about the same at any speed: ~120°/s in a full front riser turn (it can still go
        // over the top when held, but steadily).
        private const float LinesRollDrag = 0.385f;

        // Slow (deep brakes, the flare) the air hardly brakes the roll and the swing couples with
        // the yaw (sideslip, weathercock) into a limit cycle the sway keeps going: ±40° of bank,
        // ±50°/s of yaw in a held full pull (offline simulation). Below RamAirSteeringSpeed the
        // damping ratio grows by up to LinesRollSlowDamping (fading out toward that speed): the
        // swing then stays within ~5°.
        private const float LinesRollSlowDamping = 0.6f;

        private bool _linesRolling;
        private float _linesRollRate;

        private float _attitudePitch = AttitudeRigging;
        private float _lastAirspeed = -1f;
        private float _airspeedChange;
        private float _leftStall;
        private float _leftReserveUsed;
        private float _rightReserveUsed;
        private float _rightStall;
        private float _leftDeepStall;
        private float _rightDeepStall;
        private float _yawRate;
        private Vector3 _lastPath;
        private float _pathRate;
        private float _frontRollForce;

        /// <summary>One axis of the sway (about -1..1, averaging 0), its waves' phases from <paramref name="phase"/>.</summary>
        private static float SwayWave(float phase)
        {
            var t = Time.time * 2f * Mathf.PI;
            return 0.6f * Mathf.Sin(t * SwayRate + phase)
                + 0.4f * Mathf.Sin(t * SwayRate2 + phase * 1.7f)
                + SwayQuickShare * Mathf.Sin(t * SwayQuickRate + phase * 2.9f);
        }

        /// <summary><paramref name="direction"/> turned by the canopy's sway (see SwayForeAft).</summary>
        private Vector3 CanopySway(Vector3 direction)
        {
            var heading = Vector3.ProjectOnPlane(_canopyForward, direction);
            if (heading.sqrMagnitude < 1e-6f)
            {
                return direction;
            }

            heading.Normalize();
            var side = Vector3.Cross(direction, heading);
            var foreAft = SwayWave(FlutterSeed * 0.31f);
            var sideways = SwayWave(FlutterSeed * 0.73f + 2f);
            var opened = Mathf.Clamp01(_inflate);
            return Quaternion.AngleAxis(foreAft * SwayForeAft * opened, side) * Quaternion.AngleAxis(sideways * SwaySideways * opened, heading) * direction;
        }

        /// <summary>One half's stall (0 flying .. 1 stalled), see StallSpeed.</summary>
        private static float StepStall(float stall, float pull, float equivalentAirspeed, float stallSpeed, bool landed, float dt)
        {
            if (landed)
            {
                // Down, a stalled half stays stalled (its shape fades into the fold, StallShown).
                return stall;
            }

            // Once it has begun, it goes on while the line is held: falling, the stalled half
            // soon gets faster again, but held deep it can't fly.
            if ((pull >= StallPull && equivalentAirspeed < stallSpeed) || (stall > 0f && pull >= StallRecoverPull))
            {
                return Mathf.MoveTowards(stall, 1f, dt / StallOnsetSeconds);
            }

            return pull < StallRecoverPull ? Mathf.MoveTowards(stall, 0f, dt / StallRecoverSeconds) : stall;
        }

        /// <summary>
        /// One half's stall reserve used (0..1): it runs out while the half is
        /// <paramref name="past"/> (degrees) its maximum lift's angle, quicker the further past, and
        /// refills below it.
        /// </summary>
        private static float StepReserve(float used, float past, bool landed, bool flaring, float dt)
        {
            if (landed)
            {
                return 0f;
            }

            if (flaring && past > 0f)
            {
                return used;
            }

            return past > 0f
                ? Mathf.Min(1f, used + dt * (Mathf.Min(past / StallReserveFullRateDegrees, 1f) + past / StallReserveDegrees) / StallReserveSeconds)
                : Mathf.Max(0f, used - dt / StallReserveRefillSeconds);
        }

        /// <summary>How long one half's full stall has been held (0..1 over StallDeepSeconds; held down).</summary>
        private static float StepDeepStall(float deep, float stall, float pull, bool landed, float dt) =>
            landed ? deep
            : stall >= 1f && pull >= StallPull
                ? Mathf.MoveTowards(deep, 1f, dt / StallDeepSeconds)
                : Mathf.MoveTowards(deep, 0f, dt / StallRecoverSeconds);

        /// <summary>How far one half has collapsed in its stall (0..StallDeepCollapse).</summary>
        private float StallCollapseOf(int side)
        {
            var stall = side < 0 ? _leftStall : _rightStall;
            var deep = side < 0 ? _leftDeepStall : _rightDeepStall;
            // A deep stall on both sides goes lopsided: one half collapses more than the other,
            // slowly changing sides.
            var lopsided = Mathf.Min(_leftDeepStall, _rightDeepStall) * StallAsymmetry
                * (Mathf.PerlinNoise(_shapeClock * StallAsymmetryRate, FlutterSeed + 83f) * 2f - 1f);
            return Mathf.Clamp01(stall * Mathf.Lerp(StallCollapse, StallDeepCollapse, deep) * (1f + side * lopsided));
        }

        /// <summary>
        /// The nose-down a wing opened from a track takes while it fills (degrees, see
        /// OpeningPitchShare), easing out once it is full.
        /// </summary>
        private float OpeningPitch(float basePitch, Vector3 heading, Vector3 up, Vector3 motion, bool landed)
        {
            if (_openingPitchShare <= 0f || landed)
            {
                return 0f;
            }

            var ease = _inflate < 1f ? 1f : Mathf.Clamp01(1f - _openingFullFor / OpeningPitchEaseSeconds);
            if (ease <= 0f)
            {
                _openingPitchShare = 0f;
                return 0f;
            }

            var rad = basePitch * Mathf.Deg2Rad;
            var chord = heading * Mathf.Cos(rad) - up * Mathf.Sin(rad);
            var normal = up * Mathf.Cos(rad) + heading * Mathf.Sin(rad);
            var attack = Mathf.Atan2(-Vector3.Dot(motion, normal), Vector3.Dot(motion, chord)) * Mathf.Rad2Deg;
            return Mathf.Max(attack - OpeningMaxAttack, 0f) * _openingPitchShare * ease;
        }

        /// <summary>The wing's force from its attitude (see the file comment), in rigidbody units.</summary>
        private Vector3 RamAirAttitudeForce(float density, Vector3 airVelocity, float airspeed, bool landed, float dt)
        {
            var q = 0.5f * density * airspeed * airspeed * ModApi.Constants.MassScale;
            // Nothing until line stretch: before that the canopy is packed in its bag.
            var area = _extend >= 1f
                ? WingArea * Mathf.Pow(_inflate, RamAirOpeningCurve) * (1f - _collapse)
                : 0f;
            var motion = airVelocity / airspeed;
            var up = _canopyDirection;
            var heading = Vector3.ProjectOnPlane(_canopyForward, up);
            if (heading.sqrMagnitude < 1e-6f)
            {
                heading = Vector3.ProjectOnPlane(transform.forward, up);
            }

            heading.Normalize();
            var liftIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RamAirLiftFromOpen, RamAirFullLiftOpen, _inflate));
            // The symmetric pull's own effects (nose-up, extra deflection, drag) at BrakeEffect,
            // the flare's at full.
            var brakes = Mathf.Min(_leftPull, _rightPull) * Mathf.Lerp(BrakeEffect, 1f, _flarePull);
            if (landed || _lastAirspeed < 0f || dt <= 0f)
            {
                _airspeedChange = 0f;
            }
            else
            {
                _airspeedChange = Mathf.Lerp(_airspeedChange, (airspeed - _lastAirspeed) / dt, 1f - Mathf.Exp(-dt / PitchDampingSeconds));
            }

            _lastAirspeed = landed ? -1f : airspeed;
            // Not in the flare (it slows the wing on purpose) and not while the wing opens (its
            // deceleration would pitch it nose-down into a surge).
            var opened = Mathf.InverseLerp(PitchDampingFromOpen, 1f, _inflate);
            var steadying = Mathf.Clamp(PitchDamping * _airspeedChange, -PitchDampingMax, PitchDampingMax) * opened * (1f - _flarePull);
            _frontSurgeHeld = landed ? FrontPullAverage : Mathf.Lerp(_frontSurgeHeld, FrontPullAverage, dt > 0f ? 1f - Mathf.Exp(-dt / FrontSurgeSeconds) : 1f);
            _frontSurge = Mathf.Max(FrontPullAverage - _frontSurgeHeld, 0f);
            var basePitch = WingRigging + AttitudeFrontPitch * FrontPullAverage - AttitudeBrakePitch * brakes - AttitudeFlarePitch * _flarePull - steadying;
            basePitch += OpeningPitch(basePitch, heading, up, motion, landed);

            // The surge never pitches the wing past its flight path: at most down to
            // SurgeMinAttack (degrees) angle of attack at the middle of the wing (a canopy
            // pitched further meets the air from above and tucks).
            var surgePitch = FrontSurgePitch * _frontSurge;
            if (surgePitch > 0f)
            {
                var baseRad = basePitch * Mathf.Deg2Rad;
                var baseChord = heading * Mathf.Cos(baseRad) - up * Mathf.Sin(baseRad);
                var baseNormal = up * Mathf.Cos(baseRad) + heading * Mathf.Sin(baseRad);
                var baseAttack = Mathf.Atan2(-Vector3.Dot(motion, baseNormal), Vector3.Dot(motion, baseChord)) * Mathf.Rad2Deg;
                surgePitch = Mathf.Clamp(surgePitch, 0f, Mathf.Max(baseAttack - SurgeMinAttack, 0f));
            }

            _attitudePitch = basePitch + surgePitch;
            _surgePitchApplied = surgePitch;

            // The applied surge as drawn: smoothed over SurgeDrawnSeconds, so the guard's holding
            // back doesn't show as a wobble.
            _surgeDrawn = Mathf.Lerp(_surgeDrawn, surgePitch, dt > 0f ? 1f - Mathf.Exp(-dt / SurgeDrawnSeconds) : 1f);
            var pitch = _attitudePitch * Mathf.Deg2Rad;

            // The stall goes by the equivalent airspeed (the speed in sea-level air with the same
            // dynamic pressure): in thin air a wing flies faster at the same lift.
            var equivalentAirspeed = airspeed * Mathf.Sqrt(density / SeaLevelDensity);
            _leftStall = StepStall(_leftStall, _leftPull, equivalentAirspeed, StallSpeed * WingSpeedScale, landed, dt);
            _rightStall = StepStall(_rightStall, _rightPull, equivalentAirspeed, StallSpeed * WingSpeedScale, landed, dt);
            StepClosure(landed, dt);
            _leftDeepStall = StepDeepStall(_leftDeepStall, _leftStall, _leftPull, landed, dt);
            _rightDeepStall = StepDeepStall(_rightDeepStall, _rightStall, _rightPull, landed, dt);

            var force = Vector3.zero;
            var yawDrag = 0f;
            var stallYawDrag = 0f;
            for (var side = -1; side <= 1; side += 2)
            {
                var deflection = (side < 0 ? _leftPull : _rightPull) + BrakeExtraDeflection * brakes + FlareDeflection * _flarePull;
                if (landed)
                {
                    deflection = 0f;
                }

                // A positive angle about the heading tilts up to the left, so the right half
                // (side +1) turns the other way.
                var halfUp = Quaternion.AngleAxis(-side * HalfWingTilt, heading) * up;
                // On the front risers each half has its own nose-down (a turn lets the outside
                // one up).
                var halfPitch = pitch + FrontTurnPitch * (FrontPullOf(side) - FrontPullAverage) * Mathf.Deg2Rad;
                var chord = heading * Mathf.Cos(halfPitch) - halfUp * Mathf.Sin(halfPitch);
                var normal = halfUp * Mathf.Cos(halfPitch) + heading * Mathf.Sin(halfPitch);
                var attack = Mathf.Atan2(-Vector3.Dot(motion, normal), Vector3.Dot(motion, chord));

                // The pulled trailing edge turns the chord (following the pull over
                // LiftResponseSeconds): the effective angle, and a higher maximum lift.
                var follow = dt > 0f ? 1f - Mathf.Exp(-dt / LiftResponseSeconds) : 1f;
                float turned;
                if (side < 0)
                {
                    _leftLiftDeflection = Mathf.Lerp(_leftLiftDeflection, deflection, follow);
                    turned = _leftLiftDeflection;
                }
                else
                {
                    _rightLiftDeflection = Mathf.Lerp(_rightLiftDeflection, deflection, follow);
                    turned = _rightLiftDeflection;
                }

                var pullShare = turned / (1f + BrakeExtraDeflection + FlareDeflection);
                var effective = attack + ChordShift * Mathf.Deg2Rad * pullShare;
                var maxLift = MaxLift + MaxLiftWithPull * pullShare;
                var maxAngle = ZeroLiftAngle * Mathf.Deg2Rad + maxLift / LiftSlope;

                // The lift reserve past the maximum, by how far past it; not while the wing opens
                // (falling along the lines it meets the air at ~90°).
                var past = (effective - maxAngle) * Mathf.Rad2Deg;
                float used;
                if (side < 0)
                {
                    _leftReserveUsed = StepReserve(_leftReserveUsed, past, landed || _inflate < 1f, _flarePull > 0f, dt);
                    used = _leftReserveUsed;
                }
                else
                {
                    _rightReserveUsed = StepReserve(_rightReserveUsed, past, landed || _inflate < 1f, _flarePull > 0f, dt);
                    used = _rightReserveUsed;
                }

                // Lift up to the maximum (more while the reserve lasts), only with the air from the
                // front of the wing; past the maximum it mushes (LiftPlateauEnd) unless stalled;
                // stalled, less.
                var stall = side < 0 ? _leftStall : _rightStall;
                var flap = FlapLift * Mathf.Pow(pullShare, FlapLiftPower);
                var maxAttack = maxAngle - ChordShift * Mathf.Deg2Rad * pullShare;
                var facing = Mathf.Max(Mathf.Cos(attack), 0f);
                if (attack > maxAttack)
                {
                    var mush = Mathf.Cos(maxAttack) * Mathf.InverseLerp(90f, LiftPlateauEnd, attack * Mathf.Rad2Deg);
                    facing = Mathf.Lerp(Mathf.Max(mush, facing), facing, stall);
                }

                var lift = Mathf.Clamp(LiftSlope * (effective - ZeroLiftAngle * Mathf.Deg2Rad) + flap, -maxLift, (maxLift + flap) * (1f + MaxLiftOvershoot * (1f - used)))
                    * facing * liftIn * (1f - (1f - StalledLift) * stall);
                var beyond = effective > maxAngle ? Mathf.Sin(effective) - Mathf.Sin(maxAngle) : 0f;
                var dragDeflection = deflection - (1f - FlareDragShare) * FlareDeflection * (landed ? 0f : _flarePull);
                var drag = WingBaseDrag + WingInducedDrag * lift * lift + PullDrag * dragDeflection + PastMaxDrag * beyond * beyond + StallChordDrag * stall;
                var buffet = Buffet(side);
                var open = 1f - StallForceLoss * StallCollapseOf(side);
                var liftDirection = Vector3.ProjectOnPlane(normal, motion);
                liftDirection = liftDirection.sqrMagnitude > 1e-6f ? liftDirection.normalized : Vector3.zero;
                force += (liftDirection * lift - motion * drag) * (0.5f * q * area * buffet * open);
                yawDrag += side * YawDragPerPull * deflection;
                stallYawDrag += side * (StallChordDrag * stall + StallCollapseDrag * StallCollapseOf(side));
            }

            var maxYawDrag = StallYawMaxPulls * YawDragPerPull;
            yawDrag = Mathf.Clamp(yawDrag + stallYawDrag, -maxYawDrag, maxYawDrag);

            // The half-open wing's extra drag, against the motion, fading as the lift comes in.
            force -= motion * (RamAirOpeningDrag * (1f - liftIn) * q * area);

            // The wing flies where it points.
            var right = Vector3.Cross(up, heading).normalized;
            force -= right * (Vector3.Dot(motion, right) * RamAirSideForce * liftIn * q * area);

            // Front riser turn: the roll toward the deeper half (FrontTurnRoll, for RollLines) and
            // its little adverse yaw.
            var frontTurn = FrontPullOf(1) - FrontPullOf(-1);
            _frontRollForce = landed ? 0f : FrontTurnRoll * WingFrontRollScale * frontTurn * liftIn * q * area;
            yawDrag -= FrontTurnAdverseYaw * frontTurn;

            // A wing can't push itself along its flight path (no thrust): any such part goes.
            var along = Vector3.Dot(force, motion);
            if (along > 0f)
            {
                force -= motion * along;
            }

            // Yaw: toward the side with more drag, damped; both scale with the airspeed, so the
            // steady turn rate grows with it. At RamAirSteeringSpeed a full pull gives FullPullTurnRate.
            // Plus the weathercock toward the airflow in the wing's plane: by the airflow's
            // sideways part there (a positive sideslip: the air moves to the right of the
            // heading, which then yaws right), so it is small falling along the lines (opening)
            // and with the air from behind (the opening shock briefly sets the drood back under
            // the wing: by the sideslip's angle, ~180°, that spun the wing round), and only once
            // the wing is nearly open (from WeathercockFromOpen): a filling canopy keeps the
            // heading it opened on.
            var speed = Mathf.Clamp(airspeed / (RamAirSteeringSpeed * WingSpeedScale), 0.1f, YawMaxSpeedFactor);
            var yawGain = FullPullTurnRate * WingToggleTurnScale * YawDamping / YawDragPerPull;
            var sideslip = Vector3.Dot(motion, right);
            var weathercock = YawWeathercock * Mathf.Lerp(1f, StallWeathercock, Mathf.Max(_leftStall, _rightStall))
                * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WeathercockFromOpen, 1f, _inflate));
            // The flight path's turn about the lines (rad/s), for the front riser turn's
            // coordination.
            var path = Vector3.ProjectOnPlane(motion, up);
            var pathRate = 0f;
            if (!landed && dt > 0f && path.sqrMagnitude > 1e-4f && _lastPath.sqrMagnitude > 1e-4f)
            {
                pathRate = Vector3.SignedAngle(_lastPath, path, up) * Mathf.Deg2Rad / dt;
            }

            _lastPath = path;
            _pathRate = Mathf.Lerp(_pathRate, pathRate, 1f - Mathf.Exp(-dt / PathRateSeconds));
            var dampedRate = _yawRate - FrontTurnCoordination * Mathf.Abs(frontTurn) * _pathRate;
            var yawAccel = (yawGain * yawDrag * speed * speed - YawDamping * speed * dampedRate + weathercock * sideslip * speed * speed) * liftIn;

            // Slow (the flare, deep brakes) the damping above is weak and a nudge (the sway, the
            // swing) sets the heading wandering: below RamAirSteeringSpeed the turn rate is also
            // pulled toward the one the lines ask for (YawSlowDamping per s, fading out toward
            // that speed), so a turn keeps its rate but the wandering dies out.
            var commanded = yawGain * yawDrag * speed / YawDamping;
            yawAccel -= YawSlowDamping * Mathf.Clamp01(1f - speed) * (_yawRate - commanded) * liftIn;
            _yawRate += yawAccel * dt;
            if (landed)
            {
                _yawRate = 0f;
            }

            _turnRate = _yawRate;
            _canopyForward = (Quaternion.AngleAxis(_yawRate * Mathf.Rad2Deg * dt, up) * heading).normalized;
            return force;
        }
    }
}
