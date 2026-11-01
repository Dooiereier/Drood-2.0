using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft.Parts;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Designer side of held parts, run every designer frame from the modifier.
    ///
    /// 1. Keeps held parts in place relative to the drood when the game moves only the drood.
    ///    Seating a drood in a crew compartment and taking it out again
    ///    (EvaScript.SetTransformToCrewCompartment / SetTransformToCrewExit) moves the drood's
    ///    own transform, not the parts attached to its limbs; the stock drood never holds
    ///    parts. If the drood moved and a held part didn't, the part is put back at its place
    ///    relative to the drood. Otherwise that place is remembered (dragging the whole drood,
    ///    or moving the part on the limb yourself).
    /// 2. Measures each held part's grip on its bone (<see cref="DroodHeldGrips"/>) against
    ///    the pose shown here, where the part was placed, whenever a part is added or moved.
    ///    Not while the drood is seated (its bones are then in the sitting pose, while the parts
    ///    keep their standing place), and not in the first frames, while the drood is still in
    ///    the T-pose it's created in.
    /// </summary>
    internal sealed class DroodDesignerHeldParts
    {
        // The drood is created in a T-pose; give the Animator a moment to pose it.
        private const int FramesBeforeMeasuring = 10;

        private sealed class Tracked
        {
            public string Key;
            public Transform Bone;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LastPosition;
            public Quaternion LastRotation;
            public bool HasLast;
            public bool GripMeasured;
        }

        private readonly IPartScript _drood;
        private readonly DroodHeldGrips _grips;
        private readonly Dictionary<PartData, Tracked> _tracked = new Dictionary<PartData, Tracked>();
        private readonly List<PartData> _held = new List<PartData>();
        private readonly HashSet<string> _keys = new HashSet<string>();
        private Dictionary<string, Transform> _bones;
        private EvaScript _eva;
        private Vector3 _lastDroodPosition;
        private Quaternion _lastDroodRotation;
        private bool _dirty = true;
        private int _frames;

        // Once per session: the designer recreates modifier scripts on every edit.
        private static bool _loggedRunning;

        public DroodDesignerHeldParts(IPartScript drood, DroodHeldGrips grips)
        {
            _drood = drood;
            _grips = grips;
        }

        /// <summary>The craft's structure changed: find the held parts again.</summary>
        public void MarkDirty()
        {
            _dirty = true;
        }

        public void LateUpdate()
        {
            var drood = _drood?.Transform;
            if (drood == null || _drood.Data == null)
            {
                return;
            }

            if (!_loggedRunning)
            {
                _loggedRunning = true;
                DroodLog.Debug("Designer: keeping held parts with the drood (designer game loop is running).");
            }

            if (_bones == null)
            {
                _bones = drood.GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name)
                    .ToDictionary(g => g.Key, g => g.First());
                _eva = _drood.GetModifier<EvaScript>();
            }

            if (_dirty)
            {
                Rebuild(drood);
            }

            _frames++;
            var canMeasure = _frames >= FramesBeforeMeasuring && !(_eva != null && _eva.CrewCompartment != null);
            var droodMoved = drood.position != _lastDroodPosition || drood.rotation != _lastDroodRotation;
            var inverse = Quaternion.Inverse(drood.rotation);
            foreach (var part in _held)
            {
                var transform = part.PartScript?.Transform;
                if (transform == null || !_tracked.TryGetValue(part, out var tracked))
                {
                    continue;
                }

                if (droodMoved && tracked.HasLast
                    && transform.position == tracked.LastPosition
                    && transform.rotation == tracked.LastRotation)
                {
                    // Only the drood moved (seat entry or exit): bring the part along.
                    transform.SetPositionAndRotation(
                        drood.TransformPoint(tracked.LocalPosition),
                        drood.rotation * tracked.LocalRotation);
                    DroodLog.Debug($"Designer: moved {transform.name} along with the drood.");
                }
                else
                {
                    var localPosition = drood.InverseTransformPoint(transform.position);
                    var localRotation = inverse * transform.rotation;
                    if (localPosition != tracked.LocalPosition || localRotation != tracked.LocalRotation)
                    {
                        // Moved on the limb (or first seen): measure its grip again.
                        tracked.LocalPosition = localPosition;
                        tracked.LocalRotation = localRotation;
                        tracked.GripMeasured = false;
                    }
                }

                if (!tracked.GripMeasured && canMeasure && tracked.Bone != null)
                {
                    MeasureGrip(part, transform, tracked);
                }

                tracked.LastPosition = transform.position;
                tracked.LastRotation = transform.rotation;
                tracked.HasLast = true;
            }

            _lastDroodPosition = drood.position;
            _lastDroodRotation = drood.rotation;
        }

        private void Rebuild(Transform drood)
        {
            _dirty = false;
            _held.Clear();
            _keys.Clear();
            var stillHeld = new HashSet<PartData>();
            foreach (var held in DroodHeldParts.Find(_drood.Data))
            {
                _bones.TryGetValue(held.Segment.FromBone, out var bone);
                for (var i = 0; i < held.Parts.Count; i++)
                {
                    var part = held.Parts[i];
                    var key = DroodHeldGrips.Key(held.Segment.AttachPointName, i);
                    _held.Add(part);
                    _keys.Add(key);
                    stillHeld.Add(part);
                    if (!_tracked.TryGetValue(part, out var tracked))
                    {
                        tracked = new Tracked();
                        _tracked[part] = tracked;
                        var transform = part.PartScript?.Transform;
                        if (transform != null)
                        {
                            tracked.LocalPosition = drood.InverseTransformPoint(transform.position);
                            tracked.LocalRotation = Quaternion.Inverse(drood.rotation) * transform.rotation;
                        }
                    }

                    // A new key (e.g. another part now comes before it in the group) or bone
                    // needs a new grip.
                    if (tracked.Key != key || tracked.Bone != bone)
                    {
                        tracked.Key = key;
                        tracked.Bone = bone;
                        tracked.GripMeasured = false;
                    }
                }
            }

            foreach (var part in _tracked.Keys.Where(p => !stillHeld.Contains(p)).ToList())
            {
                _tracked.Remove(part);
            }

            _grips.Retain(_keys);
        }

        private void MeasureGrip(PartData part, Transform transform, Tracked tracked)
        {
            var inverse = Quaternion.Inverse(tracked.Bone.rotation);
            _grips.Set(tracked.Key, new DroodHeldGrips.Grip
            {
                PartTypeId = part.PartType?.Id,
                Position = inverse * (transform.position - tracked.Bone.position),
                Rotation = inverse * transform.rotation,
            });
            tracked.GripMeasured = true;
            DroodLog.Debug($"Designer: measured the grip of {transform.name} on {tracked.Key}.");
        }
    }
}
