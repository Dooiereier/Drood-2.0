using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Flight.Sim;
using RootMotion.FinalIK;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// With the plane built (<see cref="DroodPlaneScenery"/>): the drop zone's jump
    /// from a plane. <see cref="Begin"/> starts a straight, level run at PlaneSpeed (PlanePitch
    /// nose up) along a heading on the planet's surface, the drood sitting in the open door
    /// facing out, legs outside (<see cref="DroodPlaneSitting"/>), its hips on the sill: each
    /// physics step sets its position, velocity and rotation on the run, and the plane is drawn
    /// around it. The forward key slides it out of the door on the plane's run (DoorPush, no
    /// gravity) until its hips reach the sill's edge (HipsInsideEdge out), where it's let go (the timer started);
    /// the plane flies on for FlyOnSeconds and is removed. While in the plane the free-fall
    /// arch, AG1 and the drop zone's touchdown are held off.
    /// </summary>
    public class DroodPlaneRide : MonoBehaviour
    {
        /// <summary>The plane's speed over the ground (m/s).</summary>
        internal const float PlaneSpeed = 40f;

        // The plane flies this many degrees nose up.
        private const float PlanePitch = 4f;

        // Light turbulence: the plane (and the drood in its door) rolls up to TurbulenceRoll and
        // pitches up to TurbulencePitch (degrees), smooth noise at TurbulenceRollRate and
        // TurbulencePitchRate (noise cells a second).
        private const float TurbulenceRoll = 1.5f;
        private const float TurbulencePitch = 0.12f;
        private const float TurbulenceRollRate = 0.65f;
        private const float TurbulencePitchRate = 0.85f;

        // The door of the bundled plane (Assets/Crafts/JumpPlane.xml; plane axes, m: x right, y up,
        // z forward), on its left side: the middle of the opening along the plane, the sill's
        // height and edge there, and how they run along the plane (the edge's turn and the
        // floor's tilt, degrees). Sitting in the middle facing out, square to the edge and tilted
        // with the floor: the hips HipsInsideEdge in from the edge, HipsAboveSill over it; the
        // clip's pose is fitted to that by moving the drood (FitRate of the hips' error a frame).
        private const float DoorMiddle = -1.99f;
        private const float SillHeight = -0.71f;
        private const float SillEdge = -0.64f;
        private const float EdgeTurn = -5.7f;
        private const float FloorTilt = -6f;
        private const float HipsInsideEdge = 0.2f;
        private const float HipsAboveSill = 0.1f;
        private const float FitRate = 0.5f;

        // Seated, before the IK: both thighs laid level and parallel, pointing out of the door,
        // the shins hanging ShinForward degrees out from straight down, swinging ShinSwing
        // either way at ShinSwingRate (a second; the two legs opposite); the hands holding the
        // sill's edge, HandSide to either side of the hips (the left LeftHandOut further), HandInsideEdge in from it and
        // HandAboveSill over it, palm down, the fingers out over the edge HandFingersDown below level.
        // The thighs ThighDown below level, resting on the sill's edge.
        private const float ThighDown = 7f;
        private const float ShinForward = 12f;
        private const float ShinSwing = 10f;
        private const float ShinSwingRate = 0.75f;
        private const float HandSide = 0.24f;
        private const float LeftHandOut = 0.06f;
        private const float HandInsideEdge = 0.05f;
        private const float HandAboveSill = 0.04f;
        private const float HandFingersDown = 35f;
        private const float MaxFit = 1.5f;

        // Out of the door: slid out at this speed (m/s), carried by the plane, let go once the
        // hips reach the sill's edge.
        private const float DoorPush = 1.5f;
        private const float JumpInput = 0.5f;

        private const float FlyOnSeconds = 20f;

        // The propeller's wash: out of the door, until WashDepth below the seat (at most
        // WashSeconds), the air there blows back along the plane's heading WashSpeed faster than
        // the plane flies, so the drood is dragged back harder: the extra drag of WashArea
        // (m²) between its airspeed along the heading plus WashSpeed and without it.
        private const float WashDepth = 2f;
        private const float WashSeconds = 3f;
        private const float WashSpeed = 15f;
        private const float WashArea = 0.6f;

        // The engine: the plane's own engine sound looped, flat (2D, like the wind: the game's
        // first audio listener can be kilometres from the drood after the drop zone's teleport,
        // so a 3D source went unheard), its loudness set here by the game camera's distance from
        // the propeller (full within EngineHeardFrom, then EngineHeardFrom / distance, silent
        // past EngineHeardTo), at EngineLoudness times the mod's sound setting (Wind Volume). On
        // the game's sound mixer group in outside views; in first person past it (the game
        // muffles that group there), at the level the mixer would give it (master and sound
        // volume settings x EngineMixerScale) behind the helmet's low-pass (EngineHelmetCutoff),
        // as the wind's helmet voice (DroodWindSound). A propeller's loop is one blade pass
        // slowed down: played as the game does, at EngineRpm x blades / 360 (at most
        // EngineMaxPitch) times the time scale. It's layered from EngineVoicePitches.Length voices
        // at those shares of the pitch, each starting a share of the clip further on and at
        // 1/sqrt(voices) of the loudness: their repeats never line up, so the loop's rhythm
        // smears into a steady drone at the same loudness. The same again for the loop three
        // times as high (DroodPlaneScenery.EngineHighClip) at EngineHighShare of the loudness.
        private static readonly float[] EngineVoicePitches = { 1f, 0.96f, 1.04f };
        private const float EngineHighShare = 0.5f;
        private const float EngineLoudness = 1f;
        private const float EngineRpm = 2470f;
        private const float EngineMaxPitch = 25f;
        private const float EngineHeardFrom = 6f;
        private const float EngineHeardTo = 600f;
        private const float EngineMixerScale = 0.15f;
        private const float EngineHelmetCutoff = 2500f;

        private EvaScript _eva;
        private DroodPlaneSitting _sitting;
        private GameObject _plane;
        private DroodPlanePropeller[] _propellers;
        private Animator _animator;
        private bool _inPlane;
        private bool _exiting;
        private float _exitProgress;
        private AudioSource _engine;
        private AudioSource[] _engineVoices;
        private AudioLowPassFilter[] _engineHelmets;
        private float[] _engineShares;
        private UnityEngine.Audio.AudioMixerGroup _gameMixer;
        private bool _engineHeld;
        private bool _warpTold;
        private bool _focused = true;
        private float _releasedAt = float.NegativeInfinity;
        private bool _washLogged;
        private string _planet;
        private Vector3d _start;
        private Vector3d _heading;
        private double _radius;
        private double _startTime;
        private Vector3 _hipsTarget;
        private Vector3 _seatLocal;
        private Quaternion _seatFacing = Quaternion.Euler(0f, -90f, 0f);
        private Vector3 _outwardLocal = Vector3.left;
        private float _edgeSlope;
        private float _sillSlope;
        private FullBodyBipedIK _ik;
        private DroodIKOwner _ikOwner;
        private bool _toesReported;

        /// <summary>The drood is in the plane (sitting in the door).</summary>
        internal bool InPlane => _inPlane;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
        }

        private void OnDestroy()
        {
            if (_plane != null)
            {
                Destroy(_plane);
            }
        }

        /// <summary>
        /// A run from <paramref name="start"/> (surface coordinates, the plane's root) along
        /// <paramref name="heading"/> (a surface direction, level).
        /// </summary>
        internal void Begin(IPlanetNode planet, Vector3d start, Vector3d heading)
        {
            if (_plane == null)
            {
                _plane = DroodPlaneScenery.CreateInstance(gameObject.layer);
            }

            if (_plane == null)
            {
                return;
            }

            _plane.SetActive(true);
            _propellers = _plane.GetComponentsInChildren<DroodPlanePropeller>(true);
            StartEngine();

            var edgeSlope = Mathf.Tan(EdgeTurn * Mathf.Deg2Rad);
            var outward = new Vector3(-1f, 0f, edgeSlope).normalized;
            _seatFacing = Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Euler(0f, 0f, FloorTilt);
            _outwardLocal = outward;
            _edgeSlope = edgeSlope;
            _sillSlope = Mathf.Tan(FloorTilt * Mathf.Deg2Rad);
            _hipsTarget = new Vector3(SillEdge, SillHeight + HipsAboveSill, DoorMiddle) - outward * HipsInsideEdge;
            _seatLocal = _hipsTarget;
            _planet = planet.Name;
            _start = start;
            _radius = start.magnitude;
            var up = start.normalized;
            _heading = (heading - up * Vector3d.Dot(heading, up)).normalized;
            _startTime = Time.fixedTimeAsDouble;
            _inPlane = true;
            _exiting = false;
            _exitProgress = 0f;
            _releasedAt = float.NegativeInfinity;
            _warpTold = false;
            DroodLog.Debug($"Plane ride: started at {PlaneSpeed:0} m/s.");
        }

        /// <summary>The engine sound on the plane (at its propeller), playing.</summary>
        private void StartEngine()
        {
            var clip = DroodPlaneScenery.EngineClip;
            if (clip == null)
            {
                return;
            }

            if (_engine == null)
            {
                var host = _propellers != null && _propellers.Length > 0 ? _propellers[0].transform.parent : _plane.transform;
                _gameMixer = Assets.Scripts.Game.Instance?.AudioPlayer?.GetGameMixerGroup();
                var high = DroodPlaneScenery.EngineHighClip;
                var count = EngineVoicePitches.Length * (high != null ? 2 : 1);
                _engineVoices = new AudioSource[count];
                _engineHelmets = new AudioLowPassFilter[count];
                _engineShares = new float[count];
                for (var i = 0; i < _engineVoices.Length; i++)
                {
                    var place = new GameObject("Drood2.PlaneEngine");
                    place.transform.SetParent(host, false);
                    if (_propellers != null && _propellers.Length > 0)
                    {
                        place.transform.localPosition = _propellers[0].transform.localPosition;
                    }

                    var voice = place.AddComponent<AudioSource>();
                    var isHigh = i >= EngineVoicePitches.Length;
                    voice.clip = isHigh ? high : clip;
                    _engineShares[i] = isHigh ? EngineHighShare : 1f;
                    voice.loop = true;
                    voice.playOnAwake = false;
                    voice.spatialBlend = 0f;
                    voice.dopplerLevel = 0f;
                    voice.outputAudioMixerGroup = _gameMixer;
                    _engineVoices[i] = voice;
                    var helmet = place.AddComponent<AudioLowPassFilter>();
                    helmet.cutoffFrequency = EngineHelmetCutoff;
                    helmet.enabled = false;
                    _engineHelmets[i] = helmet;
                }

                _engine = _engineVoices[0];
            }

            _engineHeld = false;
            for (var i = 0; i < _engineVoices.Length; i++)
            {
                var voice = _engineVoices[i];
                voice.volume = 0f;
                if (!voice.isPlaying)
                {
                    voice.time = voice.clip.length * (i % EngineVoicePitches.Length) / EngineVoicePitches.Length;
                    voice.Play();
                }
            }
        }

        /// <summary>Game-loop Update while the game is paused: the engine paused with it.</summary>
        internal void PausedStep() => HoldEngine(true);

        /// <summary>The engine paused at once when the game window loses focus.</summary>
        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            if (!focused)
            {
                HoldEngine(true);
            }
        }

        private void HoldEngine(bool hold)
        {
            if (_engine == null || hold == _engineHeld)
            {
                return;
            }

            _engineHeld = hold;
            foreach (var voice in _engineVoices)
            {
                if (hold)
                {
                    voice.Pause();
                }
                else
                {
                    voice.UnPause();
                }
            }
        }

        private float EngineVolumeNow()
        {
            var setting = Assets.Scripts.ModSettings.Instance?.WindVolume;
            var camera = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera?.NearCamera;
            var distance = camera != null && _engine != null ? Vector3.Distance(camera.transform.position, _engine.transform.position) : 0f;
            var fall = distance > EngineHeardTo ? 0f : Mathf.Min(1f, EngineHeardFrom / Mathf.Max(distance, 0.01f));
            var volume = EngineLoudness * fall * (setting != null ? setting.Value : 1f);

            // First person: past the (muffled) game mixer, at its level, behind the helmet.
            var firstPerson = _eva != null && _eva.IsFpsActive;
            if (_engine != null)
            {
                var output = firstPerson ? null : _gameMixer;
                for (var i = 0; i < _engineVoices.Length; i++)
                {
                    if (_engineVoices[i].outputAudioMixerGroup != output)
                    {
                        _engineVoices[i].outputAudioMixerGroup = output;
                    }

                    if (_engineHelmets[i].enabled != firstPerson)
                    {
                        _engineHelmets[i].enabled = firstPerson;
                    }
                }
            }

            if (firstPerson)
            {
                var audio = Assets.Scripts.Game.Instance?.Settings?.Game?.Audio;
                var master = audio != null ? audio.MasterVolume.Value : 1f;
                var sound = audio != null ? audio.SoundVolume.Value : 1f;
                volume *= Mathf.Clamp01(master * sound * EngineMixerScale);
            }

            return volume;
        }

        /// <summary>The plane's root at <paramref name="time"/> (surface coordinates, level).</summary>
        private Vector3d RootSurface(double time) => (_start + _heading * (PlaneSpeed * (time - _startTime))).normalized * _radius;

        /// <summary>
        /// The plane at <paramref name="time"/> in the craft's frame: its root, rotation and
        /// velocity (false off the planet).
        /// </summary>
        private bool PlaneAt(double time, out Vector3 root, out Quaternion rotation, out Vector3 velocity)
        {
            root = Vector3.zero;
            rotation = Quaternion.identity;
            velocity = Vector3.zero;
            var craft = _eva?.PartScript?.CraftScript;
            if (craft == null || !(craft.CraftNode?.Parent is IPlanetNode planet) || planet.Name != _planet)
            {
                return false;
            }

            const double Step = 0.02;
            var frame = craft.ReferenceFrame;
            var now = RootSurface(time);
            root = (Vector3)frame.PlanetToFramePosition(planet.SurfaceVectorToPlanetVector(now));
            var ahead = (Vector3)frame.PlanetToFramePosition(planet.SurfaceVectorToPlanetVector(RootSurface(time + Step)));
            var up = ((Vector3)frame.PlanetToFrameVector(planet.SurfaceVectorToPlanetVector(now.normalized))).normalized;
            var forward = Vector3.ProjectOnPlane(ahead - root, up);
            if (forward.sqrMagnitude < 1e-10f)
            {
                return false;
            }

            var t = (float)(time - _startTime);
            var roll = (Mathf.PerlinNoise(t * TurbulenceRollRate, 17.3f) * 2f - 1f) * TurbulenceRoll;
            var pitch = (Mathf.PerlinNoise(t * TurbulencePitchRate, 61.7f) * 2f - 1f) * TurbulencePitch;
            rotation = Quaternion.LookRotation(forward, up) * Quaternion.Euler(-PlanePitch + pitch, 0f, roll);
            velocity = (ahead - root) / (float)Step;
            return true;
        }

        /// <summary>Game-loop Update: the sitting pose; the forward key jumps out.</summary>
        internal void InputStep()
        {
            _sitting = _sitting != null ? _sitting : GetComponent<DroodPlaneSitting>();
            _sitting?.Step(_inPlane && !_exiting);

            // No time warp in the plane: the ride holds the drood on the plane's path each physics
            // step, and under warp (worse with the window away a while) the game's own position for
            // it drifts off, moving the drood far away once it's let go (seen: ~125 km in 25 steps).
            var time = Assets.Scripts.Game.Instance?.FlightScene?.TimeManager;
            if (_inPlane && time?.CurrentMode != null && time.CurrentMode.TimeMultiplier > 1.0)
            {
                time.SetNormalSpeedMode();
                if (!_warpTold)
                {
                    _warpTold = true;
                    Assets.Scripts.Game.Instance?.FlightScene?.FlightSceneUI?.FlightLog?.AddLog(
                        "Drop Zone: time warp is off while in the plane.", ModApi.Flight.UI.FlightLogEntryCategory.Default, false, _eva.PartScript);
                }
            }
            HoldEngine(!_focused);
            if (_engine != null)
            {
                var volume = EngineVolumeNow() / Mathf.Sqrt(EngineVoicePitches.Length);
                var pitch = DroodPlaneScenery.EngineBlades > 0 ? Mathf.Min(EngineRpm * DroodPlaneScenery.EngineBlades / 360f, EngineMaxPitch) * Time.timeScale : Time.timeScale;
                for (var i = 0; i < _engineVoices.Length; i++)
                {
                    _engineVoices[i].volume = volume * _engineShares[i];
                    _engineVoices[i].pitch = pitch * EngineVoicePitches[i % EngineVoicePitches.Length];
                }
            }

            var controls = _eva?.PartScript?.CommandPod?.Controls;
            if (!_inPlane || _exiting || controls == null || !_eva.IsPlayerCraft || controls.EvaMoveFwdAft < JumpInput)
            {
                return;
            }

            _exiting = true;
            _exitProgress = 0f;
            DroodLog.Debug("Plane ride: sliding out of the door.");
        }

        /// <summary>Clear of the sill: let go with the plane's speed and the slide's (the timer started).</summary>
        private void LetGo(Rigidbody body, Vector3 velocity, Vector3 slide)
        {
            _inPlane = false;
            _exiting = false;
            _releasedAt = Time.time;
            _washLogged = false;
            body.velocity = velocity + slide;
            DroodLog.Debug($"Plane ride: out of the door at {slide.magnitude:0.0} m/s from the plane.");
            GetComponent<DroodDropZone>()?.ExitedPlane();
        }

        /// <summary>Game-loop FixedUpdate, last: the drood rides along in the door.</summary>
        internal void PhysicsStep()
        {
            if (!_inPlane)
            {
                StepWash();
                return;
            }

            var part = _eva?.PartScript;
            var body = part?.BodyScript?.RigidBody;
            if (body == null || !_eva.EvaActive || !PlaneAt(Time.fixedTimeAsDouble, out var root, out var rotation, out var velocity))
            {
                End("the drood left the planet or its EVA");
                return;
            }

            var pod = part.CommandPod;
            if (pod != null && pod.GetActivationGroupState(DroodParachute.ActivationGroup))
            {
                pod.SetActivationGroupState(DroodParachute.ActivationGroup, false);
            }

            // Sliding out: along the door's outward direction, level, at DoorPush.
            var outward = new Vector3(_outwardLocal.x, 0f, _outwardLocal.z).normalized;
            var slide = Vector3.zero;
            if (_exiting)
            {
                _exitProgress += DoorPush * Time.fixedDeltaTime;
                slide = rotation * outward * DoorPush;
                if (_exitProgress >= HipsInsideEdge)
                {
                    LetGo(body, velocity, slide);
                    return;
                }
            }

            body.position = root + rotation * (_seatLocal + outward * _exitProgress);
            body.velocity = velocity + slide;
            body.rotation = rotation * _seatFacing;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Late step: the clip's hips fitted onto the sill (the seat moved by their error), the
        /// plane drawn around the drood, or flying on after the exit.
        /// </summary>
        internal void PlaceStep()
        {
            if (_plane == null)
            {
                return;
            }

            if (!_inPlane && Time.time - _releasedAt > FlyOnSeconds)
            {
                End(null);
                return;
            }

            if (_propellers != null)
            {
                foreach (var propeller in _propellers)
                {
                    propeller.Turn(Time.deltaTime);
                }
            }

            if (_inPlane)
            {
                // Around the drood as drawn: the cabin stays put against it.
                var seated = transform.rotation * Quaternion.Inverse(_seatFacing);
                var planeRoot = transform.position - seated * (_seatLocal + new Vector3(_outwardLocal.x, 0f, _outwardLocal.z).normalized * _exitProgress);
                _plane.transform.SetPositionAndRotation(planeRoot, seated);
                _animator = _animator != null ? _animator : GetComponentInChildren<Animator>();
                var hips = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (hips != null && !_exiting)
                {
                    var error = _hipsTarget - Quaternion.Inverse(seated) * (hips.position - planeRoot);
                    _seatLocal = _hipsTarget + Vector3.ClampMagnitude(_seatLocal + error * FitRate - _hipsTarget, MaxFit);
                }

                return;
            }

            if (PlaneAt(Time.timeAsDouble, out var root, out var rotation, out _))
            {
                _plane.transform.SetPositionAndRotation(root, rotation);
            }
        }

        /// <summary>Late chain, before the IK solve: seated, the thighs laid out and the hands on the edge.</summary>
        internal void PreSolve()
        {
            if (!_inPlane || _exiting)
            {
                return;
            }

            _animator = _animator != null ? _animator : GetComponentInChildren<Animator>();
            _ik = _ik != null ? _ik : GetComponentInChildren<FullBodyBipedIK>(true);
            if (_animator == null || !_animator.isHuman || _ik == null)
            {
                return;
            }

            var seated = transform.rotation * Quaternion.Inverse(_seatFacing);
            var planeRoot = transform.position - seated * _seatLocal;
            var outward = seated * new Vector3(_outwardLocal.x, 0f, _outwardLocal.z).normalized;
            LayThigh(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, outward);
            LayThigh(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, outward);
            var down = -(seated * Vector3.up);
            var swing = Mathf.Sin(Time.time * ShinSwingRate * 2f * Mathf.PI) * ShinSwing;
            HangShin(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, down, outward, ShinForward + swing);
            HangShin(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, down, outward, ShinForward - swing);
            MatchLeftFoot(outward);

            // The legs as laid out (no foot targets or knee goals pulling them back), the hands
            // reaching only with the arms (no pull on the body).
            _ikOwner = _ikOwner != null ? _ikOwner : GetComponent<DroodIKOwner>();
            _ikOwner?.Take();
            var solver = _ik.solver;
            foreach (var effector in solver.effectors)
            {
                effector.positionWeight = 0f;
                effector.rotationWeight = 0f;
            }

            solver.leftLegChain.bendConstraint.weight = 0f;
            solver.rightLegChain.bendConstraint.weight = 0f;
            solver.leftArmChain.pull = 0f;
            solver.rightArmChain.pull = 0f;
            var up = seated * Vector3.up;
            var fingers = Quaternion.AngleAxis(HandFingersDown, Vector3.Cross(up, outward)) * outward;
            var side = Vector3.Cross(Vector3.up, _outwardLocal).normalized;
            HoldEdge(solver.leftHandEffector, true, _hipsTarget.z - side.z * (HandSide + LeftHandOut), seated, planeRoot, fingers, up);
            HoldEdge(solver.rightHandEffector, false, _hipsTarget.z + side.z * HandSide, seated, planeRoot, fingers, up);
        }

        private void LayThigh(HumanBodyBones upper, HumanBodyBones lower, Vector3 outward)
        {
            var thigh = _animator.GetBoneTransform(upper);
            var knee = _animator.GetBoneTransform(lower);
            if (thigh == null || knee == null)
            {
                return;
            }

            var down = -(transform.rotation * Quaternion.Inverse(_seatFacing) * Vector3.up);
            var resting = Quaternion.AngleAxis(ThighDown, Vector3.Cross(outward, down)) * outward;
            thigh.rotation = Quaternion.FromToRotation(knee.position - thigh.position, resting) * thigh.rotation;
        }

        /// <summary>
        /// A shin hanging from its knee <paramref name="forward"/> degrees out of the door from
        /// straight down (the clip's legs swing too far and unevenly).
        /// </summary>
        private void HangShin(HumanBodyBones lower, HumanBodyBones foot, Vector3 down, Vector3 outward, float forward)
        {
            var knee = _animator.GetBoneTransform(lower);
            var ankle = _animator.GetBoneTransform(foot);
            if (knee == null || ankle == null)
            {
                return;
            }

            var wanted = Quaternion.AngleAxis(forward, Vector3.Cross(down, outward)) * down;
            knee.rotation = Quaternion.FromToRotation(ankle.position - knee.position, wanted) * knee.rotation;
        }

        /// <summary>
        /// The left foot at the right one's angle to its shin (mirrored): the right toes' direction
        /// in the right leg's own frame (along the shin, out of the door, sideways), the left toes
        /// turned onto it in the left leg's frame, sideways mirrored.
        /// </summary>
        private void MatchLeftFoot(Vector3 outward)
        {
            var leftKnee = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var leftToes = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
            var rightKnee = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            var rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var rightToes = _animator.GetBoneTransform(HumanBodyBones.RightToes);
            if (leftKnee == null || leftFoot == null || leftToes == null || rightKnee == null || rightFoot == null || rightToes == null)
            {
                if (!_toesReported)
                {
                    _toesReported = true;
                    DroodLog.Debug("Plane ride: no toe bones in the drood's avatar; the left foot isn't matched.");
                }

                return;
            }

            var rightDown = (rightFoot.position - rightKnee.position).normalized;
            var rightOut = Vector3.ProjectOnPlane(outward, rightDown).normalized;
            var rightSide = Vector3.Cross(rightDown, rightOut);
            var toes = rightToes.position - rightFoot.position;
            var inLeg = new Vector3(Vector3.Dot(toes, rightSide), Vector3.Dot(toes, rightDown), Vector3.Dot(toes, rightOut));

            var leftDown = (leftFoot.position - leftKnee.position).normalized;
            var leftOut = Vector3.ProjectOnPlane(outward, leftDown).normalized;
            var leftSide = Vector3.Cross(leftDown, leftOut);
            var wanted = -inLeg.x * leftSide + inLeg.y * leftDown + inLeg.z * leftOut;
            leftFoot.rotation = Quaternion.FromToRotation(leftToes.position - leftFoot.position, wanted) * leftFoot.rotation;
        }

        /// <summary>A hand on the sill's edge at <paramref name="z"/> (plane axes): palm down, the fingers over it.</summary>
        private void HoldEdge(IKEffector effector, bool left, float z, Quaternion seated, Vector3 planeRoot, Vector3 fingers, Vector3 up)
        {
            var hand = effector.bone;
            var middle = _animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var index = _animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var little = _animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (hand == null || middle == null || index == null || little == null)
            {
                return;
            }

            var edge = new Vector3(SillEdge + _edgeSlope * (z - DoorMiddle), SillHeight + _sillSlope * (z - DoorMiddle), z);
            var local = edge - _outwardLocal * HandInsideEdge + Vector3.up * HandAboveSill;
            effector.position = planeRoot + seated * local;
            effector.positionWeight = 1f;

            // The hand's own axes now (fingers; the back of the hand from across the knuckles),
            // turned onto the wanted ones.
            var toFingers = middle.position - hand.position;
            var across = index.position - little.position;
            var back = left ? Vector3.Cross(toFingers, across) : Vector3.Cross(across, toFingers);
            if (toFingers.sqrMagnitude < 1e-8f || back.sqrMagnitude < 1e-8f)
            {
                return;
            }

            var now = Quaternion.LookRotation(toFingers, back);
            var wanted = Quaternion.LookRotation(fingers, up);
            effector.rotation = wanted * Quaternion.Inverse(now) * hand.rotation;
            effector.rotationWeight = 1f;
        }

        /// <summary>The propeller's wash dragging the drood back just out of the door (see WashSpeed).</summary>
        private void StepWash()
        {
            if (_plane == null || Time.time - _releasedAt > WashSeconds)
            {
                return;
            }

            var craft = _eva?.PartScript?.CraftScript;
            var body = _eva?.PartScript?.BodyScript?.RigidBody;
            if (craft == null || body == null || !PlaneAt(Time.fixedTimeAsDouble, out var root, out var rotation, out var velocity))
            {
                return;
            }

            var up = -(Vector3)craft.FlightData.GravityFrameNormalized;
            var depth = Vector3.Dot(root + rotation * _seatLocal - body.position, up);
            if (depth > WashDepth)
            {
                if (!_washLogged)
                {
                    _washLogged = true;
                    DroodLog.Debug($"Plane ride: out of the propeller's wash {Time.time - _releasedAt:0.00} s after the exit.");
                }

                return;
            }

            var heading = Vector3.ProjectOnPlane(velocity, up).normalized;
            var airspeed = Mathf.Max(Vector3.Dot((Vector3)craft.SurfaceVelocity, heading), 0f);
            var density = (float)craft.AtmosphereSample.AirDensity;
            var extra = 0.5f * density * WashArea * ((airspeed + WashSpeed) * (airspeed + WashSpeed) - airspeed * airspeed);
            body.AddForce(-heading * (extra * ModApi.Constants.MassScale), ForceMode.Force);
        }

        /// <summary>The run ends: the drood is let go, the plane removed.</summary>
        internal void End(string reason)
        {
            if (reason != null && _inPlane)
            {
                DroodLog.Debug($"Plane ride: ended ({reason}).");
            }

            _inPlane = false;
            _exiting = false;
            _exitProgress = 0f;
            if (_plane != null)
            {
                Destroy(_plane);
                _plane = null;
            }
        }
    }
}
