using System.Collections.Generic;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// One hand holding a riser or steering toggle: the hand turned so its knuckles run along
    /// the line (index finger up the line, palm facing in), the wrist placed so the line runs
    /// through the fist, and the fingers and thumb curled around it.
    ///
    /// The drood's rig has finger bones (Side + IndexFinger / MiddleFinger / RingFinger / Pinky,
    /// three bones each, and Thumb with two more). The hand's own axes (across the knuckles,
    /// toward the fingers, out of the palm) are measured from them in the hand bone's frame,
    /// once the Animator has posed the hand (fingers at rest curl toward the palm).
    /// </summary>
    internal sealed class DroodHandGrip
    {
        // Wrist to the middle of the fist (m): along the fingers and out of the palm.
        private const float FistReach = 0.07f;
        private const float FistDepth = 0.035f;

        // Curl per finger joint and per thumb joint (degrees).
        private const float FingerCurl = 65f;
        private const float ThumbCurl = 30f;

        private readonly Transform _hand;
        private readonly List<Transform[]> _fingers = new List<Transform[]>();
        private readonly Transform[] _thumb;
        private Vector3 _knuckles;
        private Vector3 _toFingers;
        private Vector3 _palm;
        private bool _measured;

        public DroodHandGrip(Transform hand, string side, IReadOnlyDictionary<string, Transform> bones)
        {
            _hand = hand;
            foreach (var name in new[] { "IndexFinger", "MiddleFinger", "RingFinger", "Pinky" })
            {
                if (bones.TryGetValue(side + name, out var bone))
                {
                    _fingers.Add(Chain(bone));
                }
            }

            _thumb = bones.TryGetValue(side + "Thumb", out var thumb) ? Chain(thumb) : new Transform[0];
        }

        public bool Valid => _hand != null && _fingers.Count >= 2;

        /// <summary>The hand bone's world position (wrist) and rotation that hold a line at
        /// <paramref name="grip"/> running <paramref name="lineUp"/>, the palm facing <paramref name="inward"/>.</summary>
        public void Target(Vector3 grip, Vector3 lineUp, Vector3 inward, out Vector3 wrist, out Quaternion rotation)
        {
            Measure();
            var palmWorld = Vector3.ProjectOnPlane(inward, lineUp);
            if (palmWorld.sqrMagnitude < 1e-6f)
            {
                palmWorld = Vector3.ProjectOnPlane(Vector3.up, lineUp);
            }

            rotation = Quaternion.LookRotation(lineUp, palmWorld.normalized) * Quaternion.Inverse(Quaternion.LookRotation(_knuckles, _palm));
            wrist = grip - rotation * (_toFingers * FistReach + _palm * FistDepth);
        }

        /// <summary>
        /// The hand bone's world rotation with its fingers pointing <paramref name="toFingers"/>
        /// and the palm facing <paramref name="palm"/> (an open hand).
        /// </summary>
        public Quaternion Orient(Vector3 toFingers, Vector3 palm)
        {
            Measure();
            var palmWorld = Vector3.ProjectOnPlane(palm, toFingers);
            if (palmWorld.sqrMagnitude < 1e-6f)
            {
                palmWorld = Vector3.ProjectOnPlane(Vector3.up, toFingers);
            }

            return Quaternion.LookRotation(toFingers, palmWorld.normalized) * Quaternion.Inverse(Quaternion.LookRotation(_toFingers, _palm));
        }

        /// <summary>The middle of the fist, where the held line runs (after the IK solve).</summary>
        public Vector3 Fist
        {
            get
            {
                Measure();
                return _hand.position + _hand.rotation * (_toFingers * FistReach + _palm * FistDepth);
            }
        }

        /// <summary>Curls the fingers and thumb around the line, by <paramref name="weight"/> (0..1).</summary>
        public void Curl(float weight)
        {
            if (weight <= 0f)
            {
                return;
            }

            Measure();
            var palm = _hand.rotation * _palm;
            var toFingers = _hand.rotation * _toFingers;

            // Rotating about Cross(d, n) moves d toward n: the fingers bend into the palm.
            var fingerAxis = Vector3.Cross(toFingers, palm);
            foreach (var chain in _fingers)
            {
                foreach (var joint in chain)
                {
                    joint.rotation = Quaternion.AngleAxis(FingerCurl * weight, fingerAxis) * joint.rotation;
                }
            }

            if (_thumb.Length > 0)
            {
                var thumbDirection = _thumb[_thumb.Length - 1].position - _thumb[0].position;
                var thumbAxis = Vector3.Cross(thumbDirection, palm);
                foreach (var joint in _thumb)
                {
                    joint.rotation = Quaternion.AngleAxis(ThumbCurl * weight, thumbAxis) * joint.rotation;
                }
            }
        }

        /// <summary>
        /// The hand's axes in its own frame: across the knuckles (pinky to index), toward the
        /// fingers, and out of the palm (the side the resting fingers curl toward).
        /// </summary>
        private void Measure()
        {
            if (_measured || !Valid)
            {
                return;
            }

            _measured = true;
            var index = _hand.InverseTransformPoint(_fingers[0][0].position);
            var pinky = _hand.InverseTransformPoint(_fingers[_fingers.Count - 1][0].position);
            var middle = _hand.InverseTransformPoint(_fingers[Mathf.Min(1, _fingers.Count - 1)][0].position);
            _knuckles = (index - pinky).normalized;
            _toFingers = Vector3.ProjectOnPlane(middle, _knuckles).normalized;
            _palm = Vector3.Cross(_toFingers, _knuckles).normalized;

            // Which way is the palm: toward the fingertips' curl, or failing that the thumb.
            var sideSum = 0f;
            foreach (var chain in _fingers)
            {
                var tip = _hand.InverseTransformPoint(chain[chain.Length - 1].position);
                var baseJoint = _hand.InverseTransformPoint(chain[0].position);
                sideSum += Vector3.Dot(tip - baseJoint, _palm);
            }

            if (Mathf.Abs(sideSum) < 0.005f && _thumb.Length > 0)
            {
                sideSum = Vector3.Dot(_hand.InverseTransformPoint(_thumb[_thumb.Length - 1].position), _palm);
            }

            if (sideSum < 0f)
            {
                _palm = -_palm;
            }

            _knuckles = Vector3.Cross(_palm, _toFingers).normalized * Mathf.Sign(Vector3.Dot(Vector3.Cross(_palm, _toFingers), _knuckles));
        }

        private static Transform[] Chain(Transform start)
        {
            var chain = new List<Transform> { start };
            var current = start;
            while (chain.Count < 3 && current.childCount > 0)
            {
                current = current.GetChild(0);
                chain.Add(current);
            }

            return chain.ToArray();
        }
    }
}
