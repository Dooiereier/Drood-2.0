using RootMotion.FinalIK;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Drood 2.0's flight behaviours and the order they run in each frame. Created by
    /// <see cref="Drood2Script"/> in flight. They don't need Harmony: without the Juno Harmony
    /// mod only the patched features are missing (limb attach points, tethers, free look under
    /// the canopy, the landing freeze).
    ///
    /// Everything runs from Juno's game loop: nothing runs while the game is paused, and the
    /// physics step doesn't run while the craft is on rails in high time warp. Each step runs
    /// on its own (<see cref="Guarded"/>): an exception in one is logged once and the others
    /// still run (the game loop would otherwise skip the rest of the chain every frame).
    /// </summary>
    internal sealed class Drood2FlightBehaviours
    {
        private readonly DroodLimbFollower _limbFollower;
        private readonly DroodLookUp _lookUp;
        private readonly DroodLandingFall _landingFall;
        private readonly DroodHangingPose _hangingPose;
        private readonly DroodCalmAnimator _calmAnimator;
        private readonly DroodRunOut _runOut;
        private readonly DroodVictory _victory;
        private readonly DroodFreeFall _freeFall;
        private readonly DroodFreeFallPose _freeFallPose;
        private readonly DroodPilotThrow _pilotThrow;
        private readonly DroodFirstPersonBody _firstPersonBody;
        private readonly DroodIKOwner _ikOwner;
        private readonly FullBodyBipedIK _ik;
        private readonly DroodParachute _parachute;
        private readonly DroodWindSound _windSound;
        private readonly DroodDeploySound _deploySound;
        private readonly DroodAudibleAltimeter _audible;
        private readonly DroodFieldOfView _fieldOfView;
        private readonly DroodChaseCamera _chaseCamera;
        private readonly DroodDropZone _dropZone;
        private readonly DroodSwoopCourse _swoop;
        private readonly DroodPlaneRide _planeRide;
        private readonly DroodCourseCamera _courseCamera;
        private readonly Guarded[] _fixedSteps;
        private readonly Guarded[] _updateSteps;
        private readonly Guarded[] _lateSteps;
        private readonly Guarded[] _pausedSteps;
        private readonly Guarded[] _releaseLateSteps;

        public Drood2FlightBehaviours(GameObject drood, Drood2Data data)
        {
            _limbFollower = GetOrAdd<DroodLimbFollower>(drood);
            _lookUp = GetOrAdd<DroodLookUp>(drood);
            _landingFall = GetOrAdd<DroodLandingFall>(drood);
            _hangingPose = GetOrAdd<DroodHangingPose>(drood);
            _parachute = GetOrAdd<DroodParachute>(drood);
            var eva = drood.GetComponent<Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript>();
            _parachute.SetSavedState(data);
            _limbFollower.SetGrips(new DroodHeldGrips(data));
            _ik = drood.GetComponentInChildren<FullBodyBipedIK>(true);
            _ikOwner = GetOrAdd<DroodIKOwner>(drood);
            _calmAnimator = GetOrAdd<DroodCalmAnimator>(drood);
            _runOut = GetOrAdd<DroodRunOut>(drood);
            _victory = GetOrAdd<DroodVictory>(drood);
            _freeFall = GetOrAdd<DroodFreeFall>(drood);
            _freeFallPose = GetOrAdd<DroodFreeFallPose>(drood);
            _pilotThrow = GetOrAdd<DroodPilotThrow>(drood);
            _firstPersonBody = GetOrAdd<DroodFirstPersonBody>(drood);
            _windSound = GetOrAdd<DroodWindSound>(drood);
            _deploySound = GetOrAdd<DroodDeploySound>(drood);
            _audible = GetOrAdd<DroodAudibleAltimeter>(drood);
            _fieldOfView = GetOrAdd<DroodFieldOfView>(drood);
            _chaseCamera = GetOrAdd<DroodChaseCamera>(drood);
            _dropZone = GetOrAdd<DroodDropZone>(drood);
            _dropZone.SetData(data);
            _swoop = GetOrAdd<DroodSwoopCourse>(drood);
            _swoop.SetData(data);
            _planeRide = GetOrAdd<DroodPlaneRide>(drood);
            GetOrAdd<DroodPlaneSitting>(drood);
            _courseCamera = GetOrAdd<DroodCourseCamera>(drood);

            // The orders below are explained on FixedUpdate, Update and LateUpdate.
            var release = drood.GetComponent<Drood2Script>();
            _fixedSteps = new[]
            {
                new Guarded("release (before physics)", release.ReleaseBeforePhysics),
                new Guarded("parachute physics", _parachute.PhysicsStep),
                new Guarded("hanging pose physics", _hangingPose.PhysicsStep),
                new Guarded("run-out physics", _runOut.PhysicsStep),
                new Guarded("landing fall physics", _landingFall.PhysicsStep),
                new Guarded("free fall", _freeFall.PhysicsStep),
                new Guarded("air interpolation", _parachute.InterpolateInAir),
                new Guarded("plane ride", _planeRide.PhysicsStep),
                new Guarded("swoop course", _swoop.PhysicsStep),
                new Guarded("course camera craft", _courseCamera.PhysicsStep),
            };
            _releaseLateSteps = new[]
            {
                new Guarded("release (before poses)", release.ReleaseBeforePoses),
            };
            _updateSteps = new[]
            {
                new Guarded("release (before update)", release.ReleaseBeforeUpdate),
                new Guarded("parachute input", _parachute.InputStep),
                new Guarded("steering overlay", () => DroodSteeringOverlay.Step(_parachute, eva, _freeFall)),
                new Guarded("flare cue", () => DroodFlareIndicator.Step(_parachute, eva)),
                new Guarded("plane ride input", _planeRide.InputStep),
                new Guarded("drop zone", _dropZone.Step),
                new Guarded("swoop course (start)", _swoop.Step),
                new Guarded("course camera (early aim)", _courseCamera.EarlyStep),
                new Guarded("look-up", _lookUp.Step),
                new Guarded("first-person field of view", _fieldOfView.Step),
                new Guarded("landing fall", _landingFall.Step),
                new Guarded("victory", _victory.Step),
                new Guarded("hanging pose", _hangingPose.Step),
                new Guarded("calm animator", _calmAnimator.Step),
                new Guarded("run-out animation", _runOut.AnimatorStep),
                new Guarded("wind sound", () => _windSound.Step(false)),
                new Guarded("opening sound", () => _deploySound.Step(false)),
                new Guarded("audible altimeter", () => _audible.Step(false)),
                new Guarded("release (after update)", release.ReleaseAfterUpdate),
            };
            _pausedSteps = new[]
            {
                new Guarded("wind sound (paused)", () => _windSound.Step(true)),
                new Guarded("opening sound (paused)", () => _deploySound.Step(true)),
                new Guarded("audible altimeter (paused)", () => _audible.Step(true)),
                new Guarded("plane engine (paused)", _planeRide.PausedStep),
                new Guarded("steering overlay (paused)", () => DroodSteeringOverlay.Step(null, null, null)),
            };
            _lateSteps = new[]
            {
                new Guarded("hanging pose (hold body)", _hangingPose.HoldBody),
                new Guarded("IK owner (begin)", _ikOwner.BeginFrame),
                new Guarded("free-fall pose", _freeFallPose.PreSolve),
                new Guarded("pilot chute throw", _pilotThrow.PreSolve),
                new Guarded("hanging pose (IK)", _hangingPose.PreSolve),
                new Guarded("landing fall (IK)", _landingFall.PreSolve),
                new Guarded("plane ride (pose)", _planeRide.PreSolve),
                new Guarded("IK owner (solve)", _ikOwner.BeforeSolve),
                new Guarded(DroodProfiler.MovedStep, SolveIK),
                new Guarded("free-fall pose (after IK)", _freeFallPose.PostSolve),
                new Guarded("landing fall (after IK)", _landingFall.PostSolve),
                new Guarded("victory (arms)", _victory.PostSolve),
                new Guarded("hanging pose (after IK)", _hangingPose.PostSolve),
                new Guarded("first-person body", _firstPersonBody.Step),
                new Guarded("held parts", _limbFollower.Step),
                new Guarded("parachute visuals", _parachute.VisualStep),
                new Guarded("canopy chase camera", _chaseCamera.Step),
                new Guarded("course camera", _courseCamera.Step),
                new Guarded("drop zone target", _dropZone.PlaceRings),
                new Guarded("swoop course marks", _swoop.PlaceMarks),
                new Guarded("plane ride", _planeRide.PlaceStep),
            };
        }

        /// <summary>Whether the parachute behaviour exists (it advertises AG1).</summary>
        public bool HasParachute => _parachute != null;

        /// <summary>Game-loop FixedUpdate; runs after EvaScript's (verified in-game).</summary>
        public void FixedUpdate()
        {
            Run(_fixedSteps);
        }

        /// <summary>
        /// Game-loop Update, after EvaScript's. Input and state first; the landing animation
        /// before the hanging pose, which yields to it; last the calm animator values, over the
        /// ones EvaScript just set; then the sounds.
        /// </summary>
        public void Update()
        {
            DroodProfiler.Frame(CurrentPhase());
            Run(_updateSteps);
        }

        /// <summary>Game-loop Update while the game is paused: the sounds fall silent.</summary>
        public void UpdatePaused()
        {
            DroodProfiler.Frame(DroodProfiler.Phase.None);
            Run(_pausedSteps);
        }

        /// <summary>
        /// The late chain: the one place the drood's final pose is built each frame, in this
        /// order. Runs from the game-loop LateUpdate (and its paused variant), after the
        /// Animator has posed the skeleton.
        ///
        /// 0. The game's own IK values on the solver while the mod holds it (DroodIKOwner), so
        ///    the poses write on top of them and leave nothing behind when they let go.
        /// 1. IK targets for exactly one mode: the free-fall arch, the landing clip plants the
        ///    hands, or the hanging pose grips the risers, or nothing (walking; or seated, where
        ///    the game's seat IK has written its targets in its own FlightUpdate); the pilot
        ///    chute throw on top of the arch.
        /// 2. The IK solve. FinalIK's UpdateSolverExternal solves now and stops the component
        ///    solving on its own later in the frame (verified in-game). It does nothing while
        ///    the game has the IK disabled.
        /// 3. On the solved pose: the arch's head lift, the hanging pose's hand twist and pose
        ///    blend, the first-person body, held parts on their bones, then the canopy and lines
        ///    (from the final shoulders), then the chase camera's view from the wing as drawn
        ///    this frame (from Update it trailed the wing by a frame, the lag varying with the
        ///    frame time: the wing jittered in the view).
        /// </summary>
        public void LateUpdate()
        {
            Run(_lateSteps);
        }

        /// <summary>Game-loop LateUpdate, before <see cref="LateUpdate"/>, not while paused.</summary>
        public void ReleaseLate()
        {
            Run(_releaseLateSteps);
        }

        private void SolveIK()
        {
            if (_ik != null)
            {
                _ik.UpdateSolverExternal();
            }
        }

        private static void Run(Guarded[] steps)
        {
            if (!DroodProfiler.On)
            {
                for (var i = 0; i < steps.Length; i++)
                {
                    steps[i].Run();
                }

                return;
            }

            for (var i = 0; i < steps.Length; i++)
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                steps[i].Run();
                DroodProfiler.Add(steps[i].Slot, System.Diagnostics.Stopwatch.GetTimestamp() - start);
            }
        }

        /// <summary>What this drood is doing, for the performance log.</summary>
        private DroodProfiler.Phase CurrentPhase()
        {
            if (!DroodProfiler.On)
            {
                return DroodProfiler.Phase.None;
            }

            return _landingFall.Active ? DroodProfiler.Phase.LandingClip
                : _freeFall.Active ? DroodProfiler.Phase.FreeFall
                : _parachute.CanopyOut ? DroodProfiler.Phase.Canopy
                : DroodProfiler.Phase.Ground;
        }

        /// <summary>The craft's structure changed (parts attached, detached or destroyed).</summary>
        public void OnCraftStructureChanged()
        {
            _limbFollower.MarkDirty();
        }

        /// <summary>The game moved this craft by <paramref name="positionDelta"/>.</summary>
        public void OnFrameShift(Vector3 positionDelta)
        {
            _parachute.OnFrameShift(positionDelta);
            _courseCamera.OnFrameShift(positionDelta);
        }

        /// <summary>
        /// One step of the pipeline: an exception is caught, logged the first time (with a
        /// count every 100th time after), and the next steps still run.
        /// </summary>
        private sealed class Guarded
        {
            private readonly string _name;
            private readonly System.Action _step;
            private int _failures;

            public Guarded(string name, System.Action step)
            {
                _name = name;
                _step = step;
                Slot = DroodProfiler.Slot(name);
            }

            /// <summary>This step's slot in <see cref="DroodProfiler"/>.</summary>
            public int Slot { get; }

            public void Run()
            {
                try
                {
                    _step();
                }
                catch (System.Exception e)
                {
                    _failures++;
                    if (_failures == 1)
                    {
                        Debug.LogError($"[Drood 2.0] The {_name} step failed (the other steps go on): {e}");
                    }
                    else if (_failures % 100 == 0)
                    {
                        Debug.LogError($"[Drood 2.0] The {_name} step has failed {_failures} times.");
                    }
                }
            }
        }

        private static T GetOrAdd<T>(GameObject drood) where T : Component
        {
            var component = drood.GetComponent<T>();
            return component != null ? component : drood.AddComponent<T>();
        }
    }
}
