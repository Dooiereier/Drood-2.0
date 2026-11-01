using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Surface attach points on every body segment of a drood. Each point names its own
    /// collider, and the designer raycasts against it (PartScript.CreateAttachPoints
    /// finds the collider by GameObject name and moves it to the AttachPointSurface layer).
    ///
    /// The drood prefab has no per-limb colliders, so <see cref="CreateLimbCollidersPatch"/>
    /// adds them to the bones just before the designer builds attach points. They exist
    /// only in the designer. In flight, attached parts are fused to the drood's body, so
    /// they don't need colliders there.
    /// </summary>
    public static class DroodLimbSurfaces
    {
        public sealed class Segment
        {
            public Segment(string attachPointName, string displayName, string fromBone, string toBone, float radiusFactor)
            {
                AttachPointName = attachPointName;
                DisplayName = displayName;
                FromBone = fromBone;
                ToBone = toBone;
                RadiusFactor = radiusFactor;
            }

            public string AttachPointName { get; }

            public string DisplayName { get; }

            /// <summary>The collider is parented here, so it follows this bone.</summary>
            public string FromBone { get; }

            /// <summary>Capsule end. Null means a sphere on <see cref="FromBone"/>.</summary>
            public string ToBone { get; }

            /// <summary>Capsule radius as a fraction of the segment length.</summary>
            public float RadiusFactor { get; }

            public string ColliderName => AttachPointName + "Collider";
        }

        // ORDER IS SAVE FORMAT. Attach point ids are list indices, and these come after
        // the stock seat point (id 0), so "DroodSurface" is id 1 and so on. Only ever
        // append to the end, here and in the part XML (Assets/Parts/Drood2.xml), which lists
        // the same attach points: name, display name, surface = ColliderName.
        //
        // "DroodSurface" (id 1) is the torso; crafts attach to it by that name and id. The
        // walking capsule (CharacterCollider) isn't a surface: it encloses the limbs, so
        // designer raycasts would hit it before any limb.
        public static readonly IReadOnlyList<Segment> Segments = new[]
        {
            new Segment(Drood2Types.AttachPointName, "Torso", "Hips", "Neck", 0.3f),
            new Segment("DroodSurfaceHead", "Head", "HeadCenter", null, 0f),
            new Segment("DroodSurfaceLeftUpperArm", "Left upper arm", "LeftShoulder", "LeftElbow", 0.22f),
            new Segment("DroodSurfaceLeftForearm", "Left forearm", "LeftElbow", "LeftHand", 0.2f),
            new Segment("DroodSurfaceLeftHand", "Left hand", "LeftHand", "LeftMiddleFingerLower", 0.35f),
            new Segment("DroodSurfaceRightUpperArm", "Right upper arm", "RightShoulder", "RightElbow", 0.22f),
            new Segment("DroodSurfaceRightForearm", "Right forearm", "RightElbow", "RightHand", 0.2f),
            new Segment("DroodSurfaceRightHand", "Right hand", "RightHand", "RightMiddleFingerLower", 0.35f),
            new Segment("DroodSurfaceLeftThigh", "Left thigh", "LeftHip", "LeftKnee", 0.22f),
            new Segment("DroodSurfaceLeftShin", "Left shin", "LeftKnee", "LeftAnkle", 0.2f),
            new Segment("DroodSurfaceLeftFoot", "Left foot", "LeftAnkle", "LeftToes", 0.4f),
            new Segment("DroodSurfaceRightThigh", "Right thigh", "RightHip", "RightKnee", 0.22f),
            new Segment("DroodSurfaceRightShin", "Right shin", "RightKnee", "RightAnkle", 0.2f),
            new Segment("DroodSurfaceRightFoot", "Right foot", "RightAnkle", "RightToes", 0.4f),
        };

        public static void CreateColliders(Transform part)
        {
            var bones = part.GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var segment in Segments)
            {
                if (bones.ContainsKey(segment.ColliderName) || !bones.TryGetValue(segment.FromBone, out var from))
                {
                    continue;
                }

                var go = new GameObject(segment.ColliderName);
                go.transform.SetParent(from, false);
                var scale = Mathf.Max(go.transform.lossyScale.x, 0.0001f);

                if (segment.ToBone != null && bones.TryGetValue(segment.ToBone, out var to))
                {
                    var start = from.position;
                    var end = to.position;
                    var length = Vector3.Distance(start, end);
                    var radius = length * segment.RadiusFactor;

                    go.transform.position = (start + end) * 0.5f;
                    go.transform.rotation = Quaternion.FromToRotation(Vector3.up, end - start);

                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.direction = 1;
                    capsule.radius = radius / scale;
                    capsule.height = (length + radius * 2f) / scale;
                }
                else
                {
                    // Head: sphere around HeadCenter, sized from the neck distance.
                    var radius = bones.TryGetValue("Neck", out var neck)
                        ? Vector3.Distance(neck.position, from.position) * 0.9f
                        : 0.15f;
                    go.AddComponent<SphereCollider>().radius = radius / scale;
                }

                // Diagnostics: where parts will snap, in the drood part's own frame.
                DroodLog.Debug($"Designer: {segment.AttachPointName} bone {segment.FromBone} at {Fmt(part.InverseTransformPoint(from.position))} (part space).");
            }
        }

        public static string Fmt(Vector3 v)
        {
            return $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
        }
    }
}
