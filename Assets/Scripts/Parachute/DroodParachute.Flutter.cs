using System.Collections.Generic;
using UnityEngine;

namespace Drood2
{
    // Ram-air flutter and partial collapse: in a stall the wing's fabric is no longer taut. Each
    // half buffets (its air force fluctuates a few times a second, averaging out), its trailing
    // edge ripples, its chord sags, and it partly collapses (StallCollapseOf: soft thin cells, a
    // shorter chord, a soft nose, the tip folding back and in). Per half, so a one-sided stall
    // (a spin) flutters on that side.
    // A pulled line also shapes its half: the trailing edge drawn down, the arch a little deeper,
    // the cells fuller. Hands off, none of this runs: the wing mesh keeps its built shape.
    public partial class DroodParachute
    {
        // Buffet: each half's air force varies by up to BuffetStall when stalled, at about
        // FlutterRate per second, plus up to BuffetStallFast at about BuffetFastRate (the stalled
        // wing's turbulence).
        private const float BuffetStall = 0.15f;
        private const float FlutterRate = 4.5f;

        // The ripple's second, faster wave, as a share of FlutterRate.
        private const float FlutterFastRate = 1.7f;
        private const float BuffetStallFast = 0.25f;
        private const float BuffetFastRate = 11f;

        // The ripple: up to this much (m) across the stalled wing, near the trailing edge (from
        // FlutterFromChord of the chord back), running along the span. The whole chord sags
        // toward the jumper, more toward the trailing edge, by up to FlutterStallSag (m) there,
        // like a pulled brake, and the trailing edge curls under (FlutterCurl, m, down and half
        // as much forward).
        private const float FlutterStall = 0.12f;
        private const float FlutterStallSag = 0.4f;
        private const float FlutterCurl = 0.15f;
        private const float FlutterFromChord = 0.8f;
        private const float FlutterWaves = 2.5f;

        // The partial collapse as drawn, per unit of collapse (StallCollapseOf, up to 0.6): the
        // cells lose CollapseThinning of their height, the chord shrinks evenly from the nose so
        // the trailing edge comes CollapseChordIn (m) forward, the nose (its air inlet) sags
        // CollapseNoseDrop (m), and the outer CollapseTipSpan of each half (fraction from the
        // tip) folds: the tip CollapseTipIn (m) in, CollapseTipBack back and CollapseTipDown
        // down. Each half bends backward along the span (its outer cells drag more): the span
        // curves back evenly, CollapseSweepBack degrees at the tip per unit of collapse (~72° in a
        // deep stall, a horseshoe seen from below), and droops a little (its arch angle grows by
        // CollapseArchDeepen per unit of collapse, the fabric keeping its length); both irregular
        // by up to CollapseArchWobble, slowly. The fabric wrinkles: slowly moving bumps of up to
        // CollapseWrinkles (m) at full collapse, about CollapseWrinkleSize (m) apart. Each half's
        // droop also flutters on its own, unevenly: its arch angle varies by up to
        // CollapseDroopFlutter per unit of collapse, about CollapseDroopFlutterRate times a second.
        private const float CollapseThinning = 1f;
        private const float CollapseChordIn = 0.4f;
        private const float CollapseNoseDrop = 0.1f;
        private const float CollapseTipSpan = 0.35f;
        private const float CollapseTipIn = 1f;
        private const float CollapseTipBack = 0.8f;
        private const float CollapseTipDown = 0.7f;
        private const float CollapseArchDeepen = 0.5f;
        private const float CollapseSweepBack = 120f;
        private const float CollapseDroopFlutter = 0.6f;
        private const float CollapseDroopFlutterRate = 1.5f;

        // ... but never flatter than this share of its built arch.
        private const float CollapseArchFlattest = 0.8f;
        private const float CollapseArchWobble = 0.25f;
        private const float CollapseWrinkles = 0.07f;
        private const float CollapseWrinkleSize = 0.5f;
        private const float CollapseWrinkleDrift = 0.4f;

        // A stalled wing shakes about its own centre (the lines follow): up to StallShakeDegrees
        // about each axis at a full stall, at about StallShakeRate per second.
        private const float StallShakeDegrees = 4f;
        private const float StallShakeRate = 6f;

        // ... and jerks the lines: sideways tugs on the drood of up to StallTug of the line pull,
        // at about StallTugRate per second.
        private const float StallTug = 0.3f;
        private const float StallTugRate = 5f;

        // The cells fill by up to BrakeCellFill with the trailing edge drawn down
        // (TrailingEdgeShape).
        private const float BrakeCellFill = 0.25f;

