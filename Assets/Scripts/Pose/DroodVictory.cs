using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Drood2
{
    /// <summary>
    /// After a top landing (graded VictoryGrade or better, <see cref="DroodParachute"/>'s Flight
    /// Log grade) the drood cheers: the "Victory" clip (Mixamo, Assets/Animations/Victory.fbx,
    /// listed under the mod's Other Assets) plays once in place over the drood's own Animator,
    /// like the landing clips (<see cref="DroodLandingFall"/>), once the drood stands still on
    /// ground no steeper than MaxSlope; it waits up to WaitSeconds for that. Blended in over
    /// BlendInSeconds and out over BlendOutSeconds; a movement key, leaving the ground or losing
    /// control blends it out over CancelSeconds. Only the drood in control.
    ///
    /// The drood's suit is wider than the Mixamo character, so a lowered arm of the clip goes
    /// into the body: an upper arm pointing down (within ArmDownFrom degrees of straight down)
    /// is turned out to the side, by up to ArmOutDegrees when it hangs straight down.
    /// </summary>
    public class DroodVictory : MonoBehaviour
    {
        public const string VictoryPath = "Assets/Animations/Victory.fbx";
        public const int VictoryGrade = 9;
        private const float MaxSlope = 10f;
        private const float WaitSeconds = 10f;
        private const float StillSpeed = 0.3f;
        private const float BlendInSeconds = 0.25f;
        private const float BlendOutSeconds = 0.4f;
        private const float CancelSeconds = 0.2f;
        private const float ArmOutDegrees = 14f;
        private const float ArmDownFrom = 50f;

        private static AnimationClip _clip;
        private static bool _loadAttempted;

        private EvaScript _eva;
        private Animator _animator;
        private DroodLandingFall _landingFall;
        private DroodRunOut _runOut;
        private PlayableGraph _graph;
        private AnimationPlayableOutput _output;
        private AnimationClipPlayable _playable;
        private Transform _leftShoulder;
        private Transform _rightShoulder;
        private Transform _leftElbow;
        private Transform _rightElbow;
        private bool _rootMotion;
        private float _queuedAt = float.NegativeInfinity;
        private float _time = -1f;
        private float _weight;
        private float _blendOutFrom = -1f;
        private float _blendOutSeconds;

        public bool Active => _time >= 0f;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _animator = GetComponentInChildren<Animator>(true);
            _landingFall = GetComponent<DroodLandingFall>();
            _runOut = GetComponent<DroodRunOut>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "LeftShoulder": _leftShoulder = _leftShoulder != null ? _leftShoulder : t; break;
                    case "RightShoulder": _rightShoulder = _rightShoulder != null ? _rightShoulder : t; break;
                    case "LeftElbow": _leftElbow = _leftElbow != null ? _leftElbow : t; break;
                    case "RightElbow": _rightElbow = _rightElbow != null ? _rightElbow : t; break;
                }
            }
        }

        /// <summary>Late, after the IK solve: the lowered arms turned out of the suit.</summary>
        internal void PostSolve()
        {
            if (!Active || _weight <= 0f)
            {
                return;
            }

            var share = Mathf.SmoothStep(0f, 1f, _weight);
            TurnOut(_leftShoulder, _leftElbow, -transform.right, share);
            TurnOut(_rightShoulder, _rightElbow, transform.right, share);
        }

        private void TurnOut(Transform shoulder, Transform elbow, Vector3 outward, float share)
        {
            if (shoulder == null || elbow == null)
            {
                return;
            }

            var arm = elbow.position - shoulder.position;
            var down = Vector3.Angle(arm, -transform.up);
            var amount = ArmOutDegrees * Mathf.InverseLerp(ArmDownFrom, 0f, down) * share;
            var axis = Vector3.Cross(arm, outward);
            if (amount > 0f && axis.sqrMagnitude > 1e-8f)
            {
                shoulder.rotation = Quaternion.AngleAxis(amount, axis.normalized) * shoulder.rotation;
            }
        }

        /// <summary>A landing graded <paramref name="grade"/> on ground <paramref name="slope"/> degrees steep.</summary>
        internal void Landed(int grade, float slope)
        {
            if (grade >= VictoryGrade && slope <= MaxSlope && _eva != null && _eva.IsPlayerCraft)
            {
                _queuedAt = Time.time;
            }
        }

        /// <summary>Game-loop Update: starts the clip once the drood stands still, steps it, ends it.</summary>
        internal void Step()
        {
            if (Active)
            {
                Play();
                return;
            }

            if (float.IsNegativeInfinity(_queuedAt))
            {
                return;
            }

            if (Time.time - _queuedAt > WaitSeconds || _eva == null || !_eva.IsPlayerCraft)
            {
                _queuedAt = float.NegativeInfinity;
                return;
            }

            var body = _eva.PartScript?.BodyScript?.RigidBody;
            var still = body != null && Vector3.ProjectOnPlane(body.velocity, transform.up).magnitude < StillSpeed;
            if (still && _eva.IsGrounded && (_landingFall == null || !_landingFall.Active) && (_runOut == null || !_runOut.Active) && !Moving())
            {
                _queuedAt = float.NegativeInfinity;
                Begin();
            }
        }

        private bool Moving()
        {
            var controls = _eva.PartScript?.CommandPod?.Controls;
            return controls != null
                && (Mathf.Abs(controls.EvaMoveFwdAft) > 0.1f || Mathf.Abs(controls.EvaStrafe) > 0.1f || Mathf.Abs(controls.EvaTurn) > 0.1f);
        }

        private void Begin()
        {
            if (_animator == null)
            {
                return;
            }

            if (!_loadAttempted)
            {
                _loadAttempted = true;
                try
                {
                    // Asking for an AnimationClip on the FBX path returns the clip inside it.
                    _clip = Assets.Scripts.Mod.Instance?.ResourceLoader?.LoadAsset<AnimationClip>(VictoryPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Drood 2.0] Loading the victory clip failed: {e.Message}");
                }

                if (_clip == null)
                {
                    Debug.LogWarning($"[Drood 2.0] Victory clip '{VictoryPath}' not found in the mod. Is it listed under Other Assets?");
                }
            }

            if (_clip == null)
            {
                return;
            }

            _graph = PlayableGraph.Create("Drood2 Victory");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _playable = AnimationClipPlayable.Create(_graph, _clip);
            _playable.SetApplyFootIK(true);
            _playable.SetSpeed(0);

            // Only the pose: the clip's root motion doesn't move the drood.
            _rootMotion = _animator.applyRootMotion;
            _animator.applyRootMotion = false;
            _output = AnimationPlayableOutput.Create(_graph, "Victory", _animator);
            _output.SetSourcePlayable(_playable);
            _output.SetWeight(0f);
            _graph.Play();
            _time = 0f;
            _weight = 0f;
            _blendOutFrom = -1f;
            DroodLog.Debug($"Victory ({_clip.length:0.0} s clip).");
        }

        private void Play()
        {
            var dt = Time.deltaTime;
            _time += dt;
            if (_blendOutFrom < 0f)
            {
                if (Moving() || !_eva.IsGrounded || !_eva.IsPlayerCraft || !_eva.EvaActive)
                {
                    BlendOut(CancelSeconds);
                }
                else if (_time >= _clip.length - BlendOutSeconds)
                {
                    BlendOut(BlendOutSeconds);
                }
            }

            _playable.SetTime(Mathf.Min(_time, _clip.length));
            _weight = _blendOutFrom >= 0f
                ? Mathf.Clamp01(1f - (_time - _blendOutFrom) / _blendOutSeconds)
                : Mathf.Clamp01(_time / BlendInSeconds);
            _output.SetWeight(Mathf.SmoothStep(0f, 1f, _weight));
            if (_blendOutFrom >= 0f && _weight <= 0f)
            {
                End();
            }
        }

        private void BlendOut(float seconds)
        {
            _blendOutFrom = _time;
            _blendOutSeconds = Mathf.Max(seconds * Mathf.Clamp01(_weight), 0.01f);
        }

        private void End()
        {
            _time = -1f;
            if (_animator != null)
            {
                _animator.applyRootMotion = _rootMotion;
            }

            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }

        private void OnDisable()
        {
            Cancel();
        }

        /// <summary>No victory now or queued (the drood is taken away, e.g. teleported up).</summary>
        internal void Cancel()
        {
            _queuedAt = float.NegativeInfinity;
            if (Active)
            {
                End();
            }
        }

        private void OnDestroy()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }
    }
}
