using System;
using System.Collections.Generic;
using System.Linq;
using ModApi.Craft.Parts;

namespace Drood2
{
    /// <summary>
    /// Finds the parts a drood "holds": everything attached to one of its body-segment
    /// surface points, plus whatever is fused onto those parts.
    /// </summary>
    public static class DroodHeldParts
    {
        // Anything bigger is treated as structure the drood is attached to, e.g. a
        // platform whose attach point happened to touch a foot. That structure isn't
        // swung around by the drood's animation.
        public const int MaxPartsPerSegment = 25;
        // In kg (PartData.Mass is in the game's mass units, kg × Constants.MassScale).
        public const float MaxMassPerSegment = 100f;

        public sealed class Held
        {
            public Held(DroodLimbSurfaces.Segment segment, List<PartData> parts)
            {
                Segment = segment;
                Parts = parts;
            }

            public DroodLimbSurfaces.Segment Segment { get; }

            public List<PartData> Parts { get; }
        }

        /// <summary>
        /// The parts the drood holds on its limbs, by its designer choice
        /// (<see cref="Drood2LimbsData.AttachedParts"/>): Automatic holds a group up to
        /// MaxPartsPerSegment parts and MaxMassPerSegment kg without a command pod, anything bigger
        /// is structure the drood is attached to; Carried holds every group on its limbs;
        /// Anchored holds none.
        /// </summary>
        public static List<Held> Find(PartData drood) => Find(drood, out _);

        /// <summary>
        /// As <see cref="Find(PartData)"/>, and whether the drood is anchored: connected to
        /// anything it doesn't hold (structure, a joint, an attach point off its limbs).
        /// </summary>
        public static List<Held> Find(PartData drood, out bool anchored)
        {
            var mode = drood.GetModifier<Drood2LimbsData>()?.AttachedParts ?? Drood2LimbsData.AttachedAutomatic;
            anchored = false;
            var result = new List<Held>();
            var visited = new HashSet<PartData> { drood };

            foreach (var attachPoint in drood.AttachPoints)
            {
                var segment = DroodLimbSurfaces.Segments.FirstOrDefault(s => s.AttachPointName == attachPoint.Name);
                if (segment == null)
                {
                    anchored |= attachPoint.PartConnections.Count > 0;
                    continue;
                }

                foreach (var connection in attachPoint.PartConnections.ToList())
                {
                    var start = connection.GetOtherPart(drood);
                    if (start == null || visited.Contains(start))
                    {
                        continue;
                    }

                    if (connection.IsPhysicsJoint)
                    {
                        anchored = true;
                        continue;
                    }

                    var parts = CollectFusedGroup(start, visited);
                    var held = mode == Drood2LimbsData.AttachedCarried || (mode == Drood2LimbsData.AttachedAutomatic && IsHoldable(parts));
                    if (held)
                    {
                        result.Add(new Held(segment, parts));
                    }
                    else
                    {
                        anchored = true;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Parts reachable from <paramref name="start"/> through fused (non-joint)
        /// connections, i.e. the ones sharing its rigidbody.
        /// </summary>
        private static List<PartData> CollectFusedGroup(PartData start, HashSet<PartData> visited)
        {
            var parts = new List<PartData>();
            var queue = new Queue<PartData>();
            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var part = queue.Dequeue();
                parts.Add(part);

                foreach (var connection in part.PartConnections)
                {
                    var other = connection.GetOtherPart(part);
                    if (connection.IsPhysicsJoint || other == null || visited.Contains(other))
                    {
                        continue;
                    }

                    visited.Add(other);
                    queue.Enqueue(other);
                }
            }

            return parts;
        }

        /// <summary>
        /// Keeps the renderers of every part <paramref name="drood"/> holds out of Juno's mesh
        /// merging (PartGroupScript.ProcessMeshes). A merged mesh wouldn't move when a held
        /// part's transform moves. Called from the modifier's OnCraftLoaded and
        /// OnCraftStructureChanged, which run before the merge (verified in-game).
        /// </summary>
        public static void ExcludeFromMeshCombine(PartData drood)
        {
            foreach (var held in Find(drood))
            {
                foreach (var part in held.Parts)
                {
                    var maps = part.PartScript?.PartMaterialScript?.RendererMaps;
                    if (maps == null)
                    {
                        continue;
                    }

                    foreach (var map in maps)
                    {
                        map.ExcludeFromMeshCombine = true;
                    }
                }
            }
        }

        private static bool IsHoldable(List<PartData> parts)
        {
            return parts.Count <= MaxPartsPerSegment
                && parts.Sum(p => Convert.ToSingle(p.Mass)) / ModApi.Constants.MassScale <= MaxMassPerSegment
                && !parts.Any(p => p.PartType != null && p.PartType.IsCommandPod);
        }
    }
}
