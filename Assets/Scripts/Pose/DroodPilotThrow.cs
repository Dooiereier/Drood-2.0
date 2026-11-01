using RootMotion.FinalIK;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The pilot chute throw (ram-air deployment, <see cref="DroodParachute.HandThrow"/>): the
    /// right hand reaches back to the pilot chute pouch at the bottom of the container, then
    /// sweeps it out to the side at arm's length and lets go; the hand then goes back to what
    /// the rest of the pose asks. In the free-fall arch the left arm rises a little to keep
    /// the body level.
    ///
    /// Only the right hand effector and elbow bend goal (and the left hand's position), set in
    /// the late chain after the free-fall pose and before the IK solve, blended in and out.
    /// </summary>
    public class DroodPilotThrow : MonoBehaviour
    {
        // Let go at arm's length: this far (m) out to the side from the shoulder, a little
        // toward the head.
        private const float ReleaseOut = 0.7f;
        private const float ReleaseTowardHead = 0.1f;

        // The elbow points out and back while reaching.
        private const float ElbowOut = 0.5f;
        private const float ElbowTowardFeet = 0.2f;

        // The left arm rises this far (m) toward the head in the arch.
        private const float BalanceReach = 0.12f;

        // Into and out of the throw over this long (s).
        private const float BlendSeconds = 0.15f;

        private DroodParachute _parachute;
        private DroodFreeFall _freeFall;
        private FullBodyBipedIK _ik;
        private DroodIKOwner _ikOwner;
        private Transform _leftShoulder;
        private Transform _rightShoulder;
        private Transform _rightHand;
        private Transform _elbowGoal;
        private Vector3 _startLocal;
        private bool _started;
        private float _weight;
        private bool _writing;

        private void Awake()
        {
            _parachute = GetComponent<DroodParachute>();
            _freeFall = GetComponent<DroodFreeFall>();
            _ik = GetComponentInChildren<FullBodyBipedIK>(true);
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "LeftShoulder": _leftShoulder = _leftShoulder != null ? _leftShoulder : t; break;
                    case "RightShoulder": _rightShoulder = _rightShoulder != null ? _rightShoulder : t; break;
                    case "RightHand": _rightHand = _rightHand != null ? _rightHand : t; break;
                }
            }

            _elbowGoal = new GameObject("DroodPilotThrowElbowGoal").transform;
            _elbowGoal.SetParent(transform, false);
        }

        /// <summary>Late chain, after the free-fall pose and before the IK solve.</summary>
        internal void PreSolve()
        {
            var progress = _parachute != null ? _parachute.HandThrow : -1f;
            var throwing = progress >= 0f;
            _weight = Mathf.MoveTowards(_weight, throwing ? 1f : 0f, Time.deltaTime / BlendSeconds);
            if (_ik == null || _leftShoulder == null || _rightShoulder == null || _rightHand == null || _weight <= 0f)
            {
                _started = false;
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

            var solver = _ik.solver;
            var hand = solver.rightHandEffector;
            var head = transform.up;
            var belly = transform.forward;
            var shoulderMid = (_leftShoulder.position + _rightShoulder.position) * 0.5f;
            var shoulder = _rightShoulder.position;
            var outward = (shoulder - shoulderMid).normalized;

            // Where the hand starts: what the rest of the pose asked for, or the animated hand.
            if (!_started)
            {
                _started = true;
                _startLocal = transform.InverseTransformPoint(hand.positionWeight > 0f ? hand.position : _rightHand.position);
            }

            var pouch = _parachute.PilotPouch;
            var release = shoulder + outward * ReleaseOut + head * ReleaseTowardHead;
            Vector3 target;
            if (!throwing || progress >= 1f)
            {
                target = release;
            }
            else if (progress < DroodParachute.HandReachFraction)
            {
                target = Vector3.Lerp(transform.TransformPoint(_startLocal), pouch, Mathf.SmoothStep(0f, 1f, progress / DroodParachute.HandReachFraction));
            }
            else
            {
                target = Vector3.Lerp(pouch, release, Mathf.SmoothStep(0f, 1f, (progress - DroodParachute.HandReachFraction) / (1f - DroodParachute.HandReachFraction)));
            }

            // Blend over whatever the rest of the pose set this frame.
            var basePosition = hand.positionWeight > 0f ? hand.position : _rightHand.position;
            hand.position = Vector3.Lerp(basePosition, target, _weight);
            hand.positionWeight = Mathf.Max(hand.positionWeight, _weight);
            hand.rotationWeight *= 1f - _weight;
            _elbowGoal.position = shoulder + outward * ElbowOut - head * ElbowTowardFeet;
            solver.rightArmChain.bendConstraint.bendGoal = _elbowGoal;
            solver.rightArmChain.bendConstraint.weight = Mathf.Max(solver.rightArmChain.bendConstraint.weight, _weight);

            // Balance with the left arm in the arch.
            if (_freeFall != null && _freeFall.Weight > 0f && solver.leftHandEffector.positionWeight > 0f)
            {
                solver.leftHandEffector.position += head * (BalanceReach * _weight * _freeFall.Weight);
            }

            _writing = true;
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