        // The trailing edge's shape at full deflection (a full flare; a turn, the brakes and the
        // flare draw each half part of the way, TrailingEdgeShape): all but the middle cell (its
        // edge, FlareFromHalf of the half span, is where it starts; the brake lines pull the four
        // outer cells of each half): the rear of the chord, from FlareCurlFromChord back (with a
        // light pull only the very end, from FlareCurlFirstChord, the start moving forward as the
        // pull grows), curls down in a smooth curve (the bend growing toward the trailing edge,
        // FlareCurlSteps steps), growing smoothly to FlareCurlPeak degrees at FlareCurlPeakHalf of
        // the half span (the outer two cells start there) and flattening smoothly again to
        // FlareCurlTipShare of that at the tip; and the brake lines pull the trailing edge straight
        // down, growing smoothly to FlareTailPull (m) where the outer two cells start and holding
        // there to the tip (those cells drop with the rest but don't tilt further; the trailing
        // edge one smooth curve). Only behind that start, growing toward the trailing edge: the
        // rest of the wing keeps its shape.
        private const float FlareFromHalf = 1f / WingCells;
        private const float FlareCurlFromChord = 0.8f;
        private const float FlareCurlFirstChord = 0.96f;
        private const int FlareCurlSteps = 6;
        private const float FlareCurlPeak = 68f;
        private const float FlareCurlPeakHalf = 5f / WingCells;
        private const float FlareCurlTipShare = 0.4f;
        private const float FlareTailPull = 0.54f;

        // The steering line draws the trailing edge toward its cascade below the middle of its
        // attachment points: down, and inward (toward the wing's middle) by FlareTailInward of the
        // downward pull.
        private const float FlareTailInward = 0.3f;

        // The widest gap between two neighbouring steering line attachments (half span; worked
        // out when asked: a static field's initializer could run before the stations', which are
        // in another file of this class).
        private static float WidestAttachGap
        {
            get
            {
                var widest = 0f;
                for (var i = 1; i < SteeringAttachStations.Length; i++)
                {
                    widest = Mathf.Max(widest, SteeringAttachStations[i] - SteeringAttachStations[i - 1]);
                }

                return widest;
            }
        }

        // The steering line pulls the trailing edge down at its attachment points
        // (SteeringAttachStations, DroodParachute.RamAir.cs): between two of them the pull eases
        // off midway (a cosine) by FlareScallopDepth at a light pull, down to
        // FlareScallopDepthFull at a full one (a fixed share of a deep pull made a jagged
        // sawtooth), and the curl by FlareScallopCurlShare of that (less curl between the points
        // also left the edge further back there: fangs from the side), so the edge scallops like
        // cloth drawn down at points instead of bending as one flap.
        private const float FlareScallopDepth = 0.2f;
        private const float FlareScallopDepthFull = 0.075f;
        private const float FlareScallopCurlShare = 0.5f;

        /// <summary>
        /// How far between two attachment points <paramref name="outward"/> of the half span lies
        /// (0 at one, 1 midway), times their distance against the widest gap (an arch half a cell
        /// long sags half as deep as one a cell long).
        /// </summary>
        private static float ScallopDip(float outward)
        {
            var stations = SteeringAttachStations;
            if (outward <= stations[0])
            {
                return 0f;
            }

            for (var i = 1; i < stations.Length; i++)
            {
                if (outward <= stations[i])
                {
                    var t = Mathf.InverseLerp(stations[i - 1], stations[i], outward);
                    return 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * t)) * (stations[i] - stations[i - 1]) / WidestAttachGap;
                }
            }

