using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Relaxed paratrooper pose while Drood 2.0 hangs under its canopy: hands on the risers
    /// at about head height with elbows out to the sides, legs hanging slightly forward
    /// with knees together.
    ///
    /// Two layers:
    /// 1. <see cref="DroodCalmAnimator"/> makes the Animator play its standing idle instead
    ///    of the flailing falling animation.
    /// 2. This component steers the drood's FinalIK FullBodyBipedIK: hand and foot
    ///    effectors, plus bend goals for the elbows and knees. Without bend goals the IK
    ///    picks any elbow/knee direction, twisting the arms and splaying the knees outward.
    ///    The game only uses these for droods in chairs and cockpits, so this writes them only
    ///    while hanging; <see cref="DroodIKOwner"/> keeps the game's values.
    ///
    /// Effectors and bend goals are set in the late chain just before the IK solve, from the
    /// current frame's animated bones (<see cref="Drood2FlightBehaviours.LateUpdate"/>).
    /// </summary>
    public class DroodHangingPose : MonoBehaviour
    {
        // Hands grip the risers this far from the shoulders toward the canopy (a bit below
        // head height), and this far (metres) out to the side, following the risers, which
        // run up and out from the shoulders.
        private const float GripAboveShoulder = 0.3f;
        private const float GripOutward = 0.2f;

        // Hands on the risers: this far (m) from the shoulder along the side lines
        // (DroodParachute.TryGetRiserGrip). The two offsets above are the fallback.
        private const float GripAlongLines = 0.3f;

        // Palms face in, turned this much (fraction) toward the front.
        private const float PalmForward = 0.3f;

        // Front risers (W): gripped this far (m) up the riser from where it leaves the harness
        // and FrontRiserOut outside it (right over the shoulder, the arm would fold across the
        // chest), elbows out and up; the hands move between the toggles and the front risers in
        // FrontGripSeconds.
        private const float FrontRiserGrip = 0.3f;
        private const float FrontRiserOut = 0.02f;

        // ... and FrontRiserForward (m) in front of it, so the forearm stays clear of the helmet.
        private const float FrontRiserForward = 0.18f;
        private const float FrontGripSeconds = 0.3f;
        private const float FrontElbowUp = 0.05f;
        private const float FrontElbowForward = 0.2f;
        private float _frontGrip;

        // Steering toggles held in front of the chest: this far (m) above, in front of and outside
        // the shoulder.
        private const float ToggleUp = 0.15f;
        private const float ToggleForward = 0.25f;
        private const float ToggleOut = 0.1f;

        // Full flare: the hands (fists) come down this far (m) below the shoulder, and this far
        // forward and out, beside the hips; the line runs up from them to the riser, about
        // RiserReach up from the shoulder.
        private const float FlareHandDown = 0.6f;
        private const float FlareHandForward = 0.2f;
        private const float FlareHandOut = 0.12f;
        private const float RiserReach = 0.5f;
        private const float HandWeight = 1f;

        // Elbows point out to the sides, slightly forward and down.
        private const float ElbowOut = 0.7f;
        private const float ElbowForward = 0.15f;
        private const float ElbowDown = 0.1f;

        // Legs hang nearly straight: feet in their natural stance (FeetTogether 0; positive
        // pulls them together until the legs touch), slightly forward and up, knees
        // forward and a little outward.
        private const float FeetTogether = 0f;
        private const float FootRaise = 0.05f;
        private const float FootForward = 0.05f;
        private const float FootWeight = 0.7f;
        private const float KneeForward = 0.6f;
        private const float KneeOut = 0.12f;

        private const float BendWeight = 1f;

        // Idle leg movement while hanging: both legs drift together on one slow noise curve
        // (metres, per second), the right leg LegDelay (s) behind the left, each with
        // LegVariation (fraction) of its own noise mixed in. A foot also lifts LegSwingUp when
        // it swings forward, plus LegSwingUpWander (fraction of LegSwingUp) of noise. The legs
        // swing back against the drood's horizontal acceleration like a pendulum (metres per
        // m/s², capped).
        private const float LegSwingForward = 0.06f;
        private const float LegSwingUp = 0.03f;
        private const float LegSwingUpWander = 0.3f;
        private const float LegSwingSide = 0.02f;
        private const float LegSwingRate = 0.7f;
        private const float LegDelay = 0.25f;
        private const float LegVariation = 0.2f;
        private const float LegLagPerAccel = 0.04f;
        private const float LegLagMax = 0.2f;
        private const float LegLagResponse = 3f;

        // Near the ground (DroodParachute.LegsReady) the legs are brought down for the touchdown:
        // the feet under the hips along the vertical (not the body, which leans back in the
        // flare), LegsReadyForward (m) ahead, the legs ReadyLegReach of their length (knees a
        // little bent); the leg lag fades out (braced legs don't swing with the braking).
        private const float LegsReadyForward = 0.1f;
        private const float ReadyLegReach = 0.93f;

        /// <summary>Blend rate per second into and out of the pose (~0.15 s).</summary>
        private const float BlendResponse = 7f;

        /// <summary>
        /// Seconds to blend the whole skeleton from the last pose before the hanging pose
        /// starts or ends (the animator switches between the calm idle and the falling
        /// animation in one step).
        /// </summary>
        private const float PoseBlendSeconds = 1.2f;

        // ... and into a run-out after a touchdown, the run starting at once, or into a landing
        // clip.
        private const float RunOutBlendSeconds = 0.3f;
        private const float LandingBlendSeconds = 0.25f;
        private const float LandingEndBlendSeconds = 0.4f;
        private bool _wasLandingFall;
        private float _poseBlendSeconds = PoseBlendSeconds;

        private DroodParachute _parachute;
        private DroodRunOut _runOut;
        private FullBodyBipedIK _ik;
        private DroodIKOwner _ikOwner;
        private DroodLandingFall _landingFall;
        private Transform _leftShoulder;
        private Transform _rightShoulder;
        private Transform _leftElbow;
        private Transform _rightElbow;
        private Transform _leftHand;
        private Transform _rightHand;
        private DroodHandGrip _leftGrip;
        private DroodHandGrip _rightGrip;
        private Rigidbody _body;
        private Vector3 _lastVelocity;
        private Vector3 _legLag;

        /// <summary>How strongly the hanging pose holds (0..1), for the log.</summary>
        internal float Weight => _weight;

        private float _noiseSeed;
        private Transform[] _skeleton;
        private Quaternion[] _lastRotations;
        private Vector3[] _lastPositions;
        private Quaternion[] _fromRotations;
        private Vector3[] _fromPositions;
        private bool _hasLastPose;
        private bool _wasCalm;
        private DroodCalmAnimator _calmAnimator;
        private float _poseBlendTime = -1f;
        private Transform _hips;
        private Transform _leftHip;
        private Transform _rightHip;

        // The body held still while hanging (see HoldBody): every bone but the neck and the
        // head (and what hangs from them) keeps the idle's pose of the first fully hanging frame.
        private bool[] _holdBone;
        private Quaternion[] _heldRotations;
        private Vector3[] _heldPositions;
        private bool _hasHeldPose;
        private DroodFreeFall _freeFall;
        private Animator _animator;

        // The held pose is taken once the animator has played the calm idle for
        // HoldAfterCalmSeconds and isn't crossfading: the switch from the falling animation is
        // a crossfade, and a pose taken during it is curled forward.
        private const float HoldAfterCalmSeconds = 0.5f;
        private float _calmSince = -1f;
        private Transform _leftKnee;
        private Transform _rightKnee;
        private Transform _leftFoot;
        private Transform _rightFoot;
        private Transform _leftElbowGoal;
        private Transform _rightElbowGoal;
        private Transform _leftKneeGoal;
        private Transform _rightKneeGoal;
        private float _weight;
        private bool _writing;

        private void Awake()
        {
            _parachute = GetComponent<DroodParachute>();
            _ik = GetComponentInChildren<FullBodyBipedIK>(true);

            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name))
                {
                    bones.Add(t.name, t);
                }
            }

            bones.TryGetValue("LeftShoulder", out _leftShoulder);
            bones.TryGetValue("RightShoulder", out _rightShoulder);
            bones.TryGetValue("LeftElbow", out _leftElbow);
            bones.TryGetValue("RightElbow", out _rightElbow);
            bones.TryGetValue("LeftHand", out _leftHand);
            bones.TryGetValue("RightHand", out _rightHand);
            _noiseSeed = Random.value * 100f;
            _leftGrip = new DroodHandGrip(_leftHand, "Left", bones);
            _rightGrip = new DroodHandGrip(_rightHand, "Right", bones);

            var skin = GetComponentInChildren<SkinnedMeshRenderer>(true);
            _skeleton = skin != null ? skin.bones : new Transform[0];
            _lastRotations = new Quaternion[_skeleton.Length];
            _lastPositions = new Vector3[_skeleton.Length];
            _fromRotations = new Quaternion[_skeleton.Length];
            _fromPositions = new Vector3[_skeleton.Length];
            _holdBone = new bool[_skeleton.Length];
            _heldRotations = new Quaternion[_skeleton.Length];
            _heldPositions = new Vector3[_skeleton.Length];
            bones.TryGetValue("Neck", out var neck);
            for (var i = 0; i < _skeleton.Length; i++)
            {
                var bone = _skeleton[i];
                _holdBone[i] = bone != null && (neck == null || (bone != neck && !bone.IsChildOf(neck)));
            }

            bones.TryGetValue("LeftKnee", out _leftKnee);
            bones.TryGetValue("RightKnee", out _rightKnee);
            bones.TryGetValue("Hips", out _hips);
            bones.TryGetValue("LeftHip", out _leftHip);
            bones.TryGetValue("RightHip", out _rightHip);
            bones.TryGetValue("LeftAnkle", out _leftFoot);
            bones.TryGetValue("RightAnkle", out _rightFoot);

            _leftElbowGoal = CreateGoal("DroodLeftElbowGoal");
            _rightElbowGoal = CreateGoal("DroodRightElbowGoal");
            _leftKneeGoal = CreateGoal("DroodLeftKneeGoal");
            _rightKneeGoal = CreateGoal("DroodRightKneeGoal");

            if (_ik == null)
            {
                Debug.LogWarning("[Drood 2.0] No FullBodyBipedIK on the drood; the hanging pose uses the calm animation only.");
            }
        }

        /// <summary>
        /// Late chain, first, on the animated pose: holds the body still while hanging and in the
        /// free-fall arch, which both build on the calm idle. The idle shifts its weight now and
        /// then, turning the hips and the upper body together by up to ~15°; with the hands held
        /// on the risers the IK would turn the hips back but not the shoulders, twisting the upper
        /// body. So every bone but the neck and the head keeps the idle's pose from the first
        /// frame fully in either pose with the idle settled, squared to the drood's forward (SquareHeldPose) and blended
        /// with the larger of the two poses' weights; the game still moves the head. The poses
        /// and the IK work on top of the held pose.
        /// </summary>
        internal void HoldBody()
        {
            _freeFall = _freeFall != null ? _freeFall : GetComponent<DroodFreeFall>();
            var weight = Mathf.Max(_weight, _freeFall != null ? _freeFall.Weight : 0f);
            if (weight <= 0f || _skeleton.Length == 0)
            {
                _hasHeldPose = false;
                return;
            }

            if (!_hasHeldPose)
            {
                _animator = _animator != null ? _animator : GetComponentInChildren<Animator>();
                // Only from the calm idle itself: a looping state, not in a transition (after a
                // load the animator first plays the end of a jump, a one-off, which would be held
                // as the hanging pose); after HoldFallbackSeconds of calm any state but a jump.
                if (weight < 1f || _calmSince < 0f || Time.time - _calmSince < HoldAfterCalmSeconds
                    || (_animator != null && (_animator.IsInTransition(0) || !IdleLike(Time.time - _calmSince >= HoldFallbackSeconds))))
                {
                    return;
                }

                for (var i = 0; i < _skeleton.Length; i++)
                {
                    if (_holdBone[i])
                    {
                        _heldRotations[i] = _skeleton[i].localRotation;
                        _heldPositions[i] = _skeleton[i].localPosition;
                    }
                }

                SquareHeldPose();
                _hasHeldPose = true;
                if (DroodLog.DebugEnabled && _animator != null)
                {
                    var state = _animator.GetCurrentAnimatorStateInfo(0);
                    var clips = _animator.GetCurrentAnimatorClipInfo(0);
                    DroodLog.Debug($"Held pose captured: animator state {state.fullPathHash} (clip {(clips.Length > 0 ? clips[0].clip.name : "none")}, " +
                        $"at {state.normalizedTime:0.00}), calm for {Time.time - _calmSince:0.0} s, hanging weight {_weight:0.00}.");
                }
                return;
            }

            for (var i = 0; i < _skeleton.Length; i++)
            {
                if (_holdBone[i])
                {
                    var bone = _skeleton[i];
                    bone.localRotation = Quaternion.Slerp(bone.localRotation, _heldRotations[i], weight);
                    bone.localPosition = Vector3.Lerp(bone.localPosition, _heldPositions[i], weight);
                }
            }
        }

        /// <summary>
        /// Turns the held pose about the body's up axis so the hips and the shoulders, on average,
        /// face the drood's forward (the canopy's heading): the idle stands a few degrees turned.
        /// </summary>
        // See HoldBody: after this long calm, any animator state but a jump will do.
        private const float HoldFallbackSeconds = 2f;

        /// <summary>
        /// The animator plays the calm idle: a looping state, or (<paramref name="anyButJump"/>)
        /// any; never a jump's (the landing state the calm animator passes through on its way to
        /// the idle counts as looping: held, it hung the drood crouched).
        /// </summary>
        private bool IdleLike(bool anyButJump)
        {
            _animator.GetCurrentAnimatorClipInfo(0, _clipInfo);
            if (_clipInfo.Count > 0 && _clipInfo[0].clip != null
                && _clipInfo[0].clip.name.IndexOf("Jump", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return anyButJump || _animator.GetCurrentAnimatorStateInfo(0).loop;
        }

        private readonly System.Collections.Generic.List<AnimatorClipInfo> _clipInfo = new System.Collections.Generic.List<AnimatorClipInfo>();

        private void SquareHeldPose()
        {
            if (_hips == null || _hips.parent == null || _leftHip == null || _rightHip == null
                || _leftShoulder == null || _rightShoulder == null)
            {
                return;
            }

            var index = System.Array.IndexOf(_skeleton, _hips);
            if (index < 0 || !_holdBone[index])
            {
                return;
            }

            var up = transform.up;
            var hips = Vector3.SignedAngle(transform.right, Vector3.ProjectOnPlane(_rightHip.position - _leftHip.position, up), up);
            var shoulders = Vector3.SignedAngle(transform.right, Vector3.ProjectOnPlane(_rightShoulder.position - _leftShoulder.position, up), up);
            var turn = -(hips + Mathf.DeltaAngle(hips, shoulders) * 0.5f);
            var squared = Quaternion.AngleAxis(turn, up) * _hips.rotation;
            _heldRotations[index] = Quaternion.Inverse(_hips.parent.rotation) * squared;
        }

        /// <summary>Late chain, before the IK solve: see <see cref="ApplyPose"/>.</summary>
        internal void PreSolve()
        {
            if (_ik != null)
            {
                ApplyPose();
            }
        }

        /// <summary>
        /// The middle of the fist on <paramref name="side"/> (-1 the drood's left, 1 its right),
        /// where the steering line ends, while the hands hold the lines.
        /// </summary>
        internal bool TryGetFist(float side, out Vector3 fist)
        {
            fist = default;
            if (_weight <= 0f || _leftGrip == null || _rightGrip == null || _leftHand == null || _rightHand == null)
            {
                return false;
            }

            var leftIsLeft = Vector3.Dot(_leftHand.position - _rightHand.position, transform.right) < 0f;
            var grip = (side < 0f) == leftIsLeft ? _leftGrip : _rightGrip;
            if (!grip.Valid)
            {
                return false;
            }

            fist = grip.Fist;
            return true;
        }

        /// <summary>Late chain, right after the IK solve, on the final pose of the frame.</summary>
        internal void PostSolve()
        {
            if (_weight > 0f)
            {
                _leftGrip?.Curl(_weight);
                _rightGrip?.Curl(_weight);
            }

            BlendPose();
        }

        /// <summary>
        /// Blends every bone from the pose frozen at the switch toward this frame's pose, then
        /// remembers the result as the latest pose.
        /// </summary>
        private void BlendPose()
        {
            if (_poseBlendTime >= 0f)
            {
                _poseBlendTime += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, _poseBlendTime / _poseBlendSeconds);
                if (t >= 1f)
                {
                    _poseBlendTime = -1f;
                }
                else
                {
                    for (var i = 0; i < _skeleton.Length; i++)
                    {
                        var bone = _skeleton[i];
                        if (bone != null)
                        {
                            bone.localRotation = Quaternion.Slerp(_fromRotations[i], bone.localRotation, t);
                            bone.localPosition = Vector3.Lerp(_fromPositions[i], bone.localPosition, t);
                        }
                    }
                }
            }

            for (var i = 0; i < _skeleton.Length; i++)
            {
                var bone = _skeleton[i];
                if (bone != null)
                {
                    _lastRotations[i] = bone.localRotation;
                    _lastPositions[i] = bone.localPosition;
                }
            }

            _hasLastPose = true;
        }

        private void StartPoseBlend()
        {
            if (!_hasLastPose)
            {
                return;
            }

            System.Array.Copy(_lastRotations, _fromRotations, _skeleton.Length);
            System.Array.Copy(_lastPositions, _fromPositions, _skeleton.Length);
            _poseBlendTime = 0f;
            _runOut = _runOut != null ? _runOut : GetComponent<DroodRunOut>();
            _poseBlendSeconds = _runOut != null && _runOut.Active ? RunOutBlendSeconds : PoseBlendSeconds;
        }

        /// <summary>Called from the modifier's game-loop Update.</summary>
        internal void Step()
        {
            var parachute = _parachute != null ? _parachute : _parachute = GetComponent<DroodParachute>();
            var hanging = parachute != null && parachute.IsHanging;
            var onFrontRisers = parachute != null && parachute.HoldsFrontRisers;
            _frontGrip = Mathf.MoveTowards(_frontGrip, onFrontRisers ? 1f : 0f, Time.deltaTime / FrontGripSeconds);
            _weight = Mathf.MoveTowards(_weight, hanging ? 1f : 0f, BlendResponse * Time.deltaTime);

            // The animator switches between the calm idle and the falling animation in one step
            // (hanging or in the free-fall arch): blend the whole skeleton over it.
            _calmAnimator = _calmAnimator != null ? _calmAnimator : GetComponent<DroodCalmAnimator>();
            var calm = _calmAnimator != null ? _calmAnimator.Calm : hanging;
            if (calm != _wasCalm)
            {
                _wasCalm = calm;
                _calmSince = calm ? Time.time : -1f;
                StartPoseBlend();
            }

            // The landing fall takes over the skeleton: the hanging pose's goals go at once, and the
            // skeleton blends from the last landing pose into the clip over LandingBlendSeconds
            // (so the game's own standing pose doesn't show in between).
            _landingFall = _landingFall != null ? _landingFall : GetComponent<DroodLandingFall>();
            var landingFall = _landingFall;
            var falling = landingFall != null && landingFall.Active;
            if (falling)
            {
                _weight = 0f;
                if (!_wasLandingFall)
                {
                    StartPoseBlend();
                    _poseBlendSeconds = LandingBlendSeconds;
                }
            }
            else if (_wasLandingFall)
            {
                // ... and when it ends, from its last pose into the game's own over
                // LandingEndBlendSeconds (its IK lets go of the hands and feet at once).
                StartPoseBlend();
                _poseBlendSeconds = LandingEndBlendSeconds;
            }

            _wasLandingFall = falling;

            if (_ik == null || _leftShoulder == null || _rightShoulder == null
                || _leftKnee == null || _rightKnee == null || _leftFoot == null || _rightFoot == null)
            {
                return;
            }

            if (_weight <= 0f)
            {
                if (_writing)
                {
                    Release();
                }

                return;
            }
        }

        /// <summary>
        /// Writes the effectors and bend goals. Runs in the late chain after the Animator has
        /// posed the skeleton this frame and right before the solve, so the targets are built
        /// from this frame's bones. (Built in Update from the previous frame's final pose, the leg
        /// targets would stay where the feet had been, and the legs would trail behind whenever
        /// the drood turns.)
        /// </summary>
        private void ApplyPose()
        {
            if (_weight <= 0f || _leftShoulder == null || _rightShoulder == null
                || _leftKnee == null || _rightKnee == null || _leftFoot == null || _rightFoot == null)
            {
                return;
            }

            _ikOwner = _ikOwner != null ? _ikOwner : GetComponent<DroodIKOwner>();
            if (_ikOwner != null)
            {
                _ikOwner.Take();
            }

            var parachute = _parachute;
            _writing = true;
            var solver = _ik.solver;
            var up = transform.up;
            var forward = transform.forward;
            var canopy = parachute != null ? parachute.CanopyDirection : up;

            // The pelvis hangs in the harness: pinned where the pose has it, so the hands pulling
            // down on the lines (the flare) don't drag the body down and fold the legs.
            // DroodIKOwner puts the game's body effector back when the pose lets go.
            solver.bodyEffector.target = null;
            solver.bodyEffector.position = solver.bodyEffector.bone.position;
            solver.bodyEffector.positionWeight = _weight;

            // Arms: hands on the risers, elbows out to each side.
            var shoulderMid = (_leftShoulder.position + _rightShoulder.position) * 0.5f;
            SetArm(solver.leftHandEffector, _leftGrip, solver.leftArmChain, _leftElbowGoal, _leftShoulder.position, shoulderMid, canopy, up, forward);
            SetArm(solver.rightHandEffector, _rightGrip, solver.rightArmChain, _rightElbowGoal, _rightShoulder.position, shoulderMid, canopy, up, forward);

            // Legs: feet slightly forward, knees pointing forward, the legs drifting slowly
            // together (the right one a little later) and swinging back against acceleration
            // (PhysicsStep).
            // The legs don't reach: the game sets the leg chains' reach to 1, which pulls the hip
            // joints toward the foot targets; with the idle's stance (one foot ahead) that turns
            // the pelvis against the shoulders. DroodIKOwner puts the game's reach back when the
            // pose lets go.
            solver.leftLegChain.reach *= 1f - _weight;
            solver.rightLegChain.reach *= 1f - _weight;
            var feetMid = (_leftFoot.position + _rightFoot.position) * 0.5f;
            var right = transform.right;
            var footOffset = up * FootRaise + forward * FootForward + _legLag;
            var leftDrift = LegDrift(0f, 50f, forward, up, right);
            var rightDrift = LegDrift(LegDelay, 80f, forward, up, right);
            var kneesMid = (_leftKnee.position + _rightKnee.position) * 0.5f;
            var ready = parachute != null ? parachute.LegsReady : 0f;
            var down = parachute != null ? parachute.WorldDown : -up;
            SetLeg(solver.leftFootEffector, solver.leftLegChain, _leftKneeGoal,
                Vector3.Lerp(Vector3.Lerp(_leftFoot.position, feetMid, FeetTogether) + footOffset + leftDrift, ReadyFoot(_leftHip, _leftKnee, _leftFoot, down, forward), ready),
                _leftKnee.position + forward * KneeForward + (_leftKnee.position - kneesMid).normalized * KneeOut);
            SetLeg(solver.rightFootEffector, solver.rightLegChain, _rightKneeGoal,
                Vector3.Lerp(Vector3.Lerp(_rightFoot.position, feetMid, FeetTogether) + footOffset + rightDrift, ReadyFoot(_rightHip, _rightKnee, _rightFoot, down, forward), ready),
                _rightKnee.position + forward * KneeForward + (_rightKnee.position - kneesMid).normalized * KneeOut);
        }

        /// <summary>
        /// A foot ready for the ground: under its hip along <paramref name="down"/>,
        /// LegsReadyForward ahead (level), the leg ReadyLegReach of its length.
        /// </summary>
        private static Vector3 ReadyFoot(Transform hip, Transform knee, Transform foot, Vector3 down, Vector3 forward)
        {
            if (hip == null)
            {
                return foot.position;
            }

            var length = Vector3.Distance(hip.position, knee.position) + Vector3.Distance(knee.position, foot.position);
            var level = Vector3.ProjectOnPlane(forward, down);
            level = level.sqrMagnitude > 1e-6f ? level.normalized : forward;
            return hip.position + down * (length * ReadyLegReach) + level * LegsReadyForward;
        }

        /// <summary>
        /// Slow smooth wander for one leg: the shared curve <paramref name="delay"/> (s) late,
        /// with LegVariation of the leg's own noise (lanes from <paramref name="ownLane"/>).
        /// </summary>
        private Vector3 LegDrift(float delay, float ownLane, Vector3 forward, Vector3 up, Vector3 right)
        {
            var t = (Time.time - delay) * LegSwingRate;
            float Noise(float lane) =>
                Mathf.Lerp(
                    Mathf.PerlinNoise(t, _noiseSeed + lane),
                    Mathf.PerlinNoise(t, _noiseSeed + ownLane + lane),
                    LegVariation) * 2f - 1f;
            var swing = Noise(0f);
            // Lift the foot mostly when it swings forward, like a lazy kick.
            return forward * (swing * LegSwingForward)
                + up * (Mathf.Max(swing, 0f) * LegSwingUp + Noise(20f) * LegSwingUp * LegSwingUpWander)
                + right * (Noise(40f) * LegSwingSide);
        }

        /// <summary>
        /// Leg lag, from the modifier's game-loop FixedUpdate: the drood's velocity only
        /// changes per physics step, so its acceleration is measured here (per rendered frame
        /// it alternates between zero and a spike, which makes the legs jitter) and smoothed.
        /// </summary>
        internal void PhysicsStep()
        {
            var dt = Time.fixedDeltaTime;
            if (_body == null)
            {
                _body = GetComponent<Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript>()?.PartScript?.BodyScript?.RigidBody;
                if (_body == null)
                {
                    return;
                }

                _lastVelocity = _body.velocity;
            }

            if (dt <= 0f)
            {
                return;
            }

            var velocity = _body.velocity;
            var accel = (velocity - _lastVelocity) / dt;
            _lastVelocity = velocity;
            var horizontal = Vector3.ProjectOnPlane(accel, transform.up);
            var ready = _parachute != null ? _parachute.LegsReady : 0f;
            var target = Vector3.ClampMagnitude(-horizontal * LegLagPerAccel, LegLagMax) * (1f - ready);
            _legLag = Vector3.Lerp(_legLag, target, 1f - Mathf.Exp(-LegLagResponse * dt));
        }

        private void OnDisable()
        {
            if (_writing)
            {
                Release();
            }
        }

        private void SetArm(IKEffector hand, DroodHandGrip grip, FBIKChain arm, Transform elbowGoal, Vector3 shoulder, Vector3 shoulderMid, Vector3 canopy, Vector3 up, Vector3 forward)
        {
            var outward = (shoulder - shoulderMid).normalized;
            hand.target = null;
            // On the lines, pulled straight down by the steering pull
            // (DroodParachute.PullDistance); the fallback is a fixed offset.
            var pull = _parachute != null ? _parachute.PullDistance(shoulder) : 0f;
            if (grip != null && grip.Valid && _parachute != null
                && _parachute.TryGetRiserGrip(shoulder, GripAlongLines, out var onLine, out var line))
            {
                // A fist around the line, the line running up from it. The steering toggles are
                // held in front of the chest (the steering line comes down to them from the rear
                // riser's guide ring or the canopy); a turn pulls one down, a flare brings both
                // down in front of the body to the hips. With the front risers (W) the hands reach
                // up to those.
                // Steering toggles in front of the chest.
                var toggle = shoulder + up * (ToggleUp - pull) + forward * ToggleForward + outward * ToggleOut;
                var flare = _parachute.FlareFractionAt(shoulder);
                if (flare > 0f)
                {
                    var hip = shoulder - up * FlareHandDown + forward * FlareHandForward + outward * FlareHandOut;
                    toggle = Vector3.Lerp(toggle, hip, flare);
                }

                var lineTop = _parachute.TryGetToggleLineTop(shoulder, out var top) ? top : onLine + line * RiserReach;

                // Front risers (W): the hands reach up to them, just above where they leave the
                // harness on top of the shoulders, and pull them down toward the chest; the hands
                // move there and back smoothly (FrontGripSeconds).
                if (_parachute.TryGetRiserGrip(shoulder, FrontRiserGrip, out var onFront, out var frontLine))
                {
                    var frontHold = onFront + outward * FrontRiserOut + forward * FrontRiserForward - up * pull;
                    var blend = Mathf.SmoothStep(0f, 1f, _frontGrip);
                    toggle = Vector3.Lerp(toggle, frontHold, blend);
                    lineTop = Vector3.Lerp(lineTop, onFront + frontLine * RiserReach, blend);
                }

                var lineUp = (lineTop - toggle).sqrMagnitude > 1e-4f ? (lineTop - toggle).normalized : line;
                var inward = (shoulderMid - shoulder).normalized + forward * PalmForward;
                grip.Target(toggle, lineUp, inward, out var wrist, out var rotation);
                hand.position = wrist;
                hand.rotation = rotation;
                hand.rotationWeight = HandWeight * _weight;
            }
            else
            {
                hand.position = shoulder + canopy * GripAboveShoulder + outward * GripOutward;
                hand.rotationWeight = 0f;
            }

            hand.positionWeight = HandWeight * _weight;

            var elbowUp = Mathf.Lerp(-ElbowDown, FrontElbowUp, Mathf.SmoothStep(0f, 1f, _frontGrip));
            var elbowForward = Mathf.Lerp(ElbowForward, FrontElbowForward, Mathf.SmoothStep(0f, 1f, _frontGrip));
            elbowGoal.position = shoulder + outward * ElbowOut + forward * elbowForward + up * elbowUp;
            arm.bendConstraint.bendGoal = elbowGoal;
            arm.bendConstraint.weight = BendWeight * _weight;
        }

        private void SetLeg(IKEffector foot, FBIKChain leg, Transform kneeGoal, Vector3 footPosition, Vector3 kneeGoalPosition)
        {
            foot.target = null;
            foot.position = footPosition;
            foot.positionWeight = FootWeight * _weight;

            kneeGoal.position = kneeGoalPosition;
            leg.bendConstraint.bendGoal = kneeGoal;
            leg.bendConstraint.weight = BendWeight * _weight;
        }

        private Transform CreateGoal(string goalName)
        {
            var goal = new GameObject(goalName).transform;
            goal.SetParent(transform, false);
            return goal;
        }

        /// <summary>
        /// Stops writing the solver. The game's IK values come back by themselves
        /// (<see cref="DroodIKOwner"/>: restored each frame no pose takes the solver).
        /// </summary>
        private void Release()
        {
            _writing = false;
        }
    }
}
