using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using RootMotion.FinalIK;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Drood2
{
    /// <summary>
    /// Hard landing for Drood 2.0: on a hard touchdown under canopy, plays a landing clip
    /// (Mixamo, imported as humanoid clips so they retarget onto the drood's humanoid rig) over
    /// the drood's own Animator, then hands back (which one: <see cref="Classify"/>): "Hard
    /// Landing" (Assets/Animations/HardLanding.fbx) for a mostly vertical one, "Falling From
    /// Losing Balance" (Assets/Animations/Collapse.fbx) for a very hard vertical one (the legs
    /// give way onto the belly), "Fall Flat" (Assets/Animations/FallFlat.fbx) for one too fast
    /// forward, or too hard and too fast together, to run out (thrown forward onto the belly);
    /// the last two lie there LieSeconds, then get
    /// up with "Standing Up", Assets/Animations/StandingUp.fbx, crossfaded in over
    /// StandUpCrossfadeSeconds; without that clip, blending back over GetUpSeconds).
    ///
    /// Playback uses a separate PlayableGraph with an AnimationPlayableOutput on the
    /// drood's Animator. The output's weight blends the clip in and out over Juno's own
    /// animator controller, so nothing about the controller is changed. Movement is frozen
    /// while it plays (<see cref="FreezeMovementDuringLandingFallPatch"/>).
    ///
    /// The drood's arms are proportioned differently from the Mixamo character, so the
    /// retargeted hands stop short of the ground where the clip plants them. The drood's
    /// FinalIK is used for hand planting only: a hand that comes within reach of the ground
    /// is pulled onto it (and one that would go through is pushed out). All other IK goals
    /// are held at zero so nothing else pulls the limbs off the clip's pose.
    ///
    /// The clip ships in the mod: the FBX must be listed under the mod's Other Assets
    /// (ModData), which is what ModTools packages.
    /// </summary>
    public class DroodLandingFall : MonoBehaviour
    {
        /// <summary>The landing a touchdown under canopy gets.</summary>
        public enum Kind
        {
            /// <summary>Runs out to a stop or stands.</summary>
            Soft,

            /// <summary>The hard-landing clip.</summary>
            Hard,

            /// <summary>The fall-flat clip.</summary>
            Flat,

            /// <summary>The collapse clip: a very hard drop, the legs giving way onto the belly.</summary>
            Collapse,
        }

        /// <summary>
        /// Above this slope (degrees) a hard touchdown always falls flat (lying, the pose tilts
        /// with the whole slope; the upright clips are made for flat ground) and a soft one gets
        /// no run-out: the game's own standing or sliding.
        /// </summary>
        public const float SteepSlope = 30f;

        // On a slope the clip is tilted toward it about the ground under the feet: lying, by the
        // whole slope; upright, by SlopeTiltShare of it, at most SlopeTiltMax (degrees; the share
        // of SlopeTiltFull, from where it stays), so a standing body stays nearer upright than
        // the hill; in between by how upright the body is (hips to neck against the vertical),
        // eased over SlopeTiltSeconds. The hard landing stays on its feet: always the upright
        // tilt (its deep forward bend, read as half lying, tilted it with more of a slope falling
        // away ahead, which bent it further: nearly flat for a moment). Hands and feet fit the local surface. Lifting the body out
        // of the ground and bringing the feet down to it work on a slope for the hard landing too
        // (above LiftSlope, degrees).
        private const float SlopeTiltShare = 0.5f;
        private const float SlopeTiltFull = 20f;
        private const float SlopeTiltMax = SlopeTiltShare * SlopeTiltFull;
        private const float SlopeTiltSeconds = 0.2f;

        // The slope is measured again each frame while the clip plays (sliding onto steeper or
        // flatter ground): the normals under the feet and SlopeProbe (m) ahead, behind and to
        // each side, averaged, eased over SlopeFollowSeconds.
        private const float SlopeProbe = 0.5f;
        private const float SlopeFollowSeconds = 0.3f;
        private const float LiftSlope = 3f;
        private Vector3 _groundNormal;
        private float _slope;
        private float _slopeTilt;
        private Transform _neck;

        /// <summary>Sink speed (m/s) a touchdown can take alone (its limit).</summary>
        public const float MaxSinkSpeed = 4f;

        /// <summary>Forward speed (m/s) a touchdown can take alone (its limit).</summary>
        public const float MaxForwardSpeed = DroodRunOut.MaxSpeed;

        /// <summary>
        /// The landing for a touchdown at <paramref name="sinkSpeed"/> down and
        /// <paramref name="forwardSpeed"/> forward (m/s), each as a share of its limit: soft while
        /// the shares squared add up to less than SoftLimit; else the hard landing (a drop the legs
        /// take) at up to HardLandingMaxForward forward, collapsing instead from CollapseSinkSpeed
        /// down, and falling flat (thrown forward) above it.
        /// </summary>
        public static Kind Classify(float sinkSpeed, float forwardSpeed)
        {
            if (Severity(sinkSpeed, forwardSpeed) < SoftLimit)
            {
                return Kind.Soft;
            }

            if (forwardSpeed > HardLandingMaxForward)
            {
                return Kind.Flat;
            }

            return sinkSpeed >= CollapseSinkSpeed ? Kind.Collapse : Kind.Hard;
        }

        /// <summary>
        /// How hard a touchdown is: its speeds as shares of their limits, combined
        /// (√(down² + forward²); below 1 soft).
        /// </summary>
        public static float Severity(float sinkSpeed, float forwardSpeed)
        {
            var down = Mathf.Max(sinkSpeed, 0f) / MaxSinkSpeed;
            var forward = Mathf.Max(forwardSpeed, 0f) / MaxForwardSpeed;
            return Mathf.Sqrt(down * down + forward * forward);
        }

        private const float SoftLimit = 1f;
        private const float HardLandingMaxForward = 6f;
        private const float CollapseSinkSpeed = 7f;

        /// <summary>Asset paths of the clips inside the mod bundle.</summary>
        public const string HardLandingPath = "Assets/Animations/HardLanding.fbx";
        public const string FallFlatPath = "Assets/Animations/FallFlat.fbx";
        public const string CollapsePath = "Assets/Animations/Collapse.fbx";
        public const string StandingUpPath = "Assets/Animations/StandingUp.fbx";

        private const float BlendInSeconds = 0.1f;
        private const float BlendOutSeconds = 0.35f;
        private const float LieSeconds = 0.5f;
        private const float GetUpSeconds = 1f;
        private const float StandUpCrossfadeSeconds = 0.4f;

        // The stand-up hands back to the game's idle over this long (its last pose and the idle
        // stand a little differently: a calm weight shift, not a slide).
        private const float StandUpBlendOutSeconds = 0.6f;

        // The stand-up waits until the drood has slid to a stop (slower than StillSpeed, m/s,
        // over the ground), at most StillWaitSeconds after it would have begun.
        private const float StillSpeed = 0.1f;

        // Standing Up stands on its heels, toes up, its feet turned out: each foot that is down
        // (its ankle within FootDownHeight, m, above the idle's ankle height, fading out over
        // FootLevelEase more) and under raised hips (at least HipsUpHeight above it, eased over
        // FootLevelEase: not lying) is turned at the ankle to the way the idle holds it
        // (remembered: forward, flat); the body keeps the clip's pose. Smoothed over
        // FootLevelSeconds.
        // Its end is shifted onto where the idle stands the feet, fading in from StandUpShiftFrom
        // to StandUpShiftFull of the clip.
        private const float FootDownHeight = 0.05f;
        private const float HipsUpHeight = 0.5f;
        private const float FootLevelEase = 0.1f;
        private const float StandUpShiftFrom = 0.7f;
        private const float StandUpShiftFull = 0.9f;
        private const float FootLevelSeconds = 0.15f;
        private float _leftFootLevel;
        private float _rightFootLevel;

        // The lift (KeepFeetAboveGround) comes up at once (nothing dips into the ground, not even
        // for a frame) and goes down over LiftDownSeconds (taken raw each frame it would jump as
        // the clip's weight shifts from foot to foot; slower, sliding over bumps would build it
        // up), at most LiftMax (m).
        private const float LiftDownSeconds = 0.1f;
        private const float LiftMax = 0.6f;

        // On steep ground the body may be lifted or let down further: from LiftMax at
        // LiftSteepFrom (degrees) up to LiftMaxSteep (m) at LiftSteepFull (lying on 44° it
        // needed ~0.9 m).
        private const float LiftMaxSteep = 1.2f;
        private const float LiftSteepFrom = 20f;
        private const float LiftSteepFull = 45f;
        private float LiftCap => Mathf.Lerp(LiftMax, LiftMaxSteep, Mathf.InverseLerp(LiftSteepFrom, LiftSteepFull, _slope));
        private const float LiftUpRate = 0.1f;
        private float _lift;

        // Standing Up comes from a character with other proportions: its feet can sink into the
        // ground. While it plays, the pose is lifted so that both ankles stay as high above the
        // ground as the idle holds them (remembered; SoleBelowAnkle, m, until known), never
        // lowered.
        private const float SoleBelowAnkle = 0.08f;

        // Falling flat (the fall, lying, standing up) the whole body is kept out of the ground the
        // same way (the clips' heights come from that character too): the hips and chest
        // TorsoClearance (m) above it, the knees and elbows KneeClearance and ElbowClearance, the
        // hands (wrists) HandClearance and the middle fingertips FingerClearance.
        private const float TorsoClearance = 0.12f;
        private const float KneeClearance = 0.05f;
        private const float ElbowClearance = 0.05f;
        private const float HandClearance = 0.03f;
        private const float FingerClearance = 0.01f;
        private (Transform bone, float clearance)[] _bodyClearance;
        private bool _flat;
        private const float StillWaitSeconds = 20f;
        private bool _standingUp;
        private float _standUpLength;
        private float _standUpWaited;

        // After a collapse the drood lies until its canopy has come down and folded
        // (DroodParachute.CanopyFolded), as it would after any landing, before it stands up.
        private bool _waitForCanopy;

        // Collapsing, the feet stay where they landed and the body falls forward over them: the
        // clip (kept over its centre of mass) slides its feet back as it falls, so the whole pose
        // is shifted forward across the ground by as much, until it lies. Lying, the drood itself
        // slides there under its own velocity (at up to ShiftSlideSpeed, m/s) while the shift
        // shrinks by as much, so the body stays where it fell, the camera follows smoothly and
        // the drood gets up where it lies.
        private const float ShiftSlideSpeed = 1f;
        private bool _keepFeet;
        private bool _feetKept;
        private Vector3 _feetSpot;
        private Vector3 _bodyShift;
        private bool _sliding;
        private Vector3 _slideTotal;
        private Vector3 _slideFrom;
        private DroodParachute _parachute;

        // The fall-flat clip starts this far in (s, frame 6 of 50): before it, the Mixamo clip runs
        // its first steps; from here a last stride, then it drops onto the knees (frame ~14) and
        // falls through onto the belly. Its
        // forward travel isn't baked into the pose (the import's Root Transform Position XZ): the
        // body stays over the drood, whose own speed carries it.
        private const float FallFlatStartSeconds = 0.2f;

        // The collapse clip (Mixamo "Falling From Losing Balance") starts this far in (s, frame
        // 12 of 43): before it, the character drops off a ledge and lands (frame 9, legs
        // straight); from here its knees are already bent ~95° and it crumples forward onto the
        // belly, lying like the fall-flat clip's end (the stand-up follows). Its forward travel
        // isn't baked into the pose, its body kept over the drood by its centre of mass (like
        // the stand-up, so the stand-up begins where it lies).
        private const float CollapseStartSeconds = 0.4f;

        // ... played at this speed (a slower fall forward).
        private const float CollapseSpeed = 0.75f;

        // Its stride before the knees throws the body back and the arms back: until
        // FallFlatDampUntil (s into the clip, frame 13) the spine, neck, head and arms keep only
        // half (FallFlatUpperDamp) of the clip's movement away from the pose at touchdown, the
        // full clip back over FallFlatDampFade.
        private const float FallFlatDampUntil = 0.43f;
        private const float FallFlatDampFade = 0.1f;
        private const float FallFlatUpperDamp = 0.5f;
        private static readonly string[] UpperBodyBones =
        {
            "Spine", "Chest", "UpperChest", "Neck", "Head",
            "LeftShoulder", "LeftElbow", "LeftHand", "RightShoulder", "RightElbow", "RightHand",
        };

        private Transform[] _upperBones;
        private Quaternion[] _touchdownRotations;
        private bool _dampUpper;

        // Hand planting: a hand within PlantReach (m) of the ground is pulled all the way down
        // (fully by PlantFull), resting HandThickness above the surface.
        private const float PlantReach = 0.35f;

        // The ground is looked for from GroundRayAbove (m) above the hand to GroundRayBelow
        // past PlantReach.
        private const float GroundRayAbove = 0.5f;
        private const float GroundRayBelow = 0.5f;

        // The body's own checks (sunk into the ground, floating above it) look from BodyRayAbove
        // above each part to BodyRayBelow below it: lying head-down on a steep slope, parts went
        // more than GroundRayAbove into the hill, their rays started inside it, found no surface
        // and nothing lifted them.
        private const float BodyRayAbove = 1.5f;
        private const float BodyRayBelow = 1.5f;
        private const float PlantFull = 0.12f;
        private const float HandThickness = 0.04f;
        private const float PlantResponse = 12f;

        // The hard landing's hands go down onto the ground over this long (s).
        private const float CrouchHandSeconds = 0.2f;

        // Standing up, the hands leaning on the ground are held StandUpHandRaise (m) higher (the
        // clip's hands, from a character with other proportions, go into it), built up over
        // StandUpHandRaiseSeconds; a hand off the ground isn't planted, so it lets go with it.
        private const float StandUpHandRaise = 0.05f;
        private const float StandUpHandRaiseSeconds = 0.3f;
        private float _handRaise;

        private static AnimationClip _hardLandingClip;
        private static bool _hardLandingLoadAttempted;
        private static AnimationClip _fallFlatClip;
        private static AnimationClip _collapseClip;
        private static bool _collapseLoadAttempted;
        private static bool _fallFlatLoadAttempted;
        private static AnimationClip _standingUpClip;
        private static bool _standingUpLoadAttempted;

        private Animator _animator;
        private FullBodyBipedIK _ik;
        private DroodIKOwner _ikOwner;
        private EvaScript _eva;
        private Transform _leftHand;
        private Transform _rightHand;
        private Transform _hips;
        private Transform _leftAnkle;
        private Transform _rightAnkle;
        private float _leftPlant;
        private float _rightPlant;
        private float _clipWeight;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private PlayableGraph _graph;
        private AnimationPlayableOutput _output;
        private float _time = -1f;
        private float _duration;
        private float _blendOut;
        private bool _rootMotion;

        // The landing clip and, after a fall, the stand-up: their times set each step (so the
        // stand-up starts at its beginning when it fades in), mixed by StandUpCrossfadeSeconds.
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _landing;
        private AnimationClipPlayable _standUp;
        private float _landingStart;
        private float _landingLength;
        private float _landingSpeed = 1f;

        // A really hard landing stays down in the crouch longer: the hard-landing clip's crouch
        // (HoldFrom to HoldTo, clip seconds: thighs about level, the torso far forward, still
        // shifting a little) plays up to 1 + HoldExtra times slower, eased in and out over
        // HoldEase, by how hard the touchdown was: no hold at HoldSeverityFrom of the landing
        // severity (Severity: 1 the soft limit; grade 1 from 1.6), the whole hold from
        // HoldSeverityFull.
        private const float HoldFrom = 0.3f;
        private const float HoldTo = 1.1f;
        private const float HoldEase = 0.15f;
        private const float HoldExtra = 2f;
        private const float HoldSeverityFrom = 1.2f;
        private const float HoldSeverityFull = 1.8f;
        private float _hold;
        private float _clipTime;
        private float _clipEnd;

        /// <summary>The clip's playback speed at <paramref name="clipTime"/> (slower in the held crouch).</summary>
        private float ClipSpeed(float clipTime)
        {
            if (_hold <= 0f)
            {
                return 1f;
            }

            var window = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HoldFrom - HoldEase, HoldFrom + HoldEase, clipTime))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HoldTo - HoldEase, HoldTo + HoldEase, clipTime)));
            return 1f / (1f + HoldExtra * _hold * window);
        }
        private float _standUpAt = -1f;

        public bool Active => _time >= 0f;

        /// <summary>Seconds since the clip began (-1 with none).</summary>
        internal float Elapsed => _time;

        /// <summary>
        /// Ends the clip at once: the drood is airborne again under a reopening canopy (the
        /// hanging pose blends from the clip's last pose).
        /// </summary>
        internal void Abort()
        {
            if (Active)
            {
                End();
            }
        }

        /// <summary>
        /// Lying down after the landing with its legs reaching back past where it landed (falling
        /// flat; collapsing, the feet stay put), until it stands up.
        /// </summary>
        public bool LiesDown => Active && _flat && !_keepFeet;

        /// <summary>
        /// Starts the landing animation for a touchdown at <paramref name="sinkSpeed"/> down and
        /// <paramref name="forwardSpeed"/> forward (m/s, for the log): the clip of
        /// <paramref name="kind"/> (Hard, Flat or Collapse; the hard landing if its clip is
        /// missing). False when no clip could play.
        /// </summary>
        public bool Begin(float sinkSpeed, float forwardSpeed, Kind kind, Vector3 groundNormal)
        {
            if (Active || _animator == null)
            {
                return false;
            }

            var clip = kind == Kind.Flat ? LoadClip(FallFlatPath, ref _fallFlatClip, ref _fallFlatLoadAttempted)
                : kind == Kind.Collapse ? LoadClip(CollapsePath, ref _collapseClip, ref _collapseLoadAttempted)
                : LoadClip(HardLandingPath, ref _hardLandingClip, ref _hardLandingLoadAttempted);
            if (clip == null && kind != Kind.Hard)
            {
                kind = Kind.Hard;
                clip = LoadClip(HardLandingPath, ref _hardLandingClip, ref _hardLandingLoadAttempted);
            }

            // Falling flat and collapsing both end lying on the belly.
            var flat = kind != Kind.Hard;

            if (clip == null)
            {
                return false;
            }

            var standUp = flat ? LoadClip(StandingUpPath, ref _standingUpClip, ref _standingUpLoadAttempted) : null;

            _graph = PlayableGraph.Create("Drood2 Landing");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            _landing = AnimationClipPlayable.Create(_graph, clip);
            _landing.SetApplyFootIK(true);
            _landing.SetSpeed(0);
            _graph.Connect(_landing, 0, _mixer, 0);
            _mixer.SetInputWeight(0, 1f);
            if (standUp != null)
            {
                _standUp = AnimationClipPlayable.Create(_graph, standUp);
                _standUp.SetApplyFootIK(true);
                _standUp.SetSpeed(0);
                _graph.Connect(_standUp, 0, _mixer, 1);
            }

            _mixer.SetInputWeight(1, 0f);

            // Only the pose: the clips' root motion doesn't move the drood.
            _rootMotion = _animator.applyRootMotion;
            _animator.applyRootMotion = false;
            _output = AnimationPlayableOutput.Create(_graph, "Landing", _animator);
            _output.SetSourcePlayable(_mixer);
            _output.SetWeight(0f);
            _graph.Play();

            // Falling flat the clip's last frame (lying) is held LieSeconds, then the drood stands
            // up (the stand-up clip) or blends back over GetUpSeconds without it.
            _landingStart = kind == Kind.Flat ? FallFlatStartSeconds : kind == Kind.Collapse ? CollapseStartSeconds : 0f;
            _landingSpeed = kind == Kind.Collapse ? CollapseSpeed : 1f;
            _hold = kind == Kind.Hard ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HoldSeverityFrom, HoldSeverityFull, Severity(sinkSpeed, forwardSpeed))) : 0f;
            _clipTime = _landingStart;
            _clipEnd = clip.length;
            _landingLength = Mathf.Max(clip.length - _landingStart, 0.1f) / _landingSpeed;
            if (_hold > 0f)
            {
                // Its length in time with the hold (the slower crouch summed in small steps).
                const int steps = 200;
                var piece = (clip.length - _landingStart) / steps;
                var length = 0f;
                for (var i = 0; i < steps; i++)
                {
                    length += piece / ClipSpeed(_landingStart + (i + 0.5f) * piece);
                }

                _landingLength = Mathf.Max(length, 0.1f) / _landingSpeed;
            }
            _standUpAt = standUp != null ? _landingLength + LieSeconds : -1f;
            _standingUp = false;
            _standUpLength = standUp != null ? standUp.length : 0f;
            _standUpWaited = 0f;
            _waitForCanopy = kind == Kind.Collapse;
            _keepFeet = kind == Kind.Collapse;
            _feetKept = false;
            _bodyShift = Vector3.zero;
            _sliding = false;
            _blendOut = standUp != null ? StandUpBlendOutSeconds : flat ? GetUpSeconds : BlendOutSeconds;
            _duration = standUp != null
                ? _standUpAt + standUp.length
                : _landingLength + (flat ? LieSeconds + GetUpSeconds : 0f);
            _time = 0f;
            _dampUpper = kind == Kind.Flat && CaptureUpperBody();
            _flat = flat;
            _groundNormal = groundNormal;
            // Against gravity's up: at touchdown the drood still leans (a flare, a fast landing),
            // and its own up made a lean read as a slope.
            var craft = _eva != null ? _eva.PartScript?.CraftScript : null;
            var gravityUp = craft != null ? -(Vector3)craft.FlightData.GravityFrameNormalized : transform.up;
            _slope = Vector3.Angle(groundNormal, gravityUp);
            _slopeTilt = 0f;
            _leftFootLevel = 0f;
            _rightFootLevel = 0f;
            _lift = 0f;
            _leftPlanted = false;
            _rightPlanted = false;
            Pose();

            _leftPlant = 0f;
            _rightPlant = 0f;
            _handRaise = 0f;
            _clipWeight = 0f;

            DroodLog.Debug($"{(kind == Kind.Flat ? "Fell flat" : kind == Kind.Collapse ? "Collapsed" : "Hard landing")} at {sinkSpeed:0.0} m/s down, {forwardSpeed:0.0} m/s forward ({clip.length:0.00} s clip" +
                $"{(_hold > 0f ? $", crouch held {_hold * 100f:0}%: {_landingLength:0.00} s" : "")}), slope {_slope:0}°.");
            return true;
        }

        private static AnimationClip LoadClip(string path, ref AnimationClip clip, ref bool attempted)
        {
            if (attempted)
            {
                return clip;
            }

            attempted = true;
            try
            {
                // Asking for an AnimationClip on the FBX path returns the clip inside it.
                clip = Assets.Scripts.Mod.Instance?.ResourceLoader?.LoadAsset<AnimationClip>(path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Loading the landing clip '{path}' failed: {e.Message}");
            }

            if (clip == null)
            {
                Debug.LogWarning($"[Drood 2.0] Landing clip '{path}' not found in the mod. Is it listed under Other Assets?");
            }

            return clip;
        }

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _animator = GetComponentInChildren<Animator>(true);
            _ik = GetComponentInChildren<FullBodyBipedIK>(true);
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (_leftHand == null && t.name == "LeftHand")
                {
                    _leftHand = t;
                }
                else if (_rightHand == null && t.name == "RightHand")
                {
                    _rightHand = t;
                }
                else if (_hips == null && t.name == "Hips")
                {
                    _hips = t;
                }
                else if (_leftAnkle == null && t.name == "LeftAnkle")
                {
                    _leftAnkle = t;
                }
                else if (_rightAnkle == null && t.name == "RightAnkle")
                {
                    _rightAnkle = t;
                }
            }
        }

        /// <summary>
        /// Late chain, before the IK solve and after the hanging pose (so this wins): the
        /// Animator has posed the skeleton with the clip, so the hand positions read here are
        /// the clip's.
        /// </summary>
        internal void PreSolve()
        {
            if (!Active)
            {
                RememberIdleFeet();
                return;
            }

            if (_ik == null)
            {
                return;
            }

            _ikOwner = _ikOwner != null ? _ikOwner : GetComponent<DroodIKOwner>();
            if (_ikOwner != null)
            {
                _ikOwner.Take();
            }

            var solver = _ik.solver;
            foreach (var effector in solver.effectors)
            {
                effector.positionWeight = 0f;
                effector.rotationWeight = 0f;
            }

            foreach (var chain in new[] { solver.leftArmChain, solver.rightArmChain, solver.leftLegChain, solver.rightLegChain })
            {
                chain.bendConstraint.weight = 0f;
            }

            DampUpperBody();
            KeepFeetPlanted();
            TiltToSlope();
            TiltStandUp();
            KeepFeetAboveGround();
            PlantFeet(solver);
            var dt = Time.deltaTime;
            _handRaise = Mathf.MoveTowards(_handRaise, _standingUp ? StandUpHandRaise : 0f, StandUpHandRaise * dt / StandUpHandRaiseSeconds);
            PlantHand(solver.leftHandEffector, _leftHand, ref _leftPlant, dt);
            PlantHand(solver.rightHandEffector, _rightHand, ref _rightPlant, dt);
        }

        /// <summary>Standing up, at the end: shifted onto where the idle stands the feet (see FootDownHeight).</summary>
        private void TiltStandUp()
        {
            if (!_standingUp || _standUpLength <= 0f || _hips == null || _leftAnkle == null || _rightAnkle == null)
            {
                return;
            }

            var up = transform.up;
            var progress = (_time - _standUpAt) / _standUpLength;
            var feet = (_leftAnkle.position + _rightAnkle.position) * 0.5f;
            var shiftShare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StandUpShiftFrom, StandUpShiftFull, progress)) * _clipWeight;
            if (_hasIdleFeet && shiftShare > 0f)
            {
                // Onto the idle's feet, level.
                _hips.position += Vector3.ProjectOnPlane(transform.TransformPoint(_idleFeet) - feet, up) * shiftShare;
            }
        }

        /// <summary>Standing up: each foot that is down turned flat, at the idle's angle (see FootDownHeight).</summary>
        private void LevelFeet()
        {
            if (!_standingUp || !_hasIdleFeet || _hips == null)
            {
                _leftFootLevel = 0f;
                _rightFootLevel = 0f;
                return;
            }

            LevelFoot(_leftAnkle, _idleLeftFoot, ref _leftFootLevel);
            LevelFoot(_rightAnkle, _idleRightFoot, ref _rightFootLevel);
        }

        private void LevelFoot(Transform ankle, Quaternion idleRotation, ref float level)
        {
            var up = transform.up;
            var height = GroundHeight(ankle.position, up) - _idleAnkleHeight;
            var down = Mathf.Clamp01((FootDownHeight - height) / FootLevelEase + 1f);
            var raised = Mathf.Clamp01((Vector3.Dot(_hips.position - ankle.position, up) - HipsUpHeight) / FootLevelEase);
            level = Mathf.Lerp(level, down * raised, 1f - Mathf.Exp(-Time.deltaTime / FootLevelSeconds));
            if (level > 0f)
            {
                ankle.rotation = Quaternion.Slerp(ankle.rotation, OnSurface(ankle.position, transform.rotation * idleRotation), level * _clipWeight);
            }
        }

        /// <summary>A point's height (m) above the ground under it (large when no ground is near).</summary>
        private float GroundHeight(Vector3 point, Vector3 up)
        {
            return FindGround(point, up, out var ground, out _) ? Vector3.Dot(point - ground, up) : 10f;
        }

        /// <summary>
        /// Late chain, right after the IK solve: the standing feet turned to the idle's (the IK
        /// would turn them back with the lower legs it moves).
        /// </summary>
        internal void PostSolve()
        {
            if (Active)
            {
                LevelFeet();
            }
        }

        /// <summary>The upper body's pose at touchdown (the last frame's), for DampUpperBody.</summary>
        private bool CaptureUpperBody()
        {
            if (_upperBones == null)
            {
                var bones = new System.Collections.Generic.Dictionary<string, Transform>();
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    if (!bones.ContainsKey(t.name))
                    {
                        bones[t.name] = t;
                    }
                }

                _upperBones = new Transform[UpperBodyBones.Length];
                _touchdownRotations = new Quaternion[UpperBodyBones.Length];
                for (var i = 0; i < UpperBodyBones.Length; i++)
                {
                    bones.TryGetValue(UpperBodyBones[i], out _upperBones[i]);
                }
            }

            for (var i = 0; i < _upperBones.Length; i++)
            {
                if (_upperBones[i] != null)
                {
                    _touchdownRotations[i] = _upperBones[i].localRotation;
                }
            }

            return true;
        }

        /// <summary>
        /// Falling flat, before the knees: the upper body keeps only FallFlatUpperDamp of the
        /// clip's movement away from its pose at touchdown.
        /// </summary>
        private void DampUpperBody()
        {
            if (!_dampUpper || _upperBones == null)
            {
                return;
            }

            var clipTime = _landingStart + _time;
            var damp = (1f - FallFlatUpperDamp) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FallFlatDampUntil, FallFlatDampUntil + FallFlatDampFade, clipTime)));
            if (damp <= 0f)
            {
                return;
            }

            for (var i = 0; i < _upperBones.Length; i++)
            {
                var bone = _upperBones[i];
                if (bone != null)
                {
                    bone.localRotation = Quaternion.Slerp(bone.localRotation, _touchdownRotations[i], damp);
                }
            }
        }

        /// <summary>The pose tilted toward the slope (see SlopeTiltShare), about the ground under the drood.</summary>
        private void TiltToSlope()
        {
            if (_hips == null || _leftAnkle == null || _rightAnkle == null)
            {
                return;
            }

            var up = transform.up;
            FollowSlope(up);
            _neck = _neck != null ? _neck : FindBone("Neck");
            var upright = _flat && _neck != null ? Mathf.Clamp01(Vector3.Dot((_neck.position - _hips.position).normalized, up)) : 1f;
            var target = Mathf.Lerp(_slope, Mathf.Min(_slope * SlopeTiltShare, SlopeTiltMax), upright);
            _slopeTilt = Mathf.Lerp(_slopeTilt, target, 1f - Mathf.Exp(-Time.deltaTime / SlopeTiltSeconds));
            var axis = Vector3.Cross(up, _groundNormal);
            if (axis.sqrMagnitude < 1e-8f)
            {
                return;
            }

            // About the ground under the feet (the drood's origin sits at its hips, out of the
            // ground lookup's reach).
            var feet = _leftAnkle != null && _rightAnkle != null ? (_leftAnkle.position + _rightAnkle.position) * 0.5f : transform.position;
            var pivot = FindGround(feet, up, out var ground, out _) ? ground : feet;
            _hips.RotateAround(pivot, axis.normalized, _slopeTilt * _clipWeight);
        }

        /// <summary>The slope under the drood now, eased (see SlopeProbe).</summary>
        private void FollowSlope(Vector3 up)
        {
            var feet = (_leftAnkle.position + _rightAnkle.position) * 0.5f;
            var forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            var right = Vector3.Cross(up, forward);
            var sum = Vector3.zero;
            for (var i = 0; i < 5; i++)
            {
                // Under the feet, then ahead, behind, right and left (no allocation per frame).
                var offset = i == 0 ? Vector3.zero : (i < 3 ? forward : right) * (i % 2 == 1 ? 1f : -1f);
                if (FindGround(feet + offset * SlopeProbe, up, out _, out var normal))
                {
                    sum += normal;
                }
            }

            if (sum.sqrMagnitude > 1e-6f)
            {
                _groundNormal = Vector3.Slerp(_groundNormal, sum.normalized, 1f - Mathf.Exp(-Time.deltaTime / SlopeFollowSeconds)).normalized;
                _slope = Vector3.Angle(_groundNormal, up);
            }
        }

        /// <summary>Standing up: lifts the pose so the ankles stay above the ground (see SoleBelowAnkle).</summary>
        private void KeepFeetAboveGround()
        {
            if ((!_flat && _slope < LiftSlope) || _hips == null || _leftAnkle == null || _rightAnkle == null)
            {
                return;
            }

            var up = transform.up;
            var lift = Mathf.Max(SinkBelowSole(_leftAnkle, up), SinkBelowSole(_rightAnkle, up));
            if (_bodyClearance == null)
            {
                _bodyClearance = new (Transform, float)[]
                {
                    (_hips, TorsoClearance), (FindBone("Chest"), TorsoClearance),
                    (FindBone("LeftKnee"), KneeClearance), (FindBone("RightKnee"), KneeClearance),
                    (_leftHand, HandClearance), (_rightHand, HandClearance),
                    (FingerTip("LeftMiddleFingerLower"), FingerClearance), (FingerTip("RightMiddleFingerLower"), FingerClearance),
                    (FindBone("LeftElbow"), ElbowClearance), (FindBone("RightElbow"), ElbowClearance),
                };
            }

            foreach (var (bone, clearance) in _bodyClearance)
            {
                if (bone != null)
                {
                    lift = Mathf.Max(lift, SinkBelow(bone.position, clearance, up));
                }
            }

            // Lying on a slope rising ahead, the clip's body (placed for level ground at the drood's
            // feet, then tilted about the ground under its own feet, which lies lower) floats: it
            // is let down until its lowest part rests on the ground (at most LiftCap).
            if (lift <= 0f && _flat && _slope >= LiftSlope)
            {
                var gap = Mathf.Min(GapAbove(_leftAnkle.position, SoleHeight, up), GapAbove(_rightAnkle.position, SoleHeight, up));
                foreach (var (bone, clearance) in _bodyClearance)
                {
                    if (bone != null)
                    {
                        gap = Mathf.Min(gap, GapAbove(bone.position, clearance, up));
                    }
                }

                if (gap < float.MaxValue)
                {
                    lift = -Mathf.Min(gap, LiftCap);
                }
            }

            lift = Mathf.Min(lift, LiftCap);
            if (lift >= _lift)
            {
                // While the stand-up crossfades in, the lift eases up (LiftUpRate) so the lying
                // body doesn't hop as the new clip's limbs settle.
                var crossfading = _standingUp && _time - _standUpAt < StandUpCrossfadeSeconds;
                _lift = crossfading ? Mathf.MoveTowards(_lift, lift, LiftUpRate * Time.deltaTime) : lift;
            }
            else
            {
                _lift = Mathf.Lerp(_lift, lift, 1f - Mathf.Exp(-Time.deltaTime / LiftDownSeconds));
            }
            if (_lift != 0f)
            {
                _hips.position += up * (_lift * _clipWeight);
            }
        }

        /// <summary>How far (m) an ankle is below its sole's height above the ground (0 when not).</summary>
        /// <summary>How far (m) a point is below <paramref name="clearance"/> above the ground (0 when not).</summary>
        private float SinkBelow(Vector3 point, float clearance, Vector3 up)
        {
            return FindGround(point, up, out var ground, out _, BodyRayAbove, BodyRayBelow) ? Mathf.Max(clearance - Vector3.Dot(point - ground, up), 0f) : 0f;
        }

        /// <summary>How far (m) a point is above <paramref name="clearance"/> over the ground (MaxValue with no ground found).</summary>
        private float GapAbove(Vector3 point, float clearance, Vector3 up)
        {
            return FindGround(point, up, out var ground, out _, BodyRayAbove, BodyRayBelow) ? Mathf.Max(Vector3.Dot(point - ground, up) - clearance, 0f) : float.MaxValue;
        }

        private float SoleHeight => _hasIdleFeet ? _idleAnkleHeight : SoleBelowAnkle;

        /// <summary>The last bone of a finger (from its first, <paramref name="name"/>).</summary>
        private Transform FingerTip(string name)
        {
            var bone = FindBone(name);
            while (bone != null && bone.childCount > 0)
            {
                bone = bone.GetChild(0);
            }

            return bone;
        }

        private Transform FindBone(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }

        private float SinkBelowSole(Transform ankle, Vector3 up)
        {
            if (!FindGround(ankle.position, up, out var ground, out _, BodyRayAbove, BodyRayBelow))
            {
                return 0f;
            }

            var sole = _hasIdleFeet ? _idleAnkleHeight : SoleBelowAnkle;
            return Mathf.Max(sole - Vector3.Dot(ankle.position - ground, up), 0f);
        }

        // Where the game's idle stands the feet (their middle, in the drood's own space, level),
        // remembered whenever the drood stands still on the ground without a clip (slower than
        // StillSpeed, eased over IdleFeetSeconds): the end of Standing Up is shifted onto it, so
        // the feet don't move in the hand-over to the idle.
        private const float IdleFeetSeconds = 0.5f;
        private Vector3 _idleFeet;
        private Vector3 _idleLeftAnkle;
        private Vector3 _idleRightAnkle;
        private float _idleAnkleHeight = SoleBelowAnkle;
        private Quaternion _idleLeftFoot = Quaternion.identity;
        private Quaternion _idleRightFoot = Quaternion.identity;
        private bool _hasIdleFeet;

        // At the end of standing up, each foot is planted once it stands (from PlantFrom of the
        // clip, its ankle within PlantFootTolerance, m, of the idle's ankle height and the hips at
        // least PlantHipsAbove above it: not a shin lying on the ground while getting up) and held
        // there by the IK, eased in over PlantEaseSeconds, so the animation and the corrections
        // above don't shuffle it; from StandUpShiftFrom to StandUpShiftFull of the clip the planted
        // feet move once, smoothly, onto the idle's spots, and stay there through the hand-over
        // (the idle then has its feet where they already are).
        private const float PlantFrom = 0.6f;
        private const float PlantFootTolerance = 0.03f;
        private const float PlantHipsAbove = 0.6f;

        // On a slope a foot within PlantSlopeReach (m) of its standing height is brought down (or
        // up) to it; the hard landing crouches, its hips at least PlantCrouchHipsAbove over a foot.
        private const float PlantSlopeReach = 0.25f;
        private const float PlantCrouchHipsAbove = 0.3f;
        private const float PlantEaseSeconds = 0.2f;
        private bool _leftPlanted;
        private bool _rightPlanted;
        private Vector3 _leftFootSpot;
        private Vector3 _rightFootSpot;
        private float _leftPlantWeight;
        private float _rightPlantWeight;

        private void PlantFeet(IKSolverFullBodyBiped solver)
        {
            // Standing up: from PlantFrom of the clip, held, moving onto the idle's spots; the
            // hard landing on a slope: throughout, following the clip's feet (FootOnGround).
            var hardOnSlope = !_flat && _slope >= LiftSlope;
            var progress = _standingUp ? (_time - _standUpAt) / _standUpLength : 0f;
            if (_leftAnkle == null || _rightAnkle == null || !_hasIdleFeet || !((_standingUp && progress >= PlantFrom) || hardOnSlope))
            {
                _leftPlanted = false;
                _rightPlanted = false;
                _leftPlantWeight = 0f;
                _rightPlantWeight = 0f;
                return;
            }

            var up = transform.up;
            if (!_standingUp)
            {
                // The hard landing on a slope: each foot where the clip has it, only brought up or
                // down onto the ground under it (held at a spot instead, the clip's feet moving on
                // under the held ones folded one leg under the body and left the other stretched
                // back: kneeling on all fours instead of the squat).
                FootOnGround(solver.leftFootEffector, _leftAnkle, ref _leftPlantWeight, up);
                FootOnGround(solver.rightFootEffector, _rightAnkle, ref _rightPlantWeight, up);
                return;
            }

            var toIdle = _standingUp
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StandUpShiftFrom, StandUpShiftFull, progress))
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_duration - _blendOut, _duration, _time));
            PlantFoot(solver.leftFootEffector, _leftAnkle, ref _leftPlanted, ref _leftFootSpot, ref _leftPlantWeight, transform.TransformPoint(_idleLeftAnkle), transform.rotation * _idleLeftFoot, toIdle, up);
            PlantFoot(solver.rightFootEffector, _rightAnkle, ref _rightPlanted, ref _rightFootSpot, ref _rightPlantWeight, transform.TransformPoint(_idleRightAnkle), transform.rotation * _idleRightFoot, toIdle, up);
        }

        /// <summary>A foot's idle rotation fitted to the ground's slope under <paramref name="point"/>.</summary>
        private Quaternion OnSurface(Vector3 point, Quaternion rotation)
        {
            var up = transform.up;
            return FindGround(point, up, out _, out var normal) ? Quaternion.FromToRotation(up, normal) * rotation : rotation;
        }

        /// <summary>
        /// The clip's foot moved straight onto the ground under it, while the clip has it near its
        /// own ground (within PlantSlopeReach of its standing height, the hips at least
        /// PlantCrouchHipsAbove over it); by position only, eased over PlantEaseSeconds.
        /// </summary>
        private void FootOnGround(IKEffector effector, Transform ankle, ref float weight, Vector3 up)
        {
            var target = 0f;
            if (FindGround(ankle.position, up, out var ground, out _))
            {
                var above = Vector3.Dot(ankle.position - ground, up) - _idleAnkleHeight;
                if (Mathf.Abs(above) < PlantSlopeReach && _hips != null && Vector3.Dot(_hips.position - ankle.position, up) > PlantCrouchHipsAbove)
                {
                    target = 1f;
                    effector.target = null;
                    effector.position = ankle.position - up * above;
                }
            }

            weight = Mathf.MoveTowards(weight, target, Time.deltaTime / PlantEaseSeconds);
            effector.positionWeight = weight;
            effector.rotationWeight = 0f;
        }

        private void PlantFoot(IKEffector effector, Transform ankle, ref bool planted, ref Vector3 spot, ref float weight, Vector3 idleSpot, Quaternion idleRotation, float toIdle, Vector3 up)
        {
            // On a slope a foot this near the ground is brought down to it (one stands lower than
            // the other); on the flat only one already at its standing height.
            var reach = _slope >= LiftSlope ? PlantSlopeReach : PlantFootTolerance;
            var hipsAbove = _flat ? PlantHipsAbove : PlantCrouchHipsAbove;
            if (!planted
                && FindGround(ankle.position, up, out var ground, out _)
                && Mathf.Abs(Vector3.Dot(ankle.position - ground, up) - _idleAnkleHeight) < reach
                && _hips != null && Vector3.Dot(_hips.position - ankle.position, up) > hipsAbove)
            {
                planted = true;

                // Held in the drood's own frame: a drood still sliding takes its planted feet
                // along (held where they touched down, the legs stretched back after it and
                // lifted the feet off the ground).
                spot = transform.InverseTransformPoint(ground + up * _idleAnkleHeight);
            }

            if (!planted)
            {
                return;
            }

            weight = Mathf.MoveTowards(weight, 1f, Time.deltaTime / PlantEaseSeconds);
            effector.target = null;
            effector.position = Vector3.Lerp(transform.TransformPoint(spot), idleSpot, toIdle);
            effector.positionWeight = weight;
            effector.rotation = OnSurface(effector.position, idleRotation);

            // Crouching (the hard landing) the foot's turn stays out of the solve: with a foot
            // rotation FinalIK turns the knee toward the cross of the hip-to-ankle line and the
            // foot's forward, which in a deep squat lie nearly along each other, and the legs
            // twisted, knees sideways. The knees then bend as the clip bends them.
            effector.rotationWeight = _standingUp ? weight : 0f;
        }

        private void RememberIdleFeet()
        {
            if (_leftAnkle == null || _rightAnkle == null || _eva == null || !_eva.IsGrounded)
            {
                return;
            }

            var body = _eva.PartScript?.BodyScript?.RigidBody;
            if (body == null || Vector3.ProjectOnPlane(body.velocity, transform.up).magnitude > StillSpeed)
            {
                return;
            }

            var pose = GetComponent<DroodHangingPose>();
            var runOut = GetComponent<DroodRunOut>();
            if ((pose != null && pose.Weight > 0f) || (runOut != null && runOut.Active))
            {
                return;
            }

            var feet = transform.InverseTransformPoint((_leftAnkle.position + _rightAnkle.position) * 0.5f);
            feet.y = 0f;
            var ease = 1f - Mathf.Exp(-Time.deltaTime / IdleFeetSeconds);
            var left = transform.InverseTransformPoint(_leftAnkle.position);
            var right = transform.InverseTransformPoint(_rightAnkle.position);
            _idleFeet = _hasIdleFeet ? Vector3.Lerp(_idleFeet, feet, ease) : feet;
            _idleLeftAnkle = _hasIdleFeet ? Vector3.Lerp(_idleLeftAnkle, left, ease) : left;
            _idleRightAnkle = _hasIdleFeet ? Vector3.Lerp(_idleRightAnkle, right, ease) : right;
            var leftFoot = Quaternion.Inverse(transform.rotation) * _leftAnkle.rotation;
            var rightFoot = Quaternion.Inverse(transform.rotation) * _rightAnkle.rotation;
            _idleLeftFoot = _hasIdleFeet ? Quaternion.Slerp(_idleLeftFoot, leftFoot, ease) : leftFoot;
            _idleRightFoot = _hasIdleFeet ? Quaternion.Slerp(_idleRightFoot, rightFoot, ease) : rightFoot;

            var ankleHeight = Mathf.Min(GroundHeight(_leftAnkle.position, transform.up), GroundHeight(_rightAnkle.position, transform.up));
            if (ankleHeight < 0.5f)
            {
                _idleAnkleHeight = _hasIdleFeet ? Mathf.Lerp(_idleAnkleHeight, ankleHeight, ease) : ankleHeight;
            }

            _hasIdleFeet = true;
        }

        private void PlantHand(IKEffector effector, Transform hand, ref float plant, float dt)
        {
            if (hand == null)
            {
                return;
            }

            var target = 0f;
            var up = transform.up;
            Vector3 ground = default;
            if (FindGround(hand.position, up, out ground, out var normal))
            {
                var height = Vector3.Dot(hand.position - ground, up);

                // The hard landing's crouch puts its hands down: all the way once within
                // PlantReach (a hand in the slope too, lifted onto it). Lying and getting up, by
                // how near the ground the hand is.
                target = !_flat
                    ? (height < PlantReach ? 1f : 0f)
                    : 1f - Mathf.Clamp01((height - PlantFull) / (PlantReach - PlantFull));
                effector.target = null;
                effector.position = ground + normal * (HandThickness + _handRaise);
            }

            plant = Mathf.MoveTowards(plant, target, (_flat ? PlantResponse : 1f / CrouchHandSeconds) * dt);
            effector.positionWeight = plant * _clipWeight;
        }

        /// <summary>Ground under a point, ignoring the drood and anything on its craft (held parts).</summary>
        private bool FindGround(Vector3 point, Vector3 up, out Vector3 ground, out Vector3 normal) =>
            FindGround(point, up, out ground, out normal, GroundRayAbove, PlantReach + GroundRayBelow);

        /// <summary>Ground under a point, looked for from <paramref name="above"/> above it to <paramref name="below"/> below it (m).</summary>
        private bool FindGround(Vector3 point, Vector3 up, out Vector3 ground, out Vector3 normal, float above, float below)
        {
            ground = default;
            normal = up;
            var count = Physics.RaycastNonAlloc(point + up * above, -up, _hits, above + below, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.distance >= best || hit.collider.transform.IsChildOf(transform) || IsOwnCraft(hit.collider))
                {
                    continue;
                }

                best = hit.distance;
                ground = hit.point;
                normal = hit.normal;
            }

            return best < float.MaxValue;
        }

        private bool IsOwnCraft(Collider collider)
        {
            var craft = _eva?.PartScript?.CraftScript;
            var part = collider.GetComponentInParent<Assets.Scripts.Craft.Parts.PartScript>();
            return craft != null && part != null && ReferenceEquals(part.CraftScript, craft);
        }

        // Called from the modifier's game-loop Update, which runs before the Animator
        // evaluates, so the weight applies this frame.
        internal void Step()
        {
            if (!Active)
            {
                return;
            }

            _time += Time.deltaTime;
            _clipTime = Mathf.Min(_clipTime + Time.deltaTime * _landingSpeed * ClipSpeed(_clipTime), _clipEnd);
            WaitToStandUp(Time.deltaTime);
            if (_time >= _duration)
            {
                End();
                return;
            }

            var blendIn = Mathf.Clamp01(_time / BlendInSeconds);
            var blendOut = Mathf.Clamp01((_duration - _time) / _blendOut);
            _clipWeight = Mathf.Min(blendIn, blendOut);
            _output.SetWeight(_clipWeight);
            Pose();
        }

        /// <summary>
        /// Lying after a fall: the stand-up (and the end) moves on with the time while the drood
        /// still slides over the ground.
        /// </summary>
        private void WaitToStandUp(float dt)
        {
            if (_standUpAt < 0f || _standingUp || _time < _standUpAt)
            {
                return;
            }

            var body = _eva?.PartScript?.BodyScript?.RigidBody;
            var speed = body != null ? Vector3.ProjectOnPlane(body.velocity, transform.up).magnitude : 0f;
            _standUpWaited += dt;
            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            var canopyFolding = _waitForCanopy && _parachute != null && !_parachute.CanopyFolded;
            if ((speed > StillSpeed || canopyFolding) && _standUpWaited < StillWaitSeconds)
            {
                _standUpAt = _time;
                _duration = _standUpAt + _standUpLength;
                return;
            }

            _standingUp = true;
        }

        /// <summary>Collapsing: the pose shifted so the feet stay where they landed (see _keepFeet).</summary>
        private void KeepFeetPlanted()
        {
            if (!_keepFeet || _hips == null || _leftAnkle == null || _rightAnkle == null)
            {
                return;
            }

            // Where the feet landed is kept in the drood's own frame: the drood still sliding on
            // carries them along; only the clip's own sliding back of the feet is undone.
            var feet = (_leftAnkle.position + _rightAnkle.position) * 0.5f;
            if (!_feetKept)
            {
                _feetSpot = transform.InverseTransformPoint(feet);
                _feetKept = true;
            }

            if (!_sliding && !_standingUp && _time < _landingLength)
            {
                _bodyShift = Vector3.ProjectOnPlane(transform.TransformPoint(_feetSpot) - feet, transform.up);
            }
            else if (!_sliding)
            {
                // Lying: the drood slides after its body (PhysicsStep).
                _sliding = true;
                _slideTotal = _bodyShift;
                _slideFrom = transform.position;
                _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
                _parachute?.DroodMoved(_slideTotal.magnitude);
                DroodLog.Debug($"Collapse: the drood slides {_slideTotal.magnitude * 100f:0} cm forward to where it lies.");
            }

            if (_sliding)
            {
                _bodyShift = _slideTotal - SlidDistance() * _slideTotal.normalized;
            }

            _hips.position += _bodyShift;
        }

        /// <summary>How far (m) the drood has slid toward its shifted pose (0.._slideTotal).</summary>
        private float SlidDistance()
        {
            var length = _slideTotal.magnitude;
            return length > 1e-4f ? Mathf.Clamp(Vector3.Dot(transform.position - _slideFrom, _slideTotal / length), 0f, length) : 0f;
        }

        /// <summary>Game-loop FixedUpdate: the drood sliding after its collapsed body (see _keepFeet).</summary>
        internal void PhysicsStep()
        {
            if (!_sliding || !Active)
            {
                return;
            }

            var body = _eva?.PartScript?.BodyScript?.RigidBody;
            var length = _slideTotal.magnitude;
            if (body == null || length < 1e-4f)
            {
                return;
            }

            var up = transform.up;
            var direction = _slideTotal / length;
            var remaining = length - SlidDistance();
            var vertical = up * Vector3.Dot(body.velocity, up);
            var dt = Time.fixedDeltaTime;
            body.velocity = vertical + direction * (remaining > 0.005f ? Mathf.Min(ShiftSlideSpeed, remaining / dt) : 0f);
        }

        /// <summary>The clips' times and mix for the current landing time.</summary>
        private void Pose()
        {
            _landing.SetTime(_hold > 0f ? _clipTime : _landingStart + Mathf.Min(_time, _landingLength) * _landingSpeed);
            if (_standUpAt < 0f)
            {
                return;
            }

            var standing = _time - _standUpAt;
            var share = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(standing / StandUpCrossfadeSeconds));
            _standUp.SetTime(Mathf.Max(standing, 0f));
            _mixer.SetInputWeight(0, 1f - share);
            _mixer.SetInputWeight(1, share);
        }

        private void OnDisable()
        {
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

        private void End()
        {
            _sliding = false;
            _time = -1f;
            if (_animator != null)
            {
                _animator.applyRootMotion = _rootMotion;
            }

            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            // Hands back to the animation: the game's IK values return by themselves
            // (DroodIKOwner). The overall IK weight stays at the game's 1.
        }
    }
}