            return 0f;
        }

        // Turning on the front risers: each half pitches by its own riser against the wing's
        // (which follows their average), FrontTurnShown of FrontTurnPitch per unit of
        // difference, fading in from the middle over the inner FrontTurnBlendSpan of the half span.
        // Its leading edge drops too (its A lines shortened): FrontNoseDip (m, wing space) per
        // unit of difference at the nose, fading out back to FrontNoseChord of the chord (rising
        // as much on the other half).
        private const float FrontTurnBlendSpan = 0.5f;
        private const float FrontTurnShown = 0.5f;
        private const float FrontNoseDip = 0.4f;
        private const float FrontNoseChord = 0.5f;

        // The halves are shaped apart (a stall, a turn) and blend into each other across the
        // middle MiddleBlendCells cells, so a one-sided shape has no edge in the middle.
        private const float MiddleBlendCells = 3f;

        // Below this the ripple is off and the wing has its built shape.
        private const float FlutterOff = 0.002f;

        private ShapeFixed[] _shapeFixed;
        private Vector3[] _shapeVertices;

        // Per point, the first point at the same built place (the half-cells' seams, the ribs'
        // and end panels' edges): shaped once, copied to the others.
        private int[] _shapeSource;

        private static int _shapingSlot = -1;
        private static int _normalsSlot = -1;
        private static int _seamSlot = -1;

        // The radius of the wing's arch (its span bent over WingArcDegrees).
        private const float WingArcRadius = WingSpan / (WingArcDegrees * Mathf.Deg2Rad);
        private bool _wingShaped;
        private float _flutterSeed = -1f;

        // A noise offset per drood (Unity's Random can't run in a field initializer).
        private float FlutterSeed => _flutterSeed >= 0f ? _flutterSeed : _flutterSeed = Random.Range(0f, 100f);

        /// <summary>How much one half flutters (0..1): its stall.</summary>
        private float FlutterAmount(int side) => side < 0 ? _leftStall : _rightStall;

        /// <summary>
        /// How far one half's trailing edge is drawn toward its full-deflection shape (0..1): its
        /// deflection in the flight model (its line, the brakes' extra and the flare's) against a
        /// full flare's, times (trim speed / airspeed)², at most 1 (the line pulls with about the
        /// same force; faster air pushes the trailing edge back up); a turn (the line pulled
        /// beyond the other) drawn TurnShown times as deep. A full turn on one line draws that
        /// half ~0.57 of the way, full brakes ~0.51, a full flare all of it.
        /// </summary>
        private float TrailingEdgeShape(bool leftSide)
        {
            if (_trailingLanded)
            {
                // Down: the shape it had at touchdown, fading as the wing folds on the ground.
                return (leftSide ? _heldTrailingLeft : _heldTrailingRight) * _trailingFade;
            }

            var airspeed = _hasVisualAirVelocity ? _visualAirVelocity.magnitude : 0f;
            var trim = RamAirTrimSpeed * WingSpeedScale;
            var speedFactor = airspeed > trim ? (trim / airspeed) * (trim / airspeed) : 1f;
            var own = leftSide ? _leftPull : _rightPull;
            var both = Mathf.Min(_leftPull, _rightPull);
            var deflection = both + TurnShown * (own - both) + BrakeExtraDeflection * both * Mathf.Lerp(BrakeEffect, 1f, _flarePull) + FlareDeflection * _flarePull;
            var shape = Mathf.Clamp01(deflection / (1f + BrakeExtraDeflection + FlareDeflection)) * speedFactor;
            if (leftSide)
            {
                _heldTrailingLeft = shape;
            }
            else
            {
                _heldTrailingRight = shape;
            }

            return shape;
        }

        // Each half's trailing-edge shape when it was last flying, held through the touchdown and
        // faded out (_trailingFade, 1..0) as the lines are let go and the wing comes down.
        private const float TurnShown = 2f;
        private float _heldTrailingLeft;
        private float _heldTrailingRight;
        private bool _trailingLanded;
        private float _trailingFade = 1f;

        // The clock of the stalled wing's moving shape (ripple, wobble, droop, shaking, lopsided
        // collapse): the game's time in the air; down, it runs slower and stops by
        // ShapeSettleCollapse of the collapse, so the crumpled wing lies still instead of
        // fluttering on the ground.
        private const float ShapeSettleCollapse = 0.2f;
        private float _shapeClock;

        /// <summary>Visual step: advances the shape clock (see ShapeSettleCollapse).</summary>
        private void StepShapeClock()
        {
            var rate = _trailingLanded ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, ShapeSettleCollapse, _visualCollapse)) : 1f;
            _shapeClock += Time.deltaTime * rate;
        }

        /// <summary>
        /// A stall as drawn: down, the crumpled wing blends into the wing's own shape while it
        /// falls, by StallShapeGoneCollapse of the collapse (before it reaches the ground), and
        /// then folds like any landed wing.
        /// </summary>
        private float StallShown(float stall) =>
            _trailingLanded ? stall * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, StallShapeGoneCollapse, _visualCollapse))) : stall;

        private const float StallShapeGoneCollapse = 0.4f;

        /// <summary>One half's front riser against the two's average (+ deeper, - higher; 0 when level).</summary>
        private float FrontShapePull(bool leftSide) => FrontPullOf(leftSide ? -1 : 1) - FrontPullAverage;

        /// <summary>The stalled wing's shaking, in its own frame (identity when it doesn't stall).</summary>
        private Quaternion StallShake()
        {
            var stall = StallShown(Mathf.Max(_leftStall, _rightStall));
            if (stall <= 0f)
            {
                return Quaternion.identity;
            }

            var t = _shapeClock * StallShakeRate;
            var size = StallShakeDegrees * stall;
            return Quaternion.Euler(
                (Mathf.PerlinNoise(t, FlutterSeed + 11f) * 2f - 1f) * size,
                (Mathf.PerlinNoise(t, FlutterSeed + 37f) * 2f - 1f) * size,
                (Mathf.PerlinNoise(t, FlutterSeed + 59f) * 2f - 1f) * size);
        }

        /// <summary>The buffet factor for one half's lift and drag (1 when it doesn't buffet).</summary>
        private float Buffet(int side)
        {
            var amount = FlutterAmount(side);
            if (amount <= 0f)
            {
                return 1f;
            }

            var lane = FlutterSeed + (side < 0 ? 0f : 50f);
            var slow = Mathf.PerlinNoise(Time.time * FlutterRate, lane) * 2f - 1f;
            var fast = Mathf.PerlinNoise(Time.time * BuffetFastRate, lane + 25f) * 2f - 1f;
            return 1f + (slow * BuffetStall + fast * BuffetStallFast) * amount;
        }

        /// <summary>
        /// Shapes the wing per half (<see cref="WingShapePoint"/>): pulled lines (trailing edge,
        /// arch, cells), the front risers in a turn (each half's pitch), and a stall (collapse,
        /// fold, bend, flutter). Restores the built shape once nothing shapes it. Not on the
        /// ground (the landed shape is ApplyGroundLumps').
        /// </summary>
        private void ApplyWingShape(bool flying)
        {
            if (_wingMesh == null || _wingRestVertices == null)
            {
                return;
            }

            // Down: the trailing edges keep the shape they had at touchdown (TrailingEdgeShape).
            _trailingLanded = !flying;
            var leftHalf = HalfShapeOf(true);
            var rightHalf = HalfShapeOf(false);
            var left = flying ? Mathf.Max(Mathf.Max(leftHalf.Stall, leftHalf.Trailing), Mathf.Abs(leftHalf.FrontDeeper)) : Mathf.Max(leftHalf.Stall, leftHalf.Trailing);
            var right = flying ? Mathf.Max(Mathf.Max(rightHalf.Stall, rightHalf.Trailing), Mathf.Abs(rightHalf.FrontDeeper)) : Mathf.Max(rightHalf.Stall, rightHalf.Trailing);
            var folded = _wingLumps > 0f;
            if (left < FlutterOff && right < FlutterOff)
            {
                if (_wingShaped && !folded)
                {
                    _wingMesh.vertices = _wingRestVertices;
                    _wingMesh.RecalculateNormals();
            WeldSeamNormals(_wingMesh);
                }
                else if (_wingShaped && _lumpVertices != null)
                {
                    // Lying down: back to the plain folded wing (ApplyGroundLumps only rewrites it
                    // while the folding goes on).
                    _wingMesh.vertices = _lumpVertices;
                    _wingMesh.RecalculateNormals();
            WeldSeamNormals(_wingMesh);
                    _wingLowest = Lowest(_lumpVertices);
                    _wingMesh.RecalculateBounds();
                }

                _wingShaped = false;
                return;
            }

            if (_shapeFixed == null || _shapeFixed.Length != _wingRestVertices.Length)
            {
                PrepareWingShape();
            }

            _wingShaped = true;
            var timed = DroodProfiler.On;
            var start = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            for (var i = 0; i < _wingRestVertices.Length; i++)
            {
                var source = _shapeSource[i];
                if (source != i)
                {
                    _shapeVertices[i] = _shapeVertices[source];
                    continue;
                }

                var shaped = ShapedPoint(_wingRestVertices[i], in _shapeFixed[i], in leftHalf, in rightHalf);
                // The landed wing's folds (ApplyGroundLumps) on top while it lies down.
                _shapeVertices[i] = folded ? Vector3.Lerp(shaped, LandedPoint(shaped), _wingLumps) : shaped;
            }

            _wingMesh.vertices = _shapeVertices;
            var shapedAt = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            _wingMesh.RecalculateNormals();
            var normalsAt = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            WeldSeamNormals(_wingMesh);
            if (timed)
            {
                if (_shapingSlot < 0)
                {
                    _shapingSlot = DroodProfiler.PartSlot("(visuals) wing shaping");
                    _normalsSlot = DroodProfiler.PartSlot("(visuals) wing normals");
                    _seamSlot = DroodProfiler.PartSlot("(visuals) wing seams");
                }

                DroodProfiler.Add(_shapingSlot, shapedAt - start);
                DroodProfiler.Add(_normalsSlot, normalsAt - shapedAt);
                DroodProfiler.Add(_seamSlot, System.Diagnostics.Stopwatch.GetTimestamp() - normalsAt);
            }

            // Down, the wing rests on its lowest point as shaped now (a stalled wing's drooping
            // tips, the folds), so it neither sinks into the ground nor jumps when the folds begin.
            if (folded || !flying)
            {
                _wingLowest = Lowest(_shapeVertices);
                _wingMesh.RecalculateBounds();
            }
        }

        /// <summary>
        /// One half's shaping this frame, the same for each of its points (<see cref="WingShapePoint"/>).
        /// </summary>
        private struct HalfShape
        {
            public bool LeftSide;
            public float Stall;
            public float Trailing;
            public float FrontDeeper;

            // Nothing shapes this half: its points keep their built places.
            public bool Plain;
            public float Collapse;
            public float Wobble;
            public float Deepen;
            public float FoldedRadius;
            public float HeightScale;
            public float CurlFrom;
            public float CurlBend;
            public float TailPull;
            public float FrontTurn;
            public float NoseDip;
            public float WrinkleDrift;
            public float ScallopDepth;

            // The ripple's clock this frame (WingShapePoint's ripple, by its angle sums).
            public float SinT;
            public float CosT;
            public float SinFastT;
            public float CosFastT;
        }

        private HalfShape HalfShapeOf(bool leftSide)
        {
            var half = new HalfShape
            {
                LeftSide = leftSide,
                Stall = StallShown(leftSide ? _leftStall : _rightStall),
                Trailing = TrailingEdgeShape(leftSide),
                FrontDeeper = FrontShapePull(leftSide),
            };
            half.Plain = half.Stall <= 0f && half.Trailing <= 0f && half.FrontDeeper == 0f;
            if (half.Plain)
            {
                return half;
            }

            // The collapse as drawn fades with the stall as drawn (StallShown).
            var rawStall = leftSide ? _leftStall : _rightStall;
            half.Collapse = half.Stall > 0f && rawStall > 0f ? StallCollapseOf(leftSide ? -1 : 1) * (half.Stall / rawStall) : 0f;

            // About the arch's centre: the half folds down about the middle (a deeper arch of the
            // same length, its top staying put), its cells go soft and thin (toward the
            // underside's arch) and wrinkle; a pulled line deepens the arch a little and fills
            // the cells.
            half.Wobble = 1f + CollapseArchWobble * (Mathf.PerlinNoise(_shapeClock * CollapseWrinkleDrift, FlutterSeed + (leftSide ? 20f : 70f)) * 2f - 1f);
            // Each half's droop flutters on its own: it collapses down and refills, unevenly.
            var droopFlutter = half.Collapse > 0f
                ? CollapseDroopFlutter * half.Collapse * (Mathf.PerlinNoise(_shapeClock * CollapseDroopFlutterRate, FlutterSeed + (leftSide ? 140f : 190f)) * 2f - 1f)
                : 0f;
            half.Deepen = Mathf.Max(1f + CollapseArchDeepen * half.Collapse * half.Wobble + droopFlutter, CollapseArchFlattest);
            half.FoldedRadius = WingArcRadius / half.Deepen;
            half.HeightScale = (1f - CollapseThinning * half.Collapse) * (1f + BrakeCellFill * half.Trailing);
            half.WrinkleDrift = _shapeClock * CollapseWrinkleDrift;
            var t = _shapeClock * FlutterRate * 2f * Mathf.PI;
            half.SinT = Mathf.Sin(t);
            half.CosT = Mathf.Cos(t);
            half.SinFastT = Mathf.Sin(FlutterFastRate * t);
            half.CosFastT = Mathf.Cos(FlutterFastRate * t);

            // Where the trailing edge's curve starts: near the trailing edge with a light pull,
            // further forward the more the edge is drawn down.
            half.CurlFrom = Mathf.Lerp(FlareCurlFirstChord, FlareCurlFromChord, half.Trailing);
            half.CurlBend = half.Trailing * FlareCurlPeak * Mathf.Deg2Rad;
            half.TailPull = FlareTailPull * half.Trailing;
            half.ScallopDepth = Mathf.Lerp(FlareScallopDepth, FlareScallopDepthFull, half.Trailing);
            half.FrontTurn = FrontTurnPitch * FrontTurnShown * half.FrontDeeper * Mathf.Deg2Rad;
            half.NoseDip = FrontNoseDip * half.FrontDeeper;
            return half;
        }

        /// <summary>
        /// A wing point's fixed values for <see cref="WingShapePoint"/>: its place along the span
        /// (0..1) and the chord (0 nose, 1 trailing edge), its angle and distance about the
        /// arch's centre, and how much each shaping reaches it there.
        /// </summary>
        private struct ShapeFixed
        {
            public float Span;
            public float Chord;
            public float Angle;
            public float Sin;
            public float Cos;
            public float Distance;
            public float Outward;
            public float TailReach;
            public float CurlSpan;
            public float ScallopDip;
            public float LocalChord;
            public float FrontBlend;
            public float FrontNose;
            public float Tip;

            // The right half's share of its shape (0 left of the middle cells, 1 right of them).
            public float RightShare;

            // A stall's reach here: the trailing edge's ripple and curl, the nose's drop.
            public float Edge;
            public float Nose;

            // The ripple's two waves' phases here (sine and cosine), and the wrinkles' place.
            public float WaveSin;
            public float WaveCos;
            public float FastWaveSin;
            public float FastWaveCos;
            public float WrinkleX;
            public float WrinkleY;
        }

        private ShapeFixed ShapeFixedOf(Vector3 v)
        {
            var angle = Mathf.Atan2(v.x, v.y + WingArcRadius);
            return ShapeFixedOf(v, Mathf.Clamp01(angle / (WingArcDegrees * Mathf.Deg2Rad) + 0.5f));
        }

        /// <summary>The fixed values of a point at a known place along the span.</summary>
        private ShapeFixed ShapeFixedOf(Vector3 v, float span)
        {
            var angle = Mathf.Atan2(v.x, v.y + WingArcRadius);
            var outward = Mathf.Abs(span - 0.5f) * 2f;
            var fromTip = Mathf.Min(span, 1f - span) * 2f;
            var chord = ChordOf(v, span);
            var nose = Mathf.Clamp01(1f - chord / WingNoseLength);
            var wave = span * FlutterWaves * 2f * Mathf.PI + chord * 4f;
            var fastWave = span * 17f + FlutterSeed;
            return new ShapeFixed
            {
                Span = span,
                Chord = chord,
                Angle = angle,
                Sin = Mathf.Sin(angle),
                Cos = Mathf.Cos(angle),
                Distance = new Vector2(v.x, v.y + WingArcRadius).magnitude,
                Outward = outward,
                TailReach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlareFromHalf, FlareCurlPeakHalf, outward)),
                ScallopDip = ScallopDip(outward),
                CurlSpan = outward < FlareCurlPeakHalf
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlareFromHalf, FlareCurlPeakHalf, outward))
                    : Mathf.SmoothStep(1f, FlareCurlTipShare, Mathf.InverseLerp(FlareCurlPeakHalf, 1f, outward)),
                LocalChord = WingChord * PlanformScale(span),
                FrontBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, FrontTurnBlendSpan, outward)),
                FrontNose = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, FrontTurnBlendSpan, outward))
                    * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0f, FrontNoseChord, chord)),
                Tip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CollapseTipSpan, 0f, fromTip)),
                RightShare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-MiddleBlendCells * 0.5f / WingCells, MiddleBlendCells * 0.5f / WingCells, span - 0.5f)),
                Edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlutterFromChord, 1f, chord)),
                Nose = nose * nose,
                WaveSin = Mathf.Sin(wave),
                WaveCos = Mathf.Cos(wave),
                FastWaveSin = Mathf.Sin(fastWave),
                FastWaveCos = Mathf.Cos(fastWave),
                WrinkleX = span * WingSpan / CollapseWrinkleSize,
                WrinkleY = chord * WingChord / CollapseWrinkleSize + FlutterSeed,
            };
        }

        /// <summary>
        /// A wing point shaped by its half (<see cref="WingShapePoint"/>); in the middle cells, by
        /// both halves blended (<see cref="ShapeFixed.RightShare"/>).
        /// </summary>
        private Vector3 ShapedPoint(Vector3 v, in ShapeFixed point, in HalfShape leftHalf, in HalfShape rightHalf)
        {
            var share = point.RightShare;
            if (share <= 0f)
            {
                return WingShapePoint(v, in point, in leftHalf);
            }

            if (share >= 1f)
            {
                return WingShapePoint(v, in point, in rightHalf);
            }

            return Vector3.Lerp(WingShapePoint(v, in point, in leftHalf), WingShapePoint(v, in point, in rightHalf), share);
        }

        /// <summary>
        /// A point of the wing (<see cref="WingPoint"/> space) as the lines and the stall shape it
        /// now (brakes, front risers, stall), from its built place <paramref name="v"/>, its fixed
        /// values and its half's shaping. Unchanged when its half is plain.
        /// </summary>
        private Vector3 WingShapePoint(Vector3 v, in ShapeFixed point, in HalfShape half)
        {
            if (half.Plain)
            {
                return v;
            }

            var span = point.Span;
            var chord = point.Chord;
            var collapse = half.Collapse;
            var trailing = half.Trailing;

            // Height above the underside's arch; below it (the stabilizers under the tips) the
            // fabric keeps its place (thinning and filling only apply above).
            var height = point.Distance - WingArcRadius;
            var wrinkle = collapse > 0f
                ? (Mathf.PerlinNoise(point.WrinkleX + half.WrinkleDrift, point.WrinkleY) * 2f - 1f)
                    * CollapseWrinkles * collapse / StallDeepCollapse
                : 0f;
            var shapedHeight = height > 0f ? height * half.HeightScale : height;
            var distance = half.FoldedRadius + shapedHeight + wrinkle;
            float sin;
            float cos;
            if (half.Deepen == 1f)
            {
                sin = point.Sin;
                cos = point.Cos;
            }
            else
            {
                var angle = point.Angle * half.Deepen;
                sin = Mathf.Sin(angle);
                cos = Mathf.Cos(angle);
            }

            v.x = sin * distance;
            v.y = cos * distance - half.FoldedRadius;

            // The brake lines pull the trailing edge straight down.
            var outward = point.Outward;
            var rearward = Mathf.InverseLerp(half.CurlFrom, 1f, chord);
            rearward *= rearward;
            if (trailing * rearward > 0f && outward > FlareFromHalf)
            {
                var pull = half.TailPull * rearward * point.TailReach * (1f - half.ScallopDepth * point.ScallopDip);
                v.y -= pull;
                v.x -= Mathf.Sign(v.x) * FlareTailInward * pull;
            }

            // The trailing edge curls down (its length kept), the bend growing from
            // nothing where it starts to the most at the trailing edge (a smooth curve, no hinge),
            // all but the middle cell, most where the outer two cells start.
            var curlSpan = point.CurlSpan * (1f - FlareScallopCurlShare * half.ScallopDepth * point.ScallopDip);
            if (trailing > 0f && chord > half.CurlFrom && curlSpan > 0f)
            {
                var localChord = point.LocalChord;
                var curlLength = (1f - half.CurlFrom) * localChord;
                var behind = (chord - half.CurlFrom) * localChord;
                CurlOffset(half.CurlBend * curlSpan, behind / curlLength, out var back, out var down);
                v.z = v.z + behind - back * curlLength;
                v.y -= down * curlLength;
            }

            // Front riser turn: the half pitches about the middle of its chord by its own riser
            // against the wing (nose down for the deeper one, up for the other), and its leading
            // edge drops (rises).
            if (half.FrontDeeper != 0f)
            {
                v.y -= half.NoseDip * point.FrontNose;
                var turn = half.FrontTurn * point.FrontBlend;
                var turnCos = Mathf.Cos(turn);
                var turnSin = Mathf.Sin(turn);
                var y = v.y;
                var z = v.z;
                v.y = y * turnCos - z * turnSin;
                v.z = y * turnSin + z * turnCos;
            }

            if (half.Stall <= 0f)
            {
                return v;
            }

            var stall = half.Stall;
            var leftSide = half.LeftSide;

            // The chord: shorter, evenly from the nose; sagging, more toward the trailing edge;
            // the nose soft; the trailing edge rippling.
            // sin(t - wave) + 0.5 sin(fast t + fast wave), by the angle sums.
            var edge = point.Edge;
            var ripple = half.SinT * point.WaveCos - half.CosT * point.WaveSin
                + 0.5f * (half.SinFastT * point.FastWaveCos + half.CosFastT * point.FastWaveSin);
            var curl = edge * edge * FlutterCurl * stall;
            v.y += edge * stall * ripple * FlutterStall - chord * chord * stall * FlutterStallSag - point.Nose * CollapseNoseDrop * collapse - curl;
            v.z += chord * CollapseChordIn * collapse + curl * 0.5f;

            // The span bends back: along a curve, points keep their distance along the span and
            // their place along the chord.
            var sweep = CollapseSweepBack * Mathf.Deg2Rad * collapse * half.Wobble;
            if (sweep > 1e-4f)
            {
                var curvature = sweep / (WingSpan * 0.5f);
                var bend = curvature * v.x;
                var forwardOfLine = v.z;
                var bendSin = Mathf.Sin(bend);
                var bendCos = Mathf.Cos(bend);
                v.x = bendSin / curvature + forwardOfLine * bendSin;
                v.z = -(1f - bendCos) / curvature + forwardOfLine * bendCos;
            }

            // The tip folds back and in, down toward the jumper.
            var tip = point.Tip;
            if (tip > 0f)
            {
                var fold = collapse * tip;
                v.x += (leftSide ? 1f : -1f) * CollapseTipIn * fold * tip;
                v.z -= CollapseTipBack * fold;
                v.y -= CollapseTipDown * fold;
            }

            return v;
        }

        // The trailing edge's curl (CurlOffset), tabled once: CurlTableSize steps of the bend
        // (up to a full flare's FlareCurlPeak) by CurlTableSize steps of the distance into the
        // curve, read between them.
        private const int CurlTableSize = 64;
        private const float CurlTableBend = FlareCurlPeak * Mathf.Deg2Rad;
        private static float[] _curlBack;
        private static float[] _curlDown;

        /// <summary>
        /// How far back and down a point lies along the curl, <paramref name="reach"/> (0..1) of
        /// the curve's length behind its start, per unit of that length, the curve bending
        /// <paramref name="bend"/> (radians) at its end: the angle at a share s of the curve is
        /// bend × s², summed in FlareCurlSteps steps.
        /// </summary>
        private static void CurlOffset(float bend, float reach, out float back, out float down)
        {
            if (_curlBack == null)
            {
                BuildCurlTable();
            }

            var b = Mathf.Clamp01(bend / CurlTableBend) * CurlTableSize;
            var r = Mathf.Clamp01(reach) * CurlTableSize;
            var bi = Mathf.Min((int)b, CurlTableSize - 1);
            var ri = Mathf.Min((int)r, CurlTableSize - 1);
            var bt = b - bi;
            var rt = r - ri;
            var row = CurlTableSize + 1;
            var a = bi * row + ri;
            var c = a + row;
            back = Mathf.Lerp(Mathf.Lerp(_curlBack[a], _curlBack[a + 1], rt), Mathf.Lerp(_curlBack[c], _curlBack[c + 1], rt), bt);
            down = Mathf.Lerp(Mathf.Lerp(_curlDown[a], _curlDown[a + 1], rt), Mathf.Lerp(_curlDown[c], _curlDown[c + 1], rt), bt);
        }

        private static void BuildCurlTable()
        {
            var row = CurlTableSize + 1;
            var back = new float[row * row];
            var down = new float[row * row];
            for (var bi = 0; bi < row; bi++)
            {
                var bend = CurlTableBend * bi / CurlTableSize;
                for (var ri = 0; ri < row; ri++)
                {
                    var reach = (float)ri / CurlTableSize;
                    var step = reach / FlareCurlSteps;
                    for (var i = 0; i < FlareCurlSteps; i++)
                    {
                        var along = (i + 0.5f) * step;
                        var tilt = bend * along * along;
                        back[bi * row + ri] += Mathf.Cos(tilt) * step;
                        down[bi * row + ri] += Mathf.Sin(tilt) * step;
                    }
                }
            }

            _curlDown = down;
            _curlBack = back;
        }

        /// <summary>A wing point's place along the chord (0 nose, 1 trailing edge).</summary>
        private static float ChordOf(Vector3 v, float span)
        {
            var localChord = WingChord * PlanformScale(span);
            var leadingEdge = 0.5f * WingChord - (WingChord - localChord) * 0.8f;
            return Mathf.Clamp01((leadingEdge - v.z) / localChord);
        }

        /// <summary>
        /// Each vertex's fixed values (<see cref="ShapeFixedOf"/>), and the first vertex at its
        /// place (<see cref="_shapeSource"/>).
        /// </summary>
        private void PrepareWingShape()
        {
            var count = _wingRestVertices.Length;
            _shapeFixed = new ShapeFixed[count];
            _shapeVertices = new Vector3[count];
            _shapeSource = new int[count];
            var first = new Dictionary<Vector3, int>(count);
            for (var i = 0; i < count; i++)
            {
                var v = _wingRestVertices[i];
                if (first.TryGetValue(v, out var source))
                {
                    _shapeSource[i] = source;
                    continue;
                }

                first.Add(v, i);
                _shapeSource[i] = i;
                _shapeFixed[i] = ShapeFixedOf(v);
            }
        }
    }
}
