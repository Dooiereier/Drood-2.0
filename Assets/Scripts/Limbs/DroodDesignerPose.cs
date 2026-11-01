using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The drood's skeleton pose as shown in the designer, where parts are placed against
    /// the limb colliders. Bone transforms are stored in the drood part's own frame.
    ///
    /// Why it's needed: a drood is created in a T-pose and its Animator then poses it
    /// (arms down). In the designer that happens before you place parts. In flight the
    /// first frames are still the T-pose, so a part-to-bone offset taken then would lock in
    /// the T-pose, and a part placed in the hand would end up behind the drood once the arm
    /// came down. The follower computes offsets against this designer pose instead.
    ///
    /// The pose is the same for every drood (same animation), so it's recorded once per
    /// game session in the designer and saved to a file. Crafts launched without opening
    /// the designer first use the saved copy.
    /// </summary>
    public static class DroodDesignerPose
    {
        private static Dictionary<string, (Vector3 Position, Quaternion Rotation)> _bones;
        private static bool _recordedThisSession;

        private static string FilePath =>
            Path.Combine(Assets.Scripts.Game.PersistentDataPath, "Drood 2.0", "DesignerPose.txt");

        public static IEnumerable<string> BoneNames =>
            DroodLimbSurfaces.Segments.Select(s => s.FromBone).Distinct();

        public static bool NeedsRecording => !_recordedThisSession;

        public static bool TryGet(string bone, out Vector3 position, out Quaternion rotation)
        {
            EnsureLoaded();
            if (_bones != null && _bones.TryGetValue(bone, out var pose))
            {
                position = pose.Position;
                rotation = pose.Rotation;
                return true;
            }

            position = default;
            rotation = default;
            return false;
        }

        public static void Record(Transform part, IReadOnlyDictionary<string, Transform> bones)
        {
            var recorded = new Dictionary<string, (Vector3, Quaternion)>();
            foreach (var name in BoneNames)
            {
                if (bones.TryGetValue(name, out var bone))
                {
                    recorded[name] = (part.InverseTransformPoint(bone.position), Quaternion.Inverse(part.rotation) * bone.rotation);
                }
            }

            _bones = recorded;
            _recordedThisSession = true;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, recorded.Select(kv => string.Join(" ",
                    kv.Key,
                    F(kv.Value.Item1.x), F(kv.Value.Item1.y), F(kv.Value.Item1.z),
                    F(kv.Value.Item2.x), F(kv.Value.Item2.y), F(kv.Value.Item2.z), F(kv.Value.Item2.w))));
                DroodLog.Debug($"Recorded the designer pose ({recorded.Count} bones); right hand at " +
                          $"{(recorded.TryGetValue("RightHand", out var hand) ? DroodLimbSurfaces.Fmt(hand.Item1) : "?")} (part space).");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Could not save the designer pose: {e.Message}");
            }
        }

        private static void EnsureLoaded()
        {
            if (_bones != null || !File.Exists(FilePath))
            {
                return;
            }

            try
            {
                var loaded = new Dictionary<string, (Vector3, Quaternion)>();
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var f = line.Split(' ');
                    if (f.Length != 8)
                    {
                        continue;
                    }

                    loaded[f[0]] = (
                        new Vector3(P(f[1]), P(f[2]), P(f[3])),
                        new Quaternion(P(f[4]), P(f[5]), P(f[6]), P(f[7])));
                }

                _bones = loaded;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Could not read the designer pose: {e.Message}");
            }
        }

        // Invariant culture: a comma-decimal system locale must not break the file.
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static float P(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Added to Drood 2.0 in the designer. Once the Animator has posed the skeleton, it
    /// records the pose (<see cref="DroodDesignerPose"/>) and removes itself.
    /// </summary>
    public class DroodDesignerPoseRecorder : MonoBehaviour
    {
        // The drood is created in a T-pose; give the Animator a moment to pose it.
        private const int FramesToWait = 10;

        private int _frames;

        private void LateUpdate()
        {
            if (++_frames < FramesToWait)
            {
                return;
            }

            // A drood seated in a chair or pod shows the sitting pose, which is wrong for parts
            // placed on a standing drood. Another (standing) drood can record it later.
            var eva = GetComponent<Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript>();
            var seated = eva != null && eva.CrewCompartment != null;
            if (DroodDesignerPose.NeedsRecording && !seated)
            {
                var bones = GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name)
                    .ToDictionary(g => g.Key, g => g.First());
                DroodDesignerPose.Record(transform, bones);
            }

            Destroy(this);
        }
    }
}
