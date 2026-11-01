using RootMotion.FinalIK;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// Keeps the game's own IK settings on the drood's FullBodyBipedIK while the mod's poses use
    /// it. The game sets the effector and bend weights once, not every frame (seat IK:
    /// CockpitScript.SetIKEnabled, EvaChairScript.OnPilotExit), so a pose that zeroes them on
    /// release would wipe them until the next boarding.
    ///
    /// Each pose calls <see cref="Take"/> before it writes the solver in a frame. The first take
    /// saves the game's values. While the mod holds the solver, each frame starts from the
    /// game's values (<see cref="BeginFrame"/>, before the poses), so the active pose writes on
    /// top of them and nothing a pose wrote earlier lingers: the poses don't reset anything
    /// when they let go. A value the game changed in between (it differs from what the mod left
    /// last frame) is taken as the game's new value. After the first frame no pose takes the
    /// solver, the mod lets go (<see cref="BeforeSolve"/>). Only the poses in the late chain
    /// write the solver, all between these two calls (<see cref="Drood2FlightBehaviours"/>).
    /// </summary>
    public class DroodIKOwner : MonoBehaviour
    {
        private FullBodyBipedIK _ik;
        private IKEffector[] _effectors;
        private IKConstraintBend[] _bends;
        private FBIKChain[] _chains;
        private bool _held;
        private bool _taken;

        // The game's values, and the mod's at the end of its last frame (per effector:
        // position and rotation weight; per limb: bend weight and goal, its reach and its pull
        // on the body).
        private float[] _gamePosition;
        private float[] _gameRotation;
        private float[] _gameBend;
        private Transform[] _gameBendGoal;
        private float[] _modPosition;
        private float[] _modRotation;
        private float[] _modBend;
        private Transform[] _modBendGoal;
        private float[] _gameReach;
        private float[] _modReach;
        private float[] _gamePull;
        private float[] _modPull;

        private void Awake()
        {
            _ik = GetComponentInChildren<FullBodyBipedIK>(true);
            if (_ik == null)
            {
                return;
            }

            var solver = _ik.solver;
            _effectors = solver.effectors;
            _chains = new[] { solver.leftArmChain, solver.rightArmChain, solver.leftLegChain, solver.rightLegChain };
            _bends = new IKConstraintBend[_chains.Length];
            for (var i = 0; i < _chains.Length; i++)
            {
                _bends[i] = _chains[i].bendConstraint;
            }

            _gameReach = new float[_chains.Length];
            _modReach = new float[_chains.Length];
            _gamePull = new float[_chains.Length];
            _modPull = new float[_chains.Length];
            _gamePosition = new float[_effectors.Length];
            _gameRotation = new float[_effectors.Length];
            _modPosition = new float[_effectors.Length];
            _modRotation = new float[_effectors.Length];
            _gameBend = new float[_bends.Length];
            _gameBendGoal = new Transform[_bends.Length];
            _modBend = new float[_bends.Length];
            _modBendGoal = new Transform[_bends.Length];
        }

        /// <summary>A pose writes the solver this frame.</summary>
        internal void Take()
        {
            if (_ik == null)
            {
                return;
            }

            if (!_held)
            {
                Save(_gamePosition, _gameRotation, _gameBend, _gameBendGoal, _gameReach, _gamePull);
                _held = true;
            }

            _taken = true;
        }

        /// <summary>
        /// Before the poses: values the game changed since the mod's last frame are the game's;
        /// then the solver starts the frame from the game's values.
        /// </summary>
        internal void BeginFrame()
        {
            if (!_held)
            {
                return;
            }

            for (var i = 0; i < _effectors.Length; i++)
            {
                if (_effectors[i].positionWeight != _modPosition[i])
                {
                    _gamePosition[i] = _effectors[i].positionWeight;
                }

                if (_effectors[i].rotationWeight != _modRotation[i])
                {
                    _gameRotation[i] = _effectors[i].rotationWeight;
                }
            }

            for (var i = 0; i < _bends.Length; i++)
            {
                if (_bends[i].weight != _modBend[i])
                {
                    _gameBend[i] = _bends[i].weight;
                }

                if (_bends[i].bendGoal != _modBendGoal[i])
                {
                    _gameBendGoal[i] = _bends[i].bendGoal;
                }

                if (_chains[i].reach != _modReach[i])
                {
                    _gameReach[i] = _chains[i].reach;
                }

                if (_chains[i].pull != _modPull[i])
                {
                    _gamePull[i] = _chains[i].pull;
                }
            }

            Restore();
        }

        /// <summary>
        /// After the poses, before the solve: what the mod left this frame, or, if no pose took
        /// the solver (it has the game's values from BeginFrame), the mod lets go.
        /// </summary>
        internal void BeforeSolve()
        {
            if (_held)
            {
                if (_taken)
                {
                    Save(_modPosition, _modRotation, _modBend, _modBendGoal, _modReach, _modPull);
                }
                else
                {
                    _held = false;
                }
            }

            _taken = false;
        }

        /// <summary>The drood is destroyed or the mod stops: the game's values back.</summary>
        private void OnDestroy()
        {
            if (_held && _ik != null)
            {
                Restore();
            }
        }

        private void Restore()
        {
            for (var i = 0; i < _effectors.Length; i++)
            {
                _effectors[i].positionWeight = _gamePosition[i];
                _effectors[i].rotationWeight = _gameRotation[i];
            }

            for (var i = 0; i < _bends.Length; i++)
            {
                _bends[i].weight = _gameBend[i];
                _bends[i].bendGoal = _gameBendGoal[i];
                _chains[i].reach = _gameReach[i];
                _chains[i].pull = _gamePull[i];
            }
        }

        private void Save(float[] position, float[] rotation, float[] bend, Transform[] bendGoal, float[] reach, float[] pull)
        {
            for (var i = 0; i < _effectors.Length; i++)
            {
                position[i] = _effectors[i].positionWeight;
                rotation[i] = _effectors[i].rotationWeight;
            }

            for (var i = 0; i < _bends.Length; i++)
            {
                bend[i] = _bends[i].weight;
                bendGoal[i] = _bends[i].bendGoal;
                reach[i] = _chains[i].reach;
                pull[i] = _chains[i].pull;
            }
        }
    }
}
