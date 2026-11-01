using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Drood2
{
    /// <summary>
    /// Sitting in the plane's door, legs out, played over the game's animation while
    /// <see cref="DroodPlaneRide"/> has the drood in the door: the clip (Mixamo, under
    /// ModData → Other Assets, its legs and head moving) looped at ClipSpeed, its head HeadStill
    /// and its legs LegsStill held on the clip's first frame (copies of it on head-only and
    /// legs-only layers); blended in and out. Only the pose: the ride places the drood.
    /// </summary>
    public class DroodPlaneSitting : MonoBehaviour
    {
        public const string ClipPath = "Assets/Animations/Sitting.fbx";

        private const float BlendSeconds = 0.25f;

        // The shares of the head's and the legs' motion taken out, and the clip's speed.
        private const float HeadStill = 0.8f;
        private const float LegsStill = 0.84f;
        private const float ClipSpeed = 1.3f;

        private Animator _animator;
        private AnimationClip _clip;
        private bool _loadAttempted;
        private PlayableGraph _graph;
        private AnimationPlayableOutput _output;
        private AnimationClipPlayable _playable;
        private AnimationClipPlayable _still;
        private AnimationClipPlayable _stillLegs;
        private AnimationLayerMixerPlayable _mixer;
        private AvatarMask _headMask;
        private AvatarMask _legsMask;
        private bool _rootMotion;
        private float _weight;
        private float _time;

        /// <summary>Game-loop Update: sitting while <paramref name="on"/>.</summary>
        internal void Step(bool on)
        {
            if (on && !_graph.IsValid())
            {
                Begin();
            }

            if (!_graph.IsValid())
            {
                return;
            }

            var dt = Time.deltaTime;
            _weight = Mathf.MoveTowards(_weight, on ? 1f : 0f, dt / BlendSeconds);
            if (!on && _weight <= 0f)
            {
                End();
                return;
            }

            _time = Mathf.Repeat(_time + dt * ClipSpeed, _clip.length);
            _playable.SetTime(_time);
            _output.SetWeight(Mathf.SmoothStep(0f, 1f, _weight));
        }

        private void Begin()
        {
            _animator = _animator != null ? _animator : GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                return;
            }

            if (!_loadAttempted)
            {
                _loadAttempted = true;
                try
                {
                    _clip = Assets.Scripts.Mod.Instance?.ResourceLoader?.LoadAsset<AnimationClip>(ClipPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Drood 2.0] Loading the sitting clip failed: {e.Message}");
                }

                if (_clip == null)
                {
                    Debug.LogWarning($"[Drood 2.0] Sitting clip '{ClipPath}' not found in the mod. Is it listed under Other Assets, imported as Humanoid?");
                    return;
                }

                DroodLog.Debug($"Sitting clip: {_clip.length:0.00} s, humanoid {_clip.humanMotion}.");
            }

            if (_clip == null)
            {
                return;
            }

            _graph = PlayableGraph.Create("Drood2 Plane Sitting");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _playable = AnimationClipPlayable.Create(_graph, _clip);
            _playable.SetApplyFootIK(true);
            _playable.SetSpeed(0);
            _still = AnimationClipPlayable.Create(_graph, _clip);
            _still.SetSpeed(0);
            _still.SetTime(0);
            _stillLegs = AnimationClipPlayable.Create(_graph, _clip);
            _stillLegs.SetSpeed(0);
            _stillLegs.SetTime(0);
            _headMask = _headMask != null ? _headMask : Mask(AvatarMaskBodyPart.Head, AvatarMaskBodyPart.Head);
            _legsMask = _legsMask != null ? _legsMask : Mask(AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);
            _mixer = AnimationLayerMixerPlayable.Create(_graph, 3);
            _graph.Connect(_playable, 0, _mixer, 0);
            _graph.Connect(_still, 0, _mixer, 1);
            _graph.Connect(_stillLegs, 0, _mixer, 2);
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, HeadStill);
            _mixer.SetLayerMaskFromAvatarMask(1, _headMask);
            _mixer.SetInputWeight(2, LegsStill);
            _mixer.SetLayerMaskFromAvatarMask(2, _legsMask);
            _rootMotion = _animator.applyRootMotion;
            _animator.applyRootMotion = false;
            _output = AnimationPlayableOutput.Create(_graph, "Plane Sitting", _animator);
            _output.SetSourcePlayable(_mixer);
            _output.SetWeight(0f);
            _graph.Play();
            _weight = 0f;
            _time = 0f;
        }

        private void End()
        {
            if (_animator != null)
            {
                _animator.applyRootMotion = _rootMotion;
            }

            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _weight = 0f;
        }

        private void OnDisable()
        {
            End();
        }

        /// <summary>A humanoid mask with only <paramref name="first"/> and <paramref name="second"/> on.</summary>
        private static AvatarMask Mask(AvatarMaskBodyPart first, AvatarMaskBodyPart second)
        {
            var mask = new AvatarMask();
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            {
                mask.SetHumanoidBodyPartActive(part, part == first || part == second);
            }

            return mask;
        }

        private void OnDestroy()
        {
            if (_headMask != null)
            {
                Destroy(_headMask);
            }

            if (_legsMask != null)
            {
                Destroy(_legsMask);
            }
        }
    }
}
