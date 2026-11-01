using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Soft touchdown under a canopy: the drood keeps its speed over the ground along its
    /// facing and runs to a stop, slowing down evenly.
    ///
    /// The game's own run animation does the running: EvaScript feeds the Animator the drood's
    /// actual forward speed (UpdateAnimationController), so keeping the body moving is enough.
    /// Its forward input comes from the movement keys, though, so it's set here too (public
    /// CrewAnimController setter, after EvaScript's game-loop Update, like
    /// <see cref="DroodCalmAnimator"/>), so the Animator shows a running stride. The speed is
    /// set each physics step after EvaScript's, which would otherwise brake the drood
    /// (SlowDownCharacter). Any movement key pressed after touchdown hands control straight
    /// back to the player.
    /// </summary>
    public class DroodRunOut : MonoBehaviour
    {
        /// <summary>
        /// Over the ground faster than this (m/s), a touchdown is a fall (the hard-landing
        /// clip). A sport canopy lands at 9-14 m/s after a good flare, running or sliding on its
        /// feet; a lower limit makes such landings hard.
        /// </summary>
        public const float MaxSpeed = 12f;

        // Slower than this (m/s) there's nothing to run out.
        private const float MinSpeed = 1f;

        // Even deceleration (m/s²): at least MinDeceleration, and fast enough that the run-out
        // takes at most MaxSeconds (sliding to a stop from high speed).
        private const float MinDeceleration = 3f;
        private const float MaxSeconds = 2f;

        private float _deceleration;

        // Speed (m/s) that counts as a full forward input for the Animator.
        private const float FullInputSpeed = 4f;

        private EvaScript _eva;
        private Vector3 _direction;
        private float _speed;

        // For LandingGraceSeconds after a touchdown the movement keys don't count: the drood
        // doesn't walk (FreezeMovementDuringLandingFallPatch), the keys don't end the run-out,
        // and the heading is held. Otherwise a key still held from the air (the flare's back
        // key) walks the drood, and the game turns a grounded drood that gets forward/back
        // input toward the camera (EvaScript.UpdateKinematicTurning, "astronaut follow camera").
        private const float LandingGraceSeconds = 0.75f;
        private float _touchdownTime = float.NegativeInfinity;
        private Quaternion _heading;

        // For the first NoBounceSeconds of the run-out the drood can't move up (the contact
        // would bounce it off its feet).
        private const float NoBounceSeconds = 0.4f;
        private float _startTime;

        // The run-out ends once the drood has been off the ground this long (s).
        private const float AirborneEndsSeconds = 0.25f;
        private float _lastGroundedTime;

        // A surface this close ahead (m, beyond this step's travel) whose normal points up less
        // than MaxWallUp (cosine, ~60° from up) ends the run-out.
        private const float WallMargin = 0.05f;
        private const float MaxWallUp = 0.5f;

        public bool Active { get; private set; }

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
        }

        /// <summary>
        /// Any touchdown under a canopy (hard or soft): the movement keys don't count for
        /// LandingGraceSeconds (see <see cref="Settling"/>).
        /// </summary>
        public void Touchdown()
        {
            _touchdownTime = Time.time;
            _heading = transform.rotation;
        }

        /// <summary>Just touched down: the movement keys don't count yet (no walking).</summary>
        public bool Settling => Time.time - _touchdownTime < LandingGraceSeconds && _eva != null && _eva.EvaActive;

        /// <summary>Starts the run-out with the ground velocity just before touchdown.</summary>
        public void Begin(Vector3 groundVelocity)
        {
            // Level with the ground (gravity's up, not the drood's: after a flare the body leans
            // back, and its own forward points up).
            var forward = Vector3.ProjectOnPlane(transform.forward, GravityUp()).normalized;
            var speed = Vector3.Dot(groundVelocity, forward);
            if (speed < MinSpeed || forward.sqrMagnitude < 0.5f)
            {
                return;
            }

            _direction = forward;
            _speed = speed;
            _deceleration = Mathf.Max(MinDeceleration, speed / MaxSeconds);
            _startTime = Time.time;
            _lastGroundedTime = Time.time;
            Active = true;
            DroodLog.Debug($"Soft touchdown: running out from {speed:0.0} m/s.");
        }

        /// <summary>
        /// Game-loop FixedUpdate, after EvaScript's: holds the heading after touchdown (the last
        /// MoveRotation of the step wins over EvaScript's) and the slowing run.
        /// </summary>
        internal void PhysicsStep()
        {
            var body = _eva.PartScript?.BodyScript?.RigidBody;
            var controls = _eva.PartScript?.CommandPod?.Controls;
            var input = controls != null
                && (Mathf.Abs(controls.EvaMoveFwdAft) > 0.1f || Mathf.Abs(controls.EvaStrafe) > 0.1f || Mathf.Abs(controls.EvaTurn) > 0.1f);
            if (Settling && body != null)
            {
                // Keep the heading, with the game's current up (it keeps the drood upright).
                var bodyUp = body.rotation * Vector3.up;
                var heading = Vector3.ProjectOnPlane(_heading * Vector3.forward, bodyUp);
                if (heading.sqrMagnitude > 1e-4f)
                {
                    body.MoveRotation(Quaternion.LookRotation(heading, bodyUp));
                }

                // The keys aren't the player taking over the run-out yet.
                input = false;
            }

            if (!Active)
            {
                return;
            }

            _speed = Mathf.MoveTowards(_speed, 0f, _deceleration * Time.fixedDeltaTime);
            if (_eva.IsGrounded)
            {
                _lastGroundedTime = Time.time;
            }

            // Off the ground for a moment: there's nothing to run on.
            var airborne = Time.time - _lastGroundedTime > AirborneEndsSeconds;
            if (body == null || input || airborne || !_eva.EvaActive || _eva.IsInWater || _speed <= 0f)
            {
                Active = false;
                return;
            }

            // Running into something (a wall, a craft): stop there instead of pushing on into it.
            // Surfaces that face up enough to run up (slopes, steps) don't count.
            var reach = _speed * Time.fixedDeltaTime + WallMargin;
            if (body.SweepTest(_direction, out var hit, reach, QueryTriggerInteraction.Ignore)
                && Vector3.Dot(hit.normal, GravityUp()) < MaxWallUp)
            {
                Active = false;
                DroodLog.Debug($"Run-out stopped by {hit.collider.name} at {_speed:0.0} m/s.");
                return;
            }

            // Along the run direction only; the vertical speed (steps, slopes) stays the game's.
            var velocity = body.velocity;
            var up = GravityUp();
            var vertical = Vector3.Dot(velocity, up);
            if (Time.time - _startTime < NoBounceSeconds)
            {
                vertical = Mathf.Min(vertical, 0f);
            }

            body.velocity = up * vertical + _direction * _speed;
        }

        /// <summary>Up against gravity (the drood's own up if there's no craft yet).</summary>
        private Vector3 GravityUp()
        {
            var craft = _eva != null ? _eva.PartScript?.CraftScript : null;
            return craft != null ? -(Vector3)craft.FlightData.GravityFrameNormalized : transform.up;
        }

        /// <summary>Game-loop Update, after EvaScript's: a running stride in the Animator.</summary>
        internal void AnimatorStep()
        {
            if (!Active)
            {
                return;
            }

            var controller = DroodReflection.CrewAnimController(_eva);
            if (controller != null)
            {
                controller.ForwardInput = Mathf.Clamp01(_speed / FullInputSpeed);
            }
        }
    }
}
