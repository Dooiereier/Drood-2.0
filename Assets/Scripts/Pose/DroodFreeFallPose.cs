using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The free-fall arch pose (<see cref="DroodFreeFall"/>), belly to earth: the body flat to
    /// the relative wind with a straight back, tipped slightly head-up about the hips, the head
    /// a little raised; upper arms out to the sides, elbows bent with the forearms forward and
    /// the hands at about head height, palms to the wind; the thighs a little up from the hips,
    /// the lower legs rising steeply behind the knees, the feet pointed with the soles to the
    /// sky. The skydiver's corrections show in the limbs: pitch moves the arms and legs along
    /// the body, roll gives the side that must come up more drag (arm wider, leg extended, hand
    /// flat), yaw turns both hands.
    ///
    /// Like <see cref="DroodHangingPose"/>: on the calm animator pose
    /// (<see cref="DroodCalmAnimator"/>), the pelvis turned before the IK solve, then FinalIK
    /// hand and foot effectors with elbow and knee bend goals, set in the late chain just before
    /// the solve, and the head turned after it. The two poses never run together (the arch ends
    /// at line stretch, the hanging pose starts there).
    /// </summary>
    public class DroodFreeFallPose : MonoBehaviour
    {
        // The neck and head lift the face this much (degrees, half each).
        private const float HeadLift = 19.5f;
        private float _headLiftApplied;

        /// <summary>The head lift drawn this frame (degrees, about the drood's right axis, face up), 0 out of the arch.</summary>
        internal float HeadLiftApplied => _headLiftApplied;

        /// <summary>The neck bone the head lift turns about (null if missing).</summary>
        internal Transform Neck => _neck;

        // The whole body tipped head-up about the hips by this much (degrees), on the pelvis
        // bone before the solve: the upper body comes up, the feet keep their IK targets.
        // Visual only (the physics aims the body, not the pose).
        private const float PelvisPitch = 3f;

        // Arms: the hand HandOut (m) out from the shoulder and HandTowardHead toward the head,
        // HandRaised behind the body plane; the elbow bend goal ElbowOut out and ElbowRaised
        // behind it, so the upper arm goes out to the side and the forearm forward.
        private const float HandOut = 0.22f;
        private const float HandTowardHead = 0.28f;
        private const float HandRaised = 0.1f;
        private const float ElbowOut = 0.6f;
        private const float ElbowRaised = 0.1f;

        // Legs (angles above the body line, rising toward the back; the drood's own bone
        // lengths): thighs ThighUp degrees up from the hips, the knees KneeApart (m) out; shins
        // ShinUp degrees up, the feet FeetApart (m) further out than the knees; knees pointing
        // to the belly side (the bend goal KneeTowardBelly from the knee).
        private const float ThighUp = 26f;
        private const float ShinUp = 65f;
        private const float KneeApart = 0.06f;
        private const float FeetApart = 0.2f;
        private const float KneeTowardBelly = 0.4f;
        private const float FootRotationWeight = 0.8f;
        private const float FootWeight = 0.9f;
        private const float HandWeight = 1f;
        private const float BendWeight = 1f;

        // The corrections at full command (m), as a skydiver makes them (the air comes from the
        // belly side, drag pushes toward the back):
        // - pitch: head down by extending the legs and taking the arms back (more drag at the
        //   feet end), head up the other way;
        // - roll: the side that must come up gets more drag: that arm goes wider, the other in;
        // - yaw (a steered turn and the corrections alike): both hands turn slightly about their
        //   fingers, the same way, like the blades of a propeller, deflecting the air around the
        //   belly axis (up to HandTwist degrees). The arms stay where they are.
        // (Arms moved along the body against each other make no yaw force: drag acts along the
        // belly axis.)
        private const float RollReach = 0.2f;

        // Roll (bank) corrections beyond the arm spread: on the side that must come up the leg
        // extends (shin up to RollShin degrees flatter) and the hand lies flat to the wind; on
        // the other side the leg tucks as much and the hand turns up to RollSpill degrees
        // edge-on, spilling air.
        // The legs also move out (that side) and in (the other) by up to RollLegOut (m), knees
        // and feet together, like the arms.
        private const float RollShin = 15f;
        private const float RollSpill = 30f;
        private const float RollLegOut = 0.12f;

        // The roll corrections are shown this many times larger than they fly (the arch is so
        // steady in roll that they stay a few percent of the limbs' reach); capped at full.
        private const float RollShown = 4f;
        private const float PitchReach = 0.15f;
        private const float HandTwist = 25f;

        // Open hands, palms to the wind, fingers forward (toward the head, along the forearm), a
        // little out; in a track the fingers point back along the body.
        private const float FingersOut = 0.15f;
        private const float HandRotationWeight = 0.8f;

        // Steering (DroodFreeFall.Track): in a track the arms swing back along an arc around the
        // shoulders (out to the side, then back) to beside the hips (TrackArmAngle degrees from
        // straight out toward the feet, TrackArmReach m), the elbows raised (TrackElbowRaised
        // m), the hand turned less firmly (TrackHandRotation of the weight, TrackTwist of the
        // turn twist: swept back, palms to the wind need an extreme forearm twist and the IK
        // can flip between two ways of making it); the legs straighten (thighs and shins
        // TrackLegUp degrees up). In a backslide the arms reach toward the head and the knees
        // tuck (thighs BackslideThighUp, shins BackslideShinUp). The pitch corrections move the
        // shins by up to PitchShin degrees.
        private const float TrackArmAngle = 68f;
        private const float TrackArmReach = 0.54f;
        private const float TrackElbowRaised = 0.35f;
        private const float TrackHandRotation = 0.35f;
        private const float TrackTwist = 0.4f;

        // The corrections in a track, along the arms' arc around the shoulders: head up (pitch)
        // swings both arms forward and out by up to TrackPitchArmForward degrees; a roll swings
        // the arm on the side that must come up out by up to TrackRollArmOut degrees (more lift
        // that side). Head down keeps the arms where they are. A steady track needs only a few
        // percent of the limbs' reach, so the commands are shown TrackPitchShown and
        // TrackRollShown times larger (capped at full).
        private const float TrackPitchArmForward = 20f;
        private const float TrackPitchShown = 10f;
        private const float TrackRollArmOut = 15f;
        private const float TrackRollShown = 5f;

        // A turn in a track: the turn-side hand dips this far (m) toward the wind and comes this
        // far in; the other hand goes the other way.
        private const float CarveDip = 0.12f;
        private const float CarveIn = 0.08f;
        private const float BackslideHandReach = 0.25f;
        private const float TrackLegUp = 8f;
        private const float BackslideThighUp = 48f;
        private const float BackslideShinUp = 95f;
        private const float PitchShin = 20f;

        private DroodFreeFall _freeFall;
        private FullBodyBipedIK _ik;
        private DroodIKOwner _ikOwner;
        private Transform _neck;
        private Transform _pelvis;
        private Transform _head;
        private Transform _leftShoulder;
        private Transform _rightShoulder;
        private Transform _leftHip;
        private Transform _rightHip;
        private Transform _leftKnee;
        private Transform _rightKnee;
        private Transform _leftAnkle;
        private Transform _rightAnkle;
        private Transform _leftToes;
        private Transform _rightToes;
        private DroodHandGrip _leftGrip;
        private DroodHandGrip _rightGrip;
        private Transform _leftElbowGoal;
        private Transform _rightElbowGoal;

        // Each arm's length (shoulder to elbow to hand, m); a hand target stays within
        // MaxReachShare of it, so the IK reaches it with the arm alone instead of dragging the
        // shoulder after it (a track turn's swept-back hand bent and twisted the chest).
        private const float MaxReachShare = 0.97f;
        private float _leftArmLength;
        private float _rightArmLength;
        private Transform _leftKneeGoal;
        private Transform _rightKneeGoal;
        private bool _writing;

        private void Awake()
        {
            _freeFall = GetComponent<DroodFreeFall>();
            _ik = GetComponentInChildren<FullBodyBipedIK>(true);
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(t.name))
                {
                    bones.Add(t.name, t);
                }
            }

            bones.TryGetValue("Neck", out _neck);
            bones.TryGetValue("Hips", out _pelvis);
            bones.TryGetValue("Head", out _head);
            bones.TryGetValue("LeftShoulder", out _leftShoulder);
            bones.TryGetValue("RightShoulder", out _rightShoulder);
            bones.TryGetValue("LeftHip", out _leftHip);
            bones.TryGetValue("RightHip", out _rightHip);
            bones.TryGetValue("LeftKnee", out _leftKnee);
            bones.TryGetValue("RightKnee", out _rightKnee);
            bones.TryGetValue("LeftAnkle", out _leftAnkle);
            bones.TryGetValue("RightAnkle", out _rightAnkle);
            bones.TryGetValue("LeftToes", out _leftToes);
            bones.TryGetValue("RightToes", out _rightToes);
            _leftArmLength = ArmLength(bones, "Left");
            _rightArmLength = ArmLength(bones, "Right");
            if (bones.TryGetValue("LeftHand", out var leftHand))
            {
                _leftGrip = new DroodHandGrip(leftHand, "Left", bones);
            }

            if (bones.TryGetValue("RightHand", out var rightHand))
            {
                _rightGrip = new DroodHandGrip(rightHand, "Right", bones);
            }
            _leftElbowGoal = CreateGoal("DroodFreeFallLeftElbowGoal");
            _rightElbowGoal = CreateGoal("DroodFreeFallRightElbowGoal");
            _leftKneeGoal = CreateGoal("DroodFreeFallLeftKneeGoal");
            _rightKneeGoal = CreateGoal("DroodFreeFallRightKneeGoal");
            DroodLog.Debug($"Free-fall pose: pelvis {(_pelvis != null ? "found" : "missing")}, neck {(_neck != null ? "found" : "missing")}, head {(_head != null ? "found" : "missing")}.");
        }

        /// <summary>Late chain, before the IK solve.</summary>
        internal void PreSolve()
        {
            var weight = _freeFall != null ? _freeFall.Weight : 0f;
            if (weight <= 0f || _ik == null || _leftShoulder == null || _rightShoulder == null
                || _leftHip == null || _rightHip == null || _leftKnee == null || _rightKnee == null)
            {
                if (_writing)
                {
                    Release();
                }

                return;
            }

            _ikOwner = _ikOwner != null ? _ikOwner : GetComponent<DroodIKOwner>();
            if (_ikOwner != null)
            {
                _ikOwner.Take();
            }

            _writing = true;
            var right = transform.right;
            var head = transform.up;
            var belly = transform.forward;
            var command = _freeFall.LimbCommand;
            var track = _freeFall.Track;

            // A turn in a track: while the bank builds (DroodFreeFall.RollDemand, like ailerons)
            // the turn-side arm dips toward the wind and comes in, the other rises; held at the
            // bank they come back, rolling level again they go the other way.
            var carve = _freeFall.RollDemand * Mathf.Clamp01(track);

            // Head-up about the hips: about the drood's right axis (a positive angle would tip
            // the head toward the belly).
            if (_pelvis != null)
            {
                _pelvis.rotation = Quaternion.AngleAxis(-PelvisPitch * weight, right) * _pelvis.rotation;
            }

            var solver = _ik.solver;

            // The legs don't reach: the game sets the leg chains' reach to 1, which pulls the hip
            // joint toward the foot target; with the feet raised that lifts the hips and kinks
            // the lower back. DroodIKOwner puts the game's reach back when the arch lets go.
            solver.leftLegChain.reach *= 1f - weight;
            solver.rightLegChain.reach *= 1f - weight;

            // The arms neither pull the body nor reach: a hand swung out or in (a turn in a track,
            // a roll correction) drew its shoulder after it, bending the chest up to ~11° toward
            // the outstretched arm and twisting it ~8° (measured after the IK solve), the head
            // moving with it. DroodIKOwner puts the game's values back when the arch lets go.
            solver.leftArmChain.pull *= 1f - weight;
            solver.rightArmChain.pull *= 1f - weight;
            solver.leftArmChain.reach *= 1f - weight;
            solver.rightArmChain.reach *= 1f - weight;

            var shoulderMid = (_leftShoulder.position + _rightShoulder.position) * 0.5f;
            var twist = HandTwist * command.z;
            var roll = Mathf.Clamp(command.y * RollShown, -1f, 1f);
            // A positive roll command lifts the right side: the right arm and leg out, the leg
            // extended and the hand flat; the left arm and leg in, the leg tucked, the hand
            // edge-on.
            SetArm(solver.leftHandEffector, solver.leftArmChain, _leftElbowGoal, _leftGrip, _leftShoulder.position, shoulderMid, head, belly,
                -command.x * PitchReach, -roll * RollReach, twist, Mathf.Max(roll, 0f), -carve, track, weight, _leftArmLength);
            SetArm(solver.rightHandEffector, solver.rightArmChain, _rightElbowGoal, _rightGrip, _rightShoulder.position, shoulderMid, head, belly,
                -command.x * PitchReach, roll * RollReach, twist, Mathf.Max(-roll, 0f), carve, track, weight, _rightArmLength);

            var hipMid = (_leftHip.position + _rightHip.position) * 0.5f;
            SetLeg(solver.leftFootEffector, solver.leftLegChain, _leftKneeGoal, _leftHip, _leftKnee, _leftAnkle, _leftToes, hipMid, head, belly, command.x, -roll, track, weight);
            SetLeg(solver.rightFootEffector, solver.rightLegChain, _rightKneeGoal, _rightHip, _rightKnee, _rightAnkle, _rightToes, hipMid, head, belly, command.x, roll, track, weight);
        }

        private void SetArm(IKEffector hand, FBIKChain arm, Transform elbowGoal, DroodHandGrip grip, Vector3 shoulder, Vector3 shoulderMid,
            Vector3 head, Vector3 belly, float towardHead, float spread, float twist, float spill, float carve, float track, float weight, float armLength)
        {
            var outward = (shoulder - shoulderMid).normalized;
            hand.target = null;

            // The hand's place around the shoulder, as an angle from straight out toward the head
            // (negative: toward the feet) and a reach; a track swings it along that arc.
            var archOut = HandOut + spread;
            var archForward = HandTowardHead + towardHead;
            var angle = Mathf.Atan2(archForward, archOut) * Mathf.Rad2Deg;
            var reach = Mathf.Sqrt(archOut * archOut + archForward * archForward);
            var raised = HandRaised;
            var swept = track > 0f ? Mathf.SmoothStep(0f, 1f, track) : 0f;
            var trackCorrection = swept * (
                TrackPitchArmForward * Mathf.Clamp01(towardHead / PitchReach * TrackPitchShown)
                + TrackRollArmOut * Mathf.Clamp01(spread / RollReach * TrackRollShown));
            angle = Mathf.Lerp(angle, -TrackArmAngle, swept) + trackCorrection;
            reach = Mathf.Lerp(reach, TrackArmReach, swept);
            raised = Mathf.Lerp(raised, 0.05f, swept);
            Vector3 AroundShoulder(float degrees) =>
                outward * Mathf.Cos(degrees * Mathf.Deg2Rad) + head * Mathf.Sin(degrees * Mathf.Deg2Rad);
            var position = shoulder + AroundShoulder(angle) * reach - belly * raised
                + (belly * CarveDip - outward * CarveIn) * carve;
            if (track < 0f)
            {
                position += head * (BackslideHandReach * -track);
            }

            if (armLength > 0f)
            {
                position = shoulder + Vector3.ClampMagnitude(position - shoulder, armLength * MaxReachShare);
            }

            hand.position = position;
            hand.positionWeight = HandWeight * weight;

            // Open hand, palm to the wind, turned about its fingers by the yaw (the same angle
            // for both hands about their own fingers: a propeller). The fingers swing with the
            // arm along the same arc (through straight out: no flip), always square to the belly
            // axis, so the palm direction is always well defined.
            if (grip != null && grip.Valid)
            {
                var fingersAngle = Mathf.Lerp(Mathf.Atan2(1f, FingersOut) * Mathf.Rad2Deg, -TrackArmAngle - 5f, swept) + trackCorrection;
                var fingers = AroundShoulder(fingersAngle);
                var turn = (twist * Mathf.Lerp(1f, TrackTwist, swept)) + RollSpill * Mathf.Clamp01(spill);
                hand.rotation = grip.Orient(fingers, Quaternion.AngleAxis(turn, fingers) * belly);
                hand.rotationWeight = HandRotationWeight * Mathf.Lerp(1f, TrackHandRotation, swept) * weight;
            }
            else
            {
                hand.rotationWeight = 0f;
            }
            elbowGoal.position = shoulder + outward * ElbowOut - belly * Mathf.Lerp(ElbowRaised, TrackElbowRaised, swept);
            arm.bendConstraint.bendGoal = elbowGoal;
            arm.bendConstraint.weight = BendWeight * weight;
        }

        /// <summary>
        /// One leg from the hip: thigh and shin at their angles up from the body line (rising
        /// toward the back, away from the belly), with the drood's own bone lengths, splayed out;
        /// the foot pointed, sole to the sky. A positive pitch command
        /// (head down) extends the leg (flatter shin: more drag at the feet end).
        /// </summary>
        private void SetLeg(IKEffector foot, FBIKChain leg, Transform kneeGoal, Transform hipBone, Transform kneeBone, Transform ankleBone,
            Transform toesBone, Vector3 hipMid, Vector3 head, Vector3 belly, float pitch, float roll, float track, float weight)
        {
            var hip = hipBone.position;
            var outward = (hip - hipMid).normalized;
            var thighLength = Vector3.Distance(hip, kneeBone.position);
            var shinLength = ankleBone != null ? Vector3.Distance(kneeBone.position, ankleBone.position) : thighLength;

            var thighUp = ThighUp;
            var shinUp = ShinUp - PitchShin * Mathf.Clamp(pitch, -1f, 1f) - RollShin * Mathf.Clamp(roll, -1f, 1f);
            if (track > 0f)
            {
                var t = Mathf.SmoothStep(0f, 1f, track);
                thighUp = Mathf.Lerp(thighUp, TrackLegUp, t);
                shinUp = Mathf.Lerp(shinUp, TrackLegUp, t);
            }
            else if (track < 0f)
            {
                thighUp = Mathf.Lerp(thighUp, BackslideThighUp, -track);
                shinUp = Mathf.Lerp(shinUp, BackslideShinUp, -track);
            }

            // Up from the body line toward the back: along -head (toward the feet), rising to -belly.
            Vector3 Rising(float degrees) =>
                -head * Mathf.Cos(degrees * Mathf.Deg2Rad) - belly * Mathf.Sin(degrees * Mathf.Deg2Rad);
            var legOut = outward * (RollLegOut * Mathf.Clamp(roll, -1f, 1f));
            var knee = hip + Rising(thighUp) * thighLength + outward * KneeApart + legOut;
            var shin = (Rising(shinUp) * shinLength + outward * FeetApart).normalized;
            var ankle = knee + shin * shinLength;

            foot.target = null;
            foot.position = ankle;
            foot.positionWeight = FootWeight * weight;
            kneeGoal.position = knee + belly * KneeTowardBelly + outward * 0.1f;
            leg.bendConstraint.bendGoal = kneeGoal;
            leg.bendConstraint.weight = BendWeight * weight;

            // The foot pointed, the sole to the sky whatever the lower leg's angle: the toes
            // straight back along the body, the top of the foot to the belly side, the heel up
            // (at 65° lower legs an ankle of ~115°, a natural pointed foot). From the animated
            // standing pose, where the toes point forward and the top of the foot up.
            if (ankleBone != null && toesBone != null)
            {
                var standingToes = Vector3.ProjectOnPlane(toesBone.position - ankleBone.position, transform.up);
                if (standingToes.sqrMagnitude > 1e-6f)
                {
                    var turn = Quaternion.LookRotation(-head, belly) * Quaternion.Inverse(Quaternion.LookRotation(standingToes.normalized, transform.up));
                    foot.rotation = turn * ankleBone.rotation;
                    foot.rotationWeight = FootRotationWeight * weight;
                }
                else
                {
                    foot.rotationWeight = 0f;
                }
            }
            else
            {
                foot.rotationWeight = 0f;
            }
        }

        /// <summary>Shoulder to elbow to hand (m) for one side ("Left" or "Right"), 0 if a bone is missing.</summary>
        private static float ArmLength(Dictionary<string, Transform> bones, string side)
        {
            return bones.TryGetValue(side + "Shoulder", out var shoulder) && bones.TryGetValue(side + "Elbow", out var elbow) && bones.TryGetValue(side + "Hand", out var hand)
                ? Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, hand.position)
                : 0f;
        }

        private Transform CreateGoal(string goalName)
        {
            var goal = new GameObject(goalName).transform;
            goal.SetParent(transform, false);
            return goal;
        }

        /// <summary>
        /// Late chain, right after the IK solve: the head lifted (neck and head, half each). After
        /// the solve, because the solver keeps the head's animated rotation.
        /// </summary>
        internal void PostSolve()
        {
            var weight = _freeFall != null ? _freeFall.Weight : 0f;
            _headLiftApplied = 0f;
            if (!_writing || weight <= 0f)
            {
                return;
            }

            var right = transform.right;
            _headLiftApplied = _neck != null || _head != null ? HeadLift * weight : 0f;
            if (_neck != null)
            {
                _neck.rotation = Quaternion.AngleAxis(-HeadLift * 0.5f * weight, right) * _neck.rotation;
            }

            if (_head != null)
            {
                _head.rotation = Quaternion.AngleAxis(-HeadLift * 0.5f * weight, right) * _head.rotation;
            }
        }

        private void OnDisable()
        {
            if (_writing)
            {
                Release();
            }
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
