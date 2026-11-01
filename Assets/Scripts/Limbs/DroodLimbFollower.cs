using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Makes held parts follow their bone in flight: a hand-held light swings with the
    /// hand, and boots walk with the feet. Seated drood included: a part in the hand stays
    /// in the hand.
    ///
    /// Held parts are fused to the drood's rigidbody, so moving their transforms moves
    /// their colliders and force points (thrust, drag, lights) with them.
    ///
    /// Each part keeps a fixed grip on its bone (<see cref="DroodHeldGrips"/>), saved with the
    /// craft. The designer measures it where the part was placed; a craft without stored grips
    /// gets it measured once here, against the pose the drood had in the designer
    /// (<see cref="DroodDesignerPose"/>): the first flight frames are still the T-pose the
    /// drood is created in. If no designer pose has been recorded yet, it waits until the
    /// Animator has posed the skeleton and uses that.
    /// </summary>
    public class DroodLimbFollower : MonoBehaviour
    {
        // Fallback only: frames to wait for the Animator before trusting the live pose.
        private const int FramesBeforeLivePoseCapture = 30;

        private sealed class Entry
        {
            public Transform Part;
            public Transform Bone;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<string> _keys = new HashSet<string>();
        private EvaScript _eva;
        private DroodHeldGrips _grips;
        private Dictionary<string, Transform> _bones;
        private bool _dirty = true;
        private int _frames;

        public void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>
        /// Connected to something it doesn't hold (<see cref="DroodHeldParts.Find(ModApi.Craft.Parts.PartData, out bool)"/>):
        /// it is attached to it, not carrying it.
        /// </summary>
        internal bool Anchored { get; private set; }

        /// <summary>Where the grips are kept (the part modifier's data).</summary>
        internal void SetGrips(DroodHeldGrips grips)
        {
            _grips = grips;
            _dirty = true;
        }

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _bones = GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.First());
        }

        /// <summary>
        /// Late chain (see <see cref="Drood2FlightBehaviours"/>): after the IK solve, so parts
        /// follow the final pose, hands on the risers included.
        /// </summary>
        internal void Step()
        {
            _frames++;
            if (_dirty)
            {
                Rebuild();
            }

            // Position and rotation only: the rig's bones may carry import scale, which
            // must not scale the offsets.
            foreach (var entry in _entries)
            {
                if (entry.Part == null || entry.Bone == null)
                {
                    _dirty = true;
                    continue;
                }

                entry.Part.SetPositionAndRotation(
                    entry.Bone.position + entry.Bone.rotation * entry.LocalPosition,
                    entry.Bone.rotation * entry.LocalRotation);
            }
        }

        private void Rebuild()
        {
            _dirty = false;
            _entries.Clear();
            _keys.Clear();

            var drood = _eva != null ? _eva.PartScript?.Data : null;
            if (drood == null || _grips == null)
            {
                Anchored = false;
                return;
            }

            var heldGroups = DroodHeldParts.Find(drood, out var anchored);
            Anchored = anchored;
            foreach (var held in heldGroups)
            {
                if (!_bones.TryGetValue(held.Segment.FromBone, out var bone))
                {
                    continue;
                }

                for (var i = 0; i < held.Parts.Count; i++)
                {
                    var part = held.Parts[i];
                    var partTransform = part.PartScript?.Transform;
                    if (partTransform == null)
                    {
                        continue;
                    }

                    var key = DroodHeldGrips.Key(held.Segment.AttachPointName, i);
                    var partTypeId = part.PartType?.Id;
                    _keys.Add(key);
                    if (!_grips.TryGet(key, partTypeId, out var grip))
                    {
                        if (!TryMeasure(partTransform, bone, held.Segment.AttachPointName, out grip))
                        {
                            // Waiting for the Animator (fallback path); try again next frame.
                            _dirty = true;
                            continue;
                        }

                        grip.PartTypeId = partTypeId;
                        _grips.Set(key, grip);
                    }

                    _entries.Add(new Entry
                    {
                        Part = partTransform,
                        Bone = bone,
                        LocalPosition = grip.Position,
                        LocalRotation = grip.Rotation,
                    });
                }
            }

            if (!_dirty)
            {
                _grips.Retain(_keys);
            }
        }

        /// <summary>
        /// Grip of a part that has no stored one yet (a craft without stored grips):
        /// the part is still where the designer put it relative to the drood, so it's measured
        /// against the designer pose. False while the fallback is still waiting for the Animator.
        /// </summary>
        private bool TryMeasure(Transform part, Transform bone, string segmentName, out DroodHeldGrips.Grip grip)
        {
            grip = default;
            Vector3 bonePosition;
            Quaternion boneRotation;
            string source;
            if (DroodDesignerPose.TryGet(bone.name, out var designerPosition, out var designerRotation))
            {
                bonePosition = transform.TransformPoint(designerPosition);
                boneRotation = transform.rotation * designerRotation;
                source = "designer pose";
            }
            else if (_frames >= FramesBeforeLivePoseCapture)
            {
                bonePosition = bone.position;
                boneRotation = bone.rotation;
                source = "live pose (no designer pose recorded yet)";
            }
            else
            {
                return false;
            }

            var inverse = Quaternion.Inverse(boneRotation);
            grip.Position = inverse * (part.position - bonePosition);
            grip.Rotation = inverse * part.rotation;
            DroodLog.Debug($"Flight: {part.name} on {segmentName} ({bone.name}) had no stored grip; measured it against the {source}: " +
                      $"part at {DroodLimbSurfaces.Fmt(transform.InverseTransformPoint(part.position))}, " +
                      $"bone at {DroodLimbSurfaces.Fmt(transform.InverseTransformPoint(bonePosition))} (part space).");
            return true;
        }
    }
}
