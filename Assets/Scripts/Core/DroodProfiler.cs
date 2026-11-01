using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The "Performance Log" setting: how much of each frame Drood 2.0's own steps take. Every
    /// step of the flight pipeline (<see cref="Drood2FlightBehaviours"/>) is timed, summed over
    /// all Drood 2.0s, and every WindowSeconds one line goes to Player.log: the frame rate, the
    /// mod's time per frame (average and worst frame) and its share of the frame, the costliest
    /// steps, and which phases were flown meanwhile. Off by default; when off, each step costs
    /// one bool check more. Parts of a step (<see cref="PartSlot"/>) are listed on their own but
    /// not added to the mod's total again. Not timed: the Harmony patches and the tether registry (a few small
    /// calls), and the rendering of the canopy and lines (GPU).
    /// </summary>
    internal static class DroodProfiler
    {
        private const float WindowSeconds = 5f;
        private const int StepsShown = 11;

        // The game's FinalIK solves the drood every frame anyway; the mod only solves it earlier
        // (and stops the component's own solve), so this step isn't counted as the mod's cost.
        internal const string MovedStep = "IK solve";

        [System.Flags]
        internal enum Phase
        {
            None = 0,
            Ground = 1,
            FreeFall = 2,
            Canopy = 4,
            LandingClip = 8,
        }

        private static readonly List<string> Names = new List<string>();
        private static long[] _ticks = new long[64];
        private static long[] _worst = new long[64];
        private static int[] _order = new int[64];
        private static bool[] _part = new bool[64];
        private static readonly StringBuilder Line = new StringBuilder(512);

        private static int _frame = -1;
        private static int _frames;
        private static float _frameSeconds;
        private static float _worstFrameSeconds;
        private static long _frameTicks;
        private static long _worstFrameTicks;
        private static int _droods;
        private static int _droodsThisFrame;
        private static Phase _phases;
        private static int _collections;
        private static float _nextLog = -1f;

        /// <summary>Whether steps are timed this frame (read once per frame).</summary>
        internal static bool On { get; private set; }

        /// <summary>The slot that times the step called <paramref name="name"/> (one per name).</summary>
        internal static int Slot(string name)
        {
            var slot = Names.IndexOf(name);
            if (slot >= 0)
            {
                return slot;
            }

            Names.Add(name);
            if (Names.Count > _ticks.Length)
            {
                System.Array.Resize(ref _ticks, _ticks.Length * 2);
                System.Array.Resize(ref _worst, _worst.Length * 2);
                System.Array.Resize(ref _order, _order.Length * 2);
                System.Array.Resize(ref _part, _part.Length * 2);
            }

            return Names.Count - 1;
        }

        /// <summary>
        /// The slot that times a part of a step (shown as its own line, already counted in its
        /// step's time).
        /// </summary>
        internal static int PartSlot(string name)
        {
            var slot = Slot(name);
            _part[slot] = true;
            return slot;
        }

        /// <summary>
        /// Called by each Drood 2.0 at the start of its game-loop Update: the first call in a
        /// frame closes the last frame and, every WindowSeconds, writes the line.
        /// </summary>
        internal static void Frame(Phase phase)
        {
            if (Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                On = Enabled;
                if (!On)
                {
                    _nextLog = -1f;
                    return;
                }

                if (_nextLog < 0f)
                {
                    Reset();
                }
                else
                {
                    _frames++;
                    _frameSeconds += Time.unscaledDeltaTime;
                    _worstFrameSeconds = Mathf.Max(_worstFrameSeconds, Time.unscaledDeltaTime);
                    _worstFrameTicks = System.Math.Max(_worstFrameTicks, _frameTicks);
                    _droods = Mathf.Max(_droods, _droodsThisFrame);
                    if (Time.unscaledTime >= _nextLog && _frames > 0)
                    {
                        Write();
                        Reset();
                    }
                }

                _frameTicks = 0;
                _droodsThisFrame = 0;
            }

            if (On)
            {
                _droodsThisFrame++;
                _phases |= phase;
            }
        }

        /// <summary>Adds <paramref name="ticks"/> (Stopwatch ticks) to a step's time.</summary>
        internal static void Add(int slot, long ticks)
        {
            _ticks[slot] += ticks;
            _worst[slot] = System.Math.Max(_worst[slot], ticks);
            if (Names[slot] != MovedStep && !_part[slot])
            {
                _frameTicks += ticks;
            }
        }

        private static bool Enabled
        {
            get
            {
                try
                {
                    return Assets.Scripts.ModSettings.Instance?.PerformanceLog?.Value ?? false;
                }
                catch (System.Exception)
                {
                    return false;
                }
            }
        }

        private static void Reset()
        {
            System.Array.Clear(_ticks, 0, _ticks.Length);
            System.Array.Clear(_worst, 0, _worst.Length);
            _frames = 0;
            _frameSeconds = 0f;
            _worstFrameSeconds = 0f;
            _frameTicks = 0;
            _worstFrameTicks = 0;
            _droods = 0;
            _phases = Phase.None;
            _collections = System.GC.CollectionCount(0);
            _nextLog = Time.unscaledTime + WindowSeconds;
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private static void Write()
        {
            long own = 0;
            long moved = 0;
            for (var i = 0; i < Names.Count; i++)
            {
                if (Names[i] == MovedStep)
                {
                    moved += _ticks[i];
                }
                else if (!_part[i])
                {
                    own += _ticks[i];
                }

                _order[i] = i;
            }

            System.Array.Sort(_order, 0, Names.Count, Comparer<int>.Create((a, b) => _ticks[b].CompareTo(_ticks[a])));

            var frameMs = _frameSeconds * 1000.0 / _frames;
            var ownMs = Ms(own) / _frames;
            Line.Clear();
            Line.Append("[Drood 2.0] Performance (").Append(_frameSeconds.ToString("0.0")).Append(" s, ")
                .Append(_frames).Append(" frames, ").Append((_frames / _frameSeconds).ToString("0")).Append(" fps, frame ")
                .Append(frameMs.ToString("0.0")).Append(" ms avg / ").Append((_worstFrameSeconds * 1000f).ToString("0")).Append(" ms worst; ")
                .Append(_droods).Append(_droods == 1 ? " Drood 2.0; " : " Drood 2.0s; ").Append(_phases)
                .Append("): Drood 2.0 takes ").Append(ownMs.ToString("0.000")).Append(" ms per frame (")
                .Append((100.0 * ownMs / frameMs).ToString("0.0")).Append("% of the frame), worst frame ")
                .Append(Ms(_worstFrameTicks).ToString("0.000")).Append(" ms; the game's IK solve, run by the mod: ")
                .Append((Ms(moved) / _frames).ToString("0.000")).Append(" ms; garbage collections (whole game): ")
                .Append(System.GC.CollectionCount(0) - _collections).Append(". Costliest steps (ms per frame avg / worst call):");
            for (var i = 0; i < Mathf.Min(StepsShown, Names.Count); i++)
            {
                var slot = _order[i];
                if (_ticks[slot] == 0)
                {
                    break;
                }

                Line.Append(i == 0 ? " " : ", ").Append(Names[slot]).Append(' ')
                    .Append((Ms(_ticks[slot]) / _frames).ToString("0.000")).Append(" / ").Append(Ms(_worst[slot]).ToString("0.000"));
            }

            UnityEngine.Debug.Log(Line.Append('.').ToString());
        }
    }
}
