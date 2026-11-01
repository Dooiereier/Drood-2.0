using System.Collections.Generic;
using UnityEngine;

namespace Drood2
{
    // Ram-air canopy: a steerable wing, chosen per drood in the designer (Drood2Data.Canopy).
    // Wing shape, aerodynamics, visuals (a wing mesh in the drood's paint) and lines.
    public partial class DroodParachute
    {
        // A sport wing: 18 m² (~195 sq ft; ~1.5 lb/sq ft for the drood's ~130 kg in flight),
        // 9 cells, arched. Its shape is given at 5.8 m span and 2.4 m chord and drawn WingScale
        // times that size (every length of the shape, its lines' attachment and its deformations
        // alike).
        private const float WingSpan = 5.8f;
        private const float WingChord = 2.4f;
        private const float WingThickness = 0.4f;
        private const float WingArcDegrees = 60f;
        // Each cell is split along the chord by a rib carrying no lines, so the top skin bulges
        // (up to WingCellBulge, m) between every rib: twice per cell.
        private const float WingCellBulge = 0.04f;
        private const int WingCells = 9;
        private const float WingScale = 1.1372f;
        private const float RamAirArea = WingSpan * WingChord * WingScale * WingScale;

        // The swoop wing (the designer's Canopy "Swoop"): the same wing, 10.7 m² (~115 sq ft,
        // ~2.5 lb/sq ft), drawn SwoopSize of the ram-air's size, with less base drag
        // (SwoopBaseDrag) but more drag growing with its lift (SwoopInducedDrag: the thick,
        // open-nosed wing working hard in steady flight and the flare, little in a dive), rigged
        // SwoopRigging nose-down: ~17 m/s forward and ~7 m/s down in steady flight (2.5:1), ~41 m/s
        // on the front risers, ~160 m level from a 40 m/s dive (offline simulation, 2026-10-22).
        // Its speeds (trim, steering, stall, the brakes' reach, the opening's hold) are the
        // ram-air's times SwoopSpeedScale (the square root of the areas' ratio); the flare's range
        // times SwoopFlareScale, its steady glide's against the ram-air's (17.7 against 15.4 m/s
        // equivalent airspeed, in game), so the deep pull starts as far below its own steady speed.
        private const float SwoopSize = 0.771f;
        private const float SwoopBaseDrag = 0.045f;
        private const float SwoopInducedDrag = 0.40f;
        private const float SwoopRigging = 10f;
        private const float SwoopSpeedScale = 1.297f;
        private const float SwoopFlareScale = 1.15f;

        // ... and its front riser turns roll SwoopFrontRoll times as hard (FrontTurnRoll): a
        // small wing rolls quickly; twice as fast into the turn: 270° in ~4.5 s instead of
        // ~8.5 s from a dive, a full turn held ending in the same spiral (~40 m/s) as before
        // (offline 3D simulation, 2026-10-22).
        private const float SwoopFrontRoll = 3f;

        // ... and its toggles turn it SwoopToggleTurn times as hard (FullPullTurnRate): from its
        // steady glide a full toggle banks it 60° in ~2.4 s instead of ~7.9 s and turns the
        // heading 90° in ~2.5 s instead of ~4.3 s; held, the turn settles (~75° bank, ~25 m/s
        // down) as before (offline 3D simulation with the toggles, 2026-10-23).
        private const float SwoopToggleTurn = 2.5f;

        // ... and a turn in its brakes lets the other toggle up all the way for a full turn
        // (BrakeTurnLetUp: 0.4 on the ram-air): in full brakes at ~9 m/s a third of a turn input
        // yaws ~23°/s instead of ~10, a full one ~52°/s instead of ~27 (offline 3D simulation).
        private const float SwoopBrakeTurnLetUp = 1f;

        // ... and with the ground close its deep pull starts from SwoopDeepPullFromLate (m/s,
        // equivalent airspeed), below its steady speed (17.7): scaled like the rest (17.8) it
        // started at full flying speed and ballooned. Simulated, the best brakes-held landing
        // goes from grade 6 (2.8 m/s down, 6.8 forward) to 8 (1.2 down, 7.3 forward), the
        // heights it runs out from staying 6-8 m.
        private const float SwoopDeepPullFromLate = 16.5f;

        /// <summary>The swoop wing is the chosen canopy.</summary>
        private bool IsSwoopWing => _saved != null && _saved.SwoopWing;

        /// <summary>The wing as drawn: WingScale, the swoop wing's SwoopSize of it.</summary>
        private float WingDrawScale => IsSwoopWing ? WingScale * SwoopSize : WingScale;

        /// <summary>The wing's area (m²).</summary>
        private float WingArea => IsSwoopWing ? RamAirArea * SwoopSize * SwoopSize : RamAirArea;

        private float WingBaseDrag => IsSwoopWing ? SwoopBaseDrag : BaseDrag;

        private float WingInducedDrag => IsSwoopWing ? SwoopInducedDrag : InducedDrag;

        private float WingRigging => IsSwoopWing ? SwoopRigging : AttitudeRigging;

        /// <summary>The wing's speeds against the ram-air's.</summary>
        private float WingSpeedScale => IsSwoopWing ? SwoopSpeedScale : 1f;

        /// <summary>The toggle turn's strength against the ram-air's.</summary>
        private float WingToggleTurnScale => IsSwoopWing ? SwoopToggleTurn : 1f;

        /// <summary>The front riser turn's roll against the ram-air's.</summary>
        private float WingFrontRollScale => IsSwoopWing ? SwoopFrontRoll : 1f;

        /// <summary>The flare's speeds against the ram-air's.</summary>
        private float WingFlareScale => IsSwoopWing ? SwoopFlareScale : 1f;

        // Shape details of a real ram-air (visual only): the cell mouths (the underside starts
        // WingMouth of the chord behind the nose, which sits WingNoseLift above the underside:
        // each opening faces ~10° below the chord, into the air in normal flight, which meets the
        // drawn wing ~10° from below), a rectangular planform (PlanformScale), and a stabilizer
        // panel hanging WingStabilizerDepth below each tip. In section the wing is a wedge: the
        // top rising round from the nose over the front WingNoseRound of the chord to
        // WingThickness above the underside's start and flat behind it; the underside one flat plane,
        // rising straight from the cell mouths' lip (WingMouth) to meet the top at the trailing
        // edge.
        private const float WingNoseRound = 0.1f;
        private const float WingEdgeArch = 0.03f;

        // The cell bulge fades out gently from this much of the chord to the trailing edge.
        private const float WingBulgeFadeFrom = 0.75f;

        /// <summary>
        /// The top's height above the underside's start at <paramref name="chord"/> (unscaled): from
        /// the nose (WingNoseLift) a squared quarter ellipse, rising straight up first and curving
        /// back to the full thickness at WingNoseRound of the chord, flat behind it.
        /// </summary>
        private static float TopProfile(float chord) =>
            chord < WingNoseRound
                ? WingNoseLift + (WingThickness - WingNoseLift) * QuarterEllipse(Mathf.Max(chord, 0f) / WingNoseRound)
                : WingThickness;

        // How square the nose's curve is (2 a quarter ellipse; higher rises straight up for longer).
        private const float WingNoseSquareness = 2.5f;

        /// <summary>
        /// A quarter of a squared ellipse (WingNoseSquareness) from 0 (vertical) to 1 (level) over
        /// <paramref name="t"/> 0..1.
        /// </summary>
        private static float QuarterEllipse(float t)
        {
            var back = 1f - Mathf.Clamp01(t);
            return Mathf.Pow(1f - Mathf.Pow(back, WingNoseSquareness), 1f / WingNoseSquareness);
        }
        private const float WingMouth = 0.016f;
        private const float WingNoseLift = 0.22f;
        // The front of the chord a stall softens and drops (DroodParachute.Flutter.cs).
        private const float WingNoseLength = 0.25f;
        private const float WingStabilizerDepth = 0.2f;

        /// <summary>
        /// Chord (and thickness) at <paramref name="span"/> as a fraction of the middle's: the
        /// same everywhere (a rectangular planform: straight leading and trailing edges, square
        /// tips, like a typical 9-cell sport canopy).
        /// </summary>
        private static float PlanformScale(float span) => 1f;

        /// <summary>The <see cref="WingPoint"/> loop value of the underside at a chord position.</summary>
        private static float UndersideLoop(float chord) =>
            SectionShareAt(Mathf.Clamp01((chord - WingMouth) / (1f - WingMouth))) * 0.5f;

        /// <summary>The <see cref="WingPoint"/> loop value of the top at a chord position.</summary>
        private static float TopLoop(float chord) =>
            1f - SectionShareAt(Mathf.Clamp01(chord)) * 0.5f;

        // Around the section the points lie closer together toward the nose and toward the
        // trailing edge: a share t of the way from the nose is at 1 - (1 - t^SectionNosePower)^
        // SectionTailPower of the chord. The nose's round, vertical front shows, and the rear,
        // which a pulled line curls through up to ~70°, bends in six steps on each side instead
        // of four flat facets (from the side it looked like a hinged aileron); the flat middle
        // gets fewer.
        private const float SectionNosePower = 1.8f;
        private const float SectionTailPower = 1.6f;

        /// <summary>The chord position (0 nose, 1 trailing edge) a share t of the way round half the section.</summary>
        private static float SectionChord(float t) =>
            1f - Mathf.Pow(1f - Mathf.Pow(Mathf.Clamp01(t), SectionNosePower), SectionTailPower);

        /// <summary>The share of the way round half the section at a chord position (SectionChord's inverse).</summary>
        private static float SectionShareAt(float chord) =>
            Mathf.Pow(1f - Mathf.Pow(1f - Mathf.Clamp01(chord), 1f / SectionTailPower), 1f / SectionNosePower);

        // Lines from the shoulders to the wing's underside (m, to the middle of the span).
        private const float RamAirLineLength = 4f;

        // While opening, the half-inflated wing is mostly drag (RamAirAttitudeForce).
        private const float RamAirOpeningDrag = 1f;

        // The opening (from line stretch) takes RamAirInflateSeconds. The slider slows it while the
        // drood is fast: above RamAirSliderSpeed the wing opens at RamAirSliderRate of that rate,
        // at the full rate below RamAirSliderFullSpeed. It always opens fully (limited by speed
        // instead, it could stay half open). From line stretch the wing's area grows from nothing
        // with the inflation to the power RamAirOpeningCurve (its mouth opens gradually), so the
        // pull builds up through the opening: from terminal speed it peaks at ~3.5 g (3.8 from a
        // fast track), full ~2.9 s after line stretch, flying (≤6 m/s down) ~250 m below the pull
        // (offline simulation; a real sport canopy ~150-250 m, 3-4 g).
        private const float RamAirInflateSeconds = 2f;
        private const float RamAirSliderSpeed = 45f;
        private const float RamAirSliderFullSpeed = 20f;
        private const float RamAirSliderRate = 0.5f;
        private const float RamAirOpeningCurve = 1.5f;

        // The wing's lift comes in while it is still opening: from RamAirLiftFromOpen to
        // RamAirFullLiftOpen of the inflation.
        private const float RamAirLiftFromOpen = 0.05f;
        private const float RamAirFullLiftOpen = 0.4f;

        // Side force against sideslip (per unit of dynamic pressure x area): the wing flies
        // where it points, so a turned heading turns the flight path.
        private const float RamAirSideForce = 1f;

        // The ram-air's steering lines take this long (s) to pull all the way, so a key press
        // eases into the turn.
        private const float RamAirToggleSeconds = 0.3f;

        // Letting a line go is quick (s to release it fully); only pulling it down takes time
        // (released as slowly, reversing a turn keeps turning the old way for a while).
        private const float RamAirToggleReleaseSeconds = 0.15f;

        // The front risers (W) take this long (s) to pull all the way and to let go (quicker), evenly. A
        // quick change lowers (or raises) the wing's angle to the air before the canopy can
        // follow: its lift changes faster than its drag, so its force tilts back (or forward)
        // and the canopy moves aft on W and forward on release (held back there,
        // FrontReleaseForwardLag).
        private const float RamAirFrontRiserSeconds = 1.5f;
        private const float RamAirFrontRiserReleaseSeconds = 1f;

        // The wing is drawn this much (degrees) less nose-down than the flight model's pitch
        // (visual only): in a 3:1 glide the model's wing sits ~12° nose-down to the horizon, a
        // real canopy about 5-10°.
        private const float WingDrawnNoseUp = 5f;

        // ... and of the front risers' nose-down only this share is drawn (~6° at a full pull).
        private const float FrontRiserDrawn = 0.32f;
        private const float FrontSurgeDrawn = 0.3f;

        // The wing's trim speed (full flight, m/s; 3.2:1).
        private const float RamAirTrimSpeed = 15f;

        // The speed the steering's turn and roll rates are given for (m/s): they grow and shrink
        // with the airspeed against it.
        private const float RamAirSteeringSpeed = 17f;

        // Lines, cascading like a real ram-air's: on each side, rows A-D (chord positions) at
        // LineSpanStations (fraction of the half span) meet at that side's two risers, A and B
        // at the front riser, C and D at the rear one, plus the steering line from the trailing
        // edge to the rear riser (SteeringAttachStations). The risers run
        // RiserLength from the shoulder, RiserSpread in front of and behind it. The hands hold
        // the rear risers (steering toggles), or the front ones while the front risers are
        // pulled. The rows sit where a sport canopy's do (the D row about three quarters back:
        // behind it the trailing edge is free, held only by the steering line). The stations are
        // the loaded ribs, at the cell boundaries out to the tip's end rib (the half span holds
        // 4.5 cells: 1, 3, 5, 7 and 9 ninths of it).
        private static readonly float[] LineSpanStations = { 1f / 9f, 3f / 9f, 5f / 9f, 7f / 9f, 1f };
        private static readonly float[] LineChordStations = { 0.08f, 0.3f, 0.52f, 0.75f };
        // The steering line runs up from the rear riser to a cascade, SteeringCascadeBelow (m)
        // under the trailing edge, and fans out there to the trailing edge as on a sport
        // canopy: the tip's end rib, the next cell divider, the middle rib of the cell inside
        // it and the divider beyond (SteeringAttachStations of the half span; the cells are 2/9
        // of it). Those points are pulled down; the trailing edge between them sags less
        // (DroodParachute.Flutter.cs), one arch in the outer cell, two in the next.
        internal static readonly float[] SteeringAttachStations = { 5f / 9f, 6f / 9f, 7f / 9f, 1f };
        private const float SteeringCascadeBelow = 1.2f;
        private const float RiserLength = 0.5f;
        private const float RiserSpread = 0.07f;
        private static readonly int RamAirLinesPerSide = LineChordStations.Length * LineSpanStations.Length + 1 + SteeringAttachStations.Length;
        private static readonly int RamAirLineCount = 2 * RamAirLinesPerSide;

        // Slider: a square of fabric on the lines that slows the opening; it slides down as the
        // wing inflates and rests on the risers' tops (its grommets on the connector links), where
        // the line groups meet the risers. The line groups run through its four corner rings: at
        // line stretch it is up under the wing (SliderTopGap below the line groups' tops), holding
        // them together; the wing spreading as it inflates pushes it down to the risers, and there
        // it stays until the canopy is packed (AG1 off), also if the canopy deflates and reopens.
        // While it is more than SliderBendAbove (m) above its rest, the lines bend at its corners.
        private const float SliderRest = 0f;
        private const float SliderBendAbove = 0.05f;
        private const float SliderTopGap = 0.3f;

        // How far the slider has come down (0 at the top, 1 resting on the risers; only grows),
        // and its corners as placed this frame: left front, right front, right rear, left rear.
        private float _sliderDown;
        private readonly Vector3[] _sliderCorners = new Vector3[4];

        // Pilot chute: a small round canopy PilotChuteRadius wide, in the drood's Trim 1 colour,
        // on a BridleLength bridle (out in full once it is thrown: it pulls the canopy out), from
        // the container, then the bag, then the top of the wing. Collapsible, as on most sport
        // rigs: once the wing is open its kill line pulls it in over PilotCollapseSeconds, and it
        // trails as a limp tube PilotCollapsedWidth of its open width and PilotCollapsedLength
        // of its open length.
        private const float PilotChuteRadius = 0.3f;

        // Deployment (ram-air), in order:
        // 1. the right hand reaches back to the pilot chute pouch at the bottom of the container
        //    (the first HandReachFraction of HandThrowSeconds), then sweeps the pilot chute out to
        //    the side at arm's length and lets go (DroodPilotThrow); it is held folded
        //    (PilotHeldSize of its open size);
        // 2. from where it was let go, the pilot chute catches the air and its bridle pays out
        //    (PilotThrowSeconds);
        // 3. it pulls the deployment bag out (BagSize, m: across, deep, long; a soft pouch with
        //    the folded wing inside, in paint slot BagPaintSlot, Trim 2), and the lines pay out
        //    from the bag (LineExtendSeconds);
        // 4. at line stretch the wing leaves the bag over the first BagReleaseInflation of its
        //    inflation, while the empty bag slides up the bridle (toward BagOnBridle of the way
        //    to the pilot chute) and shrinks away.
        private const float HandThrowSeconds = 0.7f;
        internal const float HandReachFraction = 0.57f;
        private const float PilotHeldSize = 0.3f;
        private const float PilotThrowSeconds = 0.4f;
        private static readonly Vector3 BagSize = new Vector3(0.25f, 0.15f, 0.35f);
        // The bag's centre, this far (m) up the lines from their end (its flap at their end).
        private const float BagCentreUpLines = 0.18f;
        // Its shape (BuildBagMesh): roundness of the box (1 an ellipsoid, toward 0 square), and
        // the closing flap: its width and depth (fractions of the bag's), thickness (m) and tilt
        // (degrees).
        private const float BagRoundness = 0.35f;
        private const float BagFlapWidth = 0.8f;
        private const float BagFlapDepth = 0.7f;
        private const float BagFlapThickness = 0.002f;
        private const float BagFlapTilt = 5f;
        private const int BagPaintSlot = 2;
        private const float BagReleaseInflation = 0.15f;
        private const float BagOnBridle = 0.35f;
        private const float BagGoneFaster = 4f;

        // The pouch: this far (m) to the right of and below the middle of the container.
        private const float PouchRight = 0.1f;
        private const float PouchBelow = 0.12f;

        // The wing inside the bag: hidden (nearly zero scale); it grows out of the bag's mouth
        // as it leaves.
        private static readonly Vector3 WingInBag = new Vector3(0.001f, 0.001f, 0.001f);

        // The container on the drood's back: this far (m) below the middle of the shoulders
        // and behind them.
        private const float ContainerBelowShoulders = 0.25f;
        private const float ContainerBehind = 0.18f;

        // Where the bridle leaves the top skin of the middle cell (fraction of the chord from
        // the leading edge): near the trailing edge, so the bridle doesn't run along the curved
        // top skin and show through it.
        private const float BridleAttachChord = 0.85f;
        private const float BridleLength = 2.5f;

        // Draped back over the top, the bridle follows the top skin to the trailing edge in
        // BridleDrapePieces pieces, BridleOnSkin (m) above it (a straight line would cut under
        // the curved skin).
        private const int BridleDrapePieces = 4;
        private const float BridleOnSkin = 0.02f;
        private const float PilotCollapseSeconds = 1f;
        private const float PilotCollapsedWidth = 0.2f;
        private const float PilotCollapsedLength = 1.8f;

        // It trails in the airflow behind the point it hangs from: its drag (drag area, m²,
        // open and collapsed) against its weight (PilotChuteMass, kg, with the bridle) sets how
        // far it sags; the bridle swings toward that direction within PilotSwingSeconds. A
        // collapsed one sags ~10° at trim speed (15 m/s) and ~16° in a stall's fall (11.5 m/s, so
        // it streams up), but ~38° at 7 m/s and ~57° at 5 m/s: slow, its weight wins and it
        // drops; an open one hardly sags at all.
        private const float PilotOpenDragArea = 0.5f;
        private const float PilotCollapsedDragArea = 0.065f;
        private const float PilotChuteMass = 0.15f;
        private const float PilotSwingSeconds = 0.25f;

        // The pilot chute trails the relative wind turned this much (degrees) toward the drood.
        private const float PilotTrailBelowWind = 15f;

        // It flutters in the wing's wake: up and down by up to PilotFlutterUpDown and side to
        // side by up to PilotFlutterSideways (degrees, at trim speed; open half as much), at
        // about PilotFlutterRate cycles per second, irregular.
        private const float PilotFlutterUpDown = 4f;
        private const float PilotFlutterSideways = 1.5f;
        private const float PilotFlutterRate = 2.25f;

        // Round-canopy paint slot the pilot chute takes (1: Trim 1).
        private const int PilotChutePaintSlot = 1;

        // Riser tops as last placed, in the drood's own frame (the hands use them in the next
        // frame's IK; in world space they would lag behind at flying speed and pull the hands
        // aft): [side (0 left, 1 right) * 2 + (0 front, 1 rear)].
        private readonly Vector3[] _riserTops = new Vector3[4];
        private GameObject _ramAirExtras;
        private Transform _slider;
        private Mesh _sliderMesh;
        private readonly Vector3[] _sliderVertices = new Vector3[8];
        private Transform _pilotChute;
        private Mesh _pilotChuteMesh;
        private LineRenderer _bridle;
        private Transform _bag;
        private Mesh _bagMesh;

        // Deployment progress: the pilot chute's bridle paying out (0..1), and how far the wing
        // is out of its bag (0..1, only ever grows).
        private float _pilotOut = 1f;

        // The hand throw (0..1, see HandThrowSeconds) and where the pilot chute was let go (in
        // the drood's own frame).
        private float _handThrow = 1f;
        private Vector3 _pilotReleaseLocal;
        private Transform _rightHand;
        private float _bagRelease = 1f;
        private float _pilotCollapse;
        private Vector3 _pilotTrail;

        // The airflow, density and down direction of the last physics step, for the pilot chute.
        private Vector3 _visualAirVelocity;
        private float _visualDensity;
        private Vector3 _visualDown = Vector3.down;
        private Vector3 _visualGravity = Vector3.down * 9.81f;
        private Vector3 _visualAcceleration;
        private bool _hasVisualAirVelocity;

        // The drood's acceleration is smoothed over this long (s) for the apparent gravity.
        private const float ApparentGravitySmoothing = 0.2f;

        // The bridle leaves the top skin: it never points further into the wing than this
        // (the sine of the angle below the wing's chord plane).
        private const float BridleMinAboveWing = -0.05f;

        // After touchdown the trailing edge's shape is gone by this much of the collapse (the
        // wing tips over from 0.45 and folds from 0.65).
        private const float TrailingReleaseCollapse = 0.35f;

        private readonly List<Renderer> _extraFabricRenderers = new List<Renderer>();

        private bool IsRamAir => _canopyKind == CanopyKind.RamAir;

        /// <summary>How stalled the ram-air is (0..1, the more stalled half), for the flapping sound.</summary>
        internal float StallAmount => IsRamAir ? StallShown(Mathf.Max(_leftStall, _rightStall)) : 0f;

        /// <summary>The drawn wing's forward direction (world; zero for the round canopy), for the Canopy Chase.</summary>
        internal Vector3 WingForward => IsRamAir && _canopyRoot != null ? _canopyRoot.forward : Vector3.zero;

        /// <summary>The drawn wing's up direction (world), with <see cref="WingForward"/>.</summary>
        internal Vector3 WingUp => _canopyRoot != null ? _canopyRoot.up : Vector3.up;

        private float CurrentLineLength => IsRamAir ? RamAirLineLength : LineLength;

        /// <summary>
        /// A point on the wing in canopy-root space (+X right along the span, +Y up, +Z
        /// forward), with the root at the middle of the span's underside. <paramref name="span"/>
        /// runs 0..1 from the left tip to the right tip, <paramref name="loop"/> once around the
        /// airfoil: 0 the leading edge's underside, 0.5 the trailing edge, 1 the leading edge's
        /// top. <paramref name="inset"/> (m) moves the point into the airfoil.
        /// </summary>
        private static Vector3 WingPoint(float span, float loop, float inset = 0f)
        {
            // Around the airfoil: the underside from the cell mouths' lower lip (WingMouth of the
            // chord behind the nose) back to the trailing edge, then the top from the trailing
            // edge forward to the nose, which sits WingNoseLift above the underside. Between the
            // nose and the lip each cell is open: its air intake.
            var bottom = loop <= 0.5f;
            var chord = bottom
                ? Mathf.Lerp(WingMouth, 1f, SectionChord(loop * 2f))
                : SectionChord(Mathf.Max((1f - loop) * 2f, 0f));
            var scale = PlanformScale(span);
            // The underside's rise (straight from the lip to the top at the trailing edge) and the
            // top (TopProfile).
            var rise = WingThickness * scale * Mathf.Max(chord - WingMouth, 0f) / (1f - WingMouth);
            var top = scale * TopProfile(chord);
            var thickness = Mathf.Max(top - rise, 0f);
            // inset moves a point into the airfoil (up from the underside, down from the top),
            // at most half the thickness there, so the inner surface doesn't coincide with the
            // outer one.
            inset = Mathf.Min(inset, thickness * 0.5f);
            var height = rise + (bottom ? inset : thickness - inset);

            // Planform (PlanformScale): a chord shortened toward the tips would come mostly off
            // the leading edge.
            var localChord = WingChord * scale;
            var leadingEdge = 0.5f * WingChord - (WingChord - localChord) * 0.8f;
            var forward = leadingEdge - chord * localChord;
            if (!bottom)
            {
                // The top of each cell bulges between the ribs, flattening out toward the
                // trailing edge (a smooth edge) and carrying on to the nose: out from the surface,
                // so up on top and forward on the nose's steep front, and up as well along the
                // front's lower edge (WingEdgeArch of the chord), so each cell opening's top edge
                // is an arch, pulled down at the ribs.
                var bulge = WingCellBulge * scale * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * WingCells * span))
                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, WingBulgeFadeFrom, chord));
                var slope = (TopProfile(chord + 0.002f) - TopProfile(Mathf.Max(chord - 0.002f, 0f))) * scale
                    / ((chord + 0.002f - Mathf.Max(chord - 0.002f, 0f)) * localChord);
                var normalLength = Mathf.Sqrt(1f + slope * slope);
                height += bulge * (1f / normalLength + Mathf.Clamp01(1f - chord / WingEdgeArch));
                forward += bulge * slope / normalLength;
            }

            // Arched along the span, tips down toward the jumper (the arch's centre is below the
            // wing), each section tilted with the arch.
            var arc = WingArcDegrees * Mathf.Deg2Rad;
            var radius = WingSpan / arc;
            var angle = (span - 0.5f) * arc;
            var outward = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
            var centre = new Vector3(0f, -radius, 0f);
            return centre + outward * (radius + height) + Vector3.forward * forward;
        }

        // Each cell's paint slot from the left tip (0 Primary, 1 Trim 1, 2 Trim 2), symmetric like
        // a real canopy (the round canopy's stripes are too narrow to sample per cell).
        private static readonly int[] CellPaintSlots = { 1, 0, 2, 0, 1, 0, 2, 0, 1 };

        // The wing on the ground is drawn squashed to LandedWingHeight of its height; its
        // shape there is LandedPoint's (DroodParachute.LandedWing.cs).
        private const float LandedWingHeight = 0.12f;
        // The wing on the ground is turned rear down by this much (degrees) and lowered by
        // LandedSink (m) below its lowest point's resting height, so its front and rear rest on
        // the ground too.
        private const float LandedRearDown = 3f;
        private const float LandedSink = 0.07f;

        // Performance Log parts of PlaceRamAirVisuals.
        private int _foldSlot = -1;
        private int _linesSlot = -1;
        private int _extrasSlot = -1;

        // The wing mesh's own vertices, and how much of the ground lumps they have now.
        private Vector3[] _wingRestVertices;
        private float _wingLumps;

        // The lowest point of the wing mesh as bent now (its own space, height), so the landed
        // wing rests on the ground with it.
        private float _wingLowest;

        // Wing mesh resolution: segments across each cell (for the cell bulge) and points
        // around the airfoil.
        private const int WingSegmentsPerCell = 8;

        // Where the steering line attaches (from the first of SteeringAttachStations out to the
        // tip) the trailing edge arches between the points, half a cell or a cell long: those
        // half-cells get WingSegmentsPerCellSteered segments, so the shallow arches are round
        // (four segments drew them as flat-topped notches).
        private const int WingSegmentsPerCellSteered = 12;

        /// <summary>The segments across half-cell <paramref name="half"/> (of 2 · WingCells, from the left tip).</summary>
        private static int SegmentsPerHalf(int half)
        {
            var outward = Mathf.Abs((half + 0.5f) / (2f * WingCells) * 2f - 1f);
            return (outward >= SteeringAttachStations[0] ? WingSegmentsPerCellSteered : WingSegmentsPerCell) / 2;
        }
        private const int WingLoopPoints = 48;

        // The skin's points per column: WingLoopPoints + 1 round the section, the trailing edge
        // twice (BuildWingMesh).
        private const int SkinColumnPoints = WingLoopPoints + 2;

        /// <summary>The loop value of a skin column's point <paramref name="k"/> (the trailing edge at both WingLoopPoints / 2 and the next).</summary>
        private static float SkinLoop(int k) => (float)(k <= WingLoopPoints / 2 ? k : k - 1) / WingLoopPoints;

        /// <summary>
        /// The wing mesh, built from the wing shape (<see cref="WingPoint"/>): an outer surface
        /// facing out and an inner surface <see cref="ShellGap"/> inside it facing in, so the
        /// fabric shows from both sides; the leading edge is closed where the top and the
        /// underside meet; both tips get an end panel drawn from both sides. The round canopy's
        /// mesh only lends its paint: each cell copies the vertex attributes (part-paint slot,
        /// UVs) of a round-canopy vertex of its slot (<see cref="CellPaintSlots"/>), so the cells
        /// take the drood's Primary, Trim 1 and Trim 2. (The round canopy's own mesh can't be bent
        /// into the wing without gaps and see-through tips.)
        /// </summary>
        // The half-cells' seams are sharp only where the top bulges: up to this loop value (the
        // underside, the trailing edge and the top behind WingBulgeFadeFrom of the chord) the
        // coinciding points share their normal, so the surface shades smoothly across them.
        private static readonly float SmoothSeamLoop = TopLoop(WingBulgeFadeFrom);
        private int[] _seamA;
        private int[] _seamB;
        private readonly List<Vector3> _seamNormals = new List<Vector3>();

        /// <summary>After RecalculateNormals: the smooth seams' points share their averaged normal.</summary>
        private void WeldSeamNormals(Mesh mesh)
        {
            if (_seamA == null || _seamA.Length == 0)
            {
                return;
            }

            mesh.GetNormals(_seamNormals);
            for (var i = 0; i < _seamA.Length; i++)
            {
                var a = _seamA[i];
                var b = _seamB[i];
                if (a < _seamNormals.Count && b < _seamNormals.Count)
                {
                    var shared = (_seamNormals[a] + _seamNormals[b]).normalized;
                    _seamNormals[a] = shared;
                    _seamNormals[b] = shared;
                }
            }

            mesh.SetNormals(_seamNormals);
        }

        private Mesh BuildWingMesh(Mesh dome)
        {
            var attributes = new VertexCopier(dome);
            var source = dome.vertices;
            var axisA = (_meshHeightAxis + 1) % 3;
            var axisB = (_meshHeightAxis + 2) % 3;
            var insets = InnerShellInsets(dome, source, axisA, axisB);

            // Per cell, a round-canopy vertex of that cell's paint slot (CellPaintSlots): on the
            // outer shell if there is one. A vertex's slot is uv0.w modulo 10 (the tens digit is
            // the subpart style): the round canopy uses Primary, Trim 1 and Trim 2.
            var uvs = new List<Vector4>();
            dome.GetUVs(0, uvs);
            var slotVertex = new int[3];
            for (var slot = 0; slot < slotVertex.Length; slot++)
            {
                slotVertex[slot] = -1;
                for (var i = 0; i < source.Length && i < uvs.Count; i++)
                {
                    if (Mathf.FloorToInt(uvs[i].w) % 10 == slot && (slotVertex[slot] < 0 || insets[i] <= 0f))
                    {
                        slotVertex[slot] = i;
                        if (insets[i] <= 0f)
                        {
                            break;
                        }
                    }
                }
            }

            var paint = new int[WingCells];
            for (var cell = 0; cell < WingCells; cell++)
            {
                var vertex = slotVertex[CellPaintSlots[cell % CellPaintSlots.Length]];
                paint[cell] = vertex >= 0 ? vertex : Mathf.Max(slotVertex[0], 0);
            }

            var positions = new List<Vector3>();
            var triangles = new List<int>();
            // Each column runs round the section with the trailing edge twice: once ending the
            // underside, once starting the top, the same place but each lit by its own side (one
            // shared point took the mean of both sides' light: a dark band, and dark teeth when a
            // full pull curls them to face apart). No face between the two.
            var row = SkinColumnPoints;
            var seamA = new List<int>();
            var seamB = new List<int>();
            for (var shell = 0; shell < 2; shell++)
            {
                var inset = shell == 0 ? 0f : ShellGap;
                // Each half-cell its own piece, so every rib's seam is sharp (the light isn't
                // smoothed across it).
                var halves = 2 * WingCells;
                for (var half = 0; half < halves; half++)
                {
                    var cell = half / 2;
                    var perHalf = SegmentsPerHalf(half);
                    var first = positions.Count;

                    // Where the surface runs on smoothly across the seam with the half before
                    // (the underside, and the top's rear where the bulge has faded), the two
                    // pieces' coinciding points share their normal (WeldSeamNormals).
                    if (half > 0)
                    {
                        for (var k = 0; k < row; k++)
                        {
                            if (SkinLoop(k) <= SmoothSeamLoop)
                            {
                                seamA.Add(first - row + k);
                                seamB.Add(first + k);
                            }
                        }
                    }
                    for (var s = 0; s <= perHalf; s++)
                    {
                        var span = (half + (float)s / perHalf) / halves;
                        for (var k = 0; k < row; k++)
                        {
                            positions.Add(WingPoint(span, SkinLoop(k), inset));
                            attributes.Copy(paint[cell]);
                        }
                    }

                    for (var s = 0; s < perHalf; s++)
                    {
                        var span = (half + (s + 0.5f) / perHalf) / halves;
                        for (var k = 0; k < row - 1; k++)
                        {
                            if (k == WingLoopPoints / 2)
                            {
                                continue;
                            }

                            // Into the airfoil: up from the underside, down from the top.
                            var loop = 0.5f * (SkinLoop(k) + SkinLoop(k + 1));
                            var inside = ArchUp(span) * (loop < 0.5f ? 1f : -1f);
                            var a = first + s * row + k;
                            AddQuad(triangles, positions, a, a + row, a + 1, a + row + 1, shell == 0 ? -inside : inside);
                        }
                    }
                }
            }

            // End panels at the tips, ribs at the cell boundaries (the loaded ones) and in the middle
            // of each cell (carrying no lines), a stabilizer under each tip.
            for (var boundary = 0; boundary <= WingCells; boundary++)
            {
                AddSection((float)boundary / WingCells, paint[Mathf.Min(boundary, WingCells - 1)], positions, attributes, triangles);
            }

            for (var cell = 0; cell < WingCells; cell++)
            {
                AddSection((cell + 0.5f) / WingCells, paint[cell], positions, attributes, triangles);
            }

            AddStabilizer(0f, paint[0], positions, attributes, triangles);
            AddStabilizer(1f, paint[WingCells - 1], positions, attributes, triangles);

            var wing = new Mesh { name = "DroodRamAirWing" };
            if (positions.Count > 65535)
            {
                wing.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            wing.SetVertices(positions);
            attributes.Apply(wing);
            wing.subMeshCount = dome.subMeshCount;
            for (var sub = 0; sub < dome.subMeshCount; sub++)
            {
                wing.SetTriangles(sub == 0 ? triangles : new List<int>(), sub);
            }

            _seamA = seamA.ToArray();
            _seamB = seamB.ToArray();
            wing.RecalculateNormals();
            WeldSeamNormals(wing);
            wing.RecalculateBounds();
            if (wing.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
            {
                wing.RecalculateTangents();
            }

            _wingRestVertices = positions.ToArray();
            _wingLumps = 0f;
            _wingLowest = Lowest(_wingRestVertices);
            Log($"Ram-air wing built: {positions.Count} vertices, {triangles.Count / 3} triangles, size {DroodLimbSurfaces.Fmt(wing.bounds.size)}.");
            return wing;
        }

        /// <summary>The wing section's up direction at <paramref name="span"/> (it tilts with the arch).</summary>
        private static Vector3 ArchUp(float span)
        {
            var angle = (span - 0.5f) * WingArcDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
        }

        /// <summary>
        /// Two triangles for the quad a-b (along the span) / c-d (the next points around the
        /// airfoil), wound to face <paramref name="facing"/>: a Unity front face has
        /// Cross(b - a, c - a) toward the viewer (judged from both triangles, so a quad with one
        /// corner doubled still turns the right way).
        /// </summary>
        private static void AddQuad(List<int> triangles, List<Vector3> positions, int a, int b, int c, int d, Vector3 facing)
        {
            var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a])
                + Vector3.Cross(positions[d] - positions[b], positions[c] - positions[b]);
            if (Vector3.Dot(normal, facing) < 0f)
            {
                var swap = b;
                b = c;
                c = swap;
            }

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(d);
            triangles.Add(c);
        }

        // The end panels and ribs take the skin's own points around the section (WingLoopPoints),
        // so their edges lie exactly on the skin.
        private const int EndPanelPoints = WingLoopPoints + 1;

        // Gap (m) between the wing's outer and inner surfaces, and between the two faces of an
        // end panel, so no two faces coincide and fight over which is drawn.
        private const float ShellGap = 0.01f;

        /// <summary>
        /// Per vertex of the (double-sided) round canopy: <see cref="ShellGap"/> for vertices of
        /// its inner shell (faces turned toward the dome's axis), 0 for the outer shell. Used to
        /// take the paint from the outer shell.
        /// </summary>
        private float[] InnerShellInsets(Mesh dome, Vector3[] vertices, int axisA, int axisB)
        {
            var outer = new int[vertices.Length];
            var inner = new int[vertices.Length];
            for (var sub = 0; sub < dome.subMeshCount; sub++)
            {
                var triangles = dome.GetTriangles(sub);
                for (var t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                    var normal = Vector3.Cross(b - a, c - a);
                    var centre = (a + b + c) / 3f;
                    var radial = Vector3.zero;
                    radial[axisA] = centre[axisA];
                    radial[axisB] = centre[axisB];
                    var counts = Vector3.Dot(normal, radial) >= 0f ? outer : inner;
                    counts[triangles[t]]++;
                    counts[triangles[t + 1]]++;
                    counts[triangles[t + 2]]++;
                }
            }

            var insets = new float[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                insets[i] = inner[i] > 0 && outer[i] == 0 ? ShellGap : 0f;
            }

            return insets;
        }

        /// <summary>
        /// An airfoil-shaped panel across the wing at <paramref name="span"/>: an end panel at a
        /// tip, or a rib at a cell boundary (seen through the cell mouths).
        /// </summary>
        private static void AddSection(float span, int like, List<Vector3> positions, VertexCopier attributes, List<int> triangles)
        {
            var outline = new List<Vector3>(EndPanelPoints);
            for (var i = 0; i < EndPanelPoints; i++)
            {
                outline.Add(WingPoint(span, (float)i / (EndPanelPoints - 1)));
            }

            var along = WingPoint(Mathf.Min(span + 0.01f, 1f), 0.25f) - WingPoint(Mathf.Max(span - 0.01f, 0f), 0.25f);
            if (span <= 0f || span >= 1f)
            {
                // An end panel: pressurised, it puffs out away from the wing.
                AddPillowPanel(outline, along.normalized, span <= 0f ? -along.normalized : along.normalized, like, positions, attributes, triangles);
                return;
            }

            AddPanel(outline, along.normalized, like, positions, attributes, triangles);
        }

        // An end panel puffs out like a pillow: EndPanelRows rows between its top and bottom
        // edges, bulging out up to EndPanelBulge (m) halfway up, in proportion to the section's
        // thickness there (nothing at the trailing edge), flat along its seams with the skin.
        private const int EndPanelRows = 4;
        private const float EndPanelBulge = 0.1f;

        /// <summary>
        /// A closed panel like <see cref="AddPanel"/> (an outline round and back, each point joined
        /// to its partner across), with rows between the partners pushed out along
        /// <paramref name="outward"/>.
        /// </summary>
        private static void AddPillowPanel(List<Vector3> outline, Vector3 across, Vector3 outward, int like, List<Vector3> positions, VertexCopier attributes, List<int> triangles)
        {
            // Up to the trailing edge: with an odd count its middle point pairs with itself.
            var count = outline.Count;
            var pairs = (count + 1) / 2;
            var thickest = 0f;
            for (var i = 0; i < pairs; i++)
            {
                thickest = Mathf.Max(thickest, Vector3.Distance(outline[i], outline[count - 1 - i]));
            }

            for (var face = 0; face < 2; face++)
            {
                var side = face == 0 ? 1f : -1f;
                var offset = across * (0.5f * side * ShellGap);
                var first = positions.Count;
                for (var i = 0; i < pairs; i++)
                {
                    var bottom = outline[i];
                    var top = outline[count - 1 - i];
                    var share = thickest > 0f ? Vector3.Distance(bottom, top) / thickest : 0f;
                    for (var row = 0; row <= EndPanelRows; row++)
                    {
                        // The edges lie on the skin's own edge (no crack), the rows between bulge
                        // out, the two faces ShellGap apart.
                        var t = (float)row / EndPanelRows;
                        var edge = row == 0 || row == EndPanelRows;
                        positions.Add(Vector3.Lerp(bottom, top, t) + outward * (EndPanelBulge * share * Mathf.Sin(Mathf.PI * t)) + (edge ? Vector3.zero : offset));
                        attributes.Copy(like);
                    }
                }

                var rowLength = EndPanelRows + 1;
                for (var i = 0; i < pairs - 1; i++)
                {
                    for (var row = 0; row < EndPanelRows; row++)
                    {
                        var a = first + i * rowLength + row;
                        AddQuad(triangles, positions, a, a + rowLength, a + 1, a + rowLength + 1, across * side);
                    }
                }
            }
        }

        // A stabilizer's top edge runs from StabilizerTopFrom to StabilizerTopTo of the chord
        // through the underside's own points (WingLoopPoints), so it follows the skin exactly as
        // the wing is shaped (brakes, flare, stall); other points, the two straight-segment edges
        // crossed and let light through between them. It sits StabilizerTuck (m) up inside the
        // wing. The bottom edge runs from StabilizerBottomTo back to StabilizerBottomFrom.
        private const float StabilizerTopFrom = 0.2f;
        private const float StabilizerTopTo = 0.95f;
        private const float StabilizerBottomFrom = 0.35f;
        private const float StabilizerBottomTo = 0.85f;
        private const float StabilizerTuck = 0.01f;

        /// <summary>
        /// A stabilizer: a shallow panel along the underside of the wing tip at
        /// <paramref name="span"/>, hanging <see cref="WingStabilizerDepth"/> below it (a
        /// trapezoid, shorter at the bottom; see StabilizerTopFrom).
        /// </summary>
        private static void AddStabilizer(float span, int like, List<Vector3> positions, VertexCopier attributes, List<int> triangles)
        {
            var up = ArchUp(span);
            var down = -up * (WingStabilizerDepth * PlanformScale(span));
            var outline = new List<Vector3>();
            var chords = new List<float>();
            for (var l = 0; l <= WingLoopPoints / 2; l++)
            {
                // The underside's chord at its loop point (as WingPoint places it).
                var chord = Mathf.Lerp(WingMouth, 1f, SectionChord(2f * l / WingLoopPoints));
                if (chord >= StabilizerTopFrom && chord <= StabilizerTopTo)
                {
                    outline.Add(WingPoint(span, (float)l / WingLoopPoints) + up * StabilizerTuck);
                    chords.Add(chord);
                }
            }

            for (var i = chords.Count - 1; i >= 0; i--)
            {
                var share = Mathf.InverseLerp(StabilizerTopFrom, StabilizerTopTo, chords[i]);
                outline.Add(WingPoint(span, UndersideLoop(Mathf.Lerp(StabilizerBottomFrom, StabilizerBottomTo, share))) + down);
            }

            var front = outline[0];
            var rear = outline[chords.Count - 1];
            var frontLow = outline[outline.Count - 1];
            var along = Vector3.Cross(rear - front, frontLow - front).normalized;
            AddPanel(outline, along, like, positions, attributes, triangles);
        }

        /// <summary>
        /// A closed flat panel drawn from both sides: two faces <see cref="ShellGap"/> apart along
        /// <paramref name="across"/>, each with its own vertices so each gets its own normal. The
        /// outline runs round the panel and back (to one point, or two, at the far end), and each point is
        /// joined straight across to its partner on the other side (point i to point n - 1 - i:
        /// top to underside at the same place along the chord), so the panel stays inside the
        /// skin however the skin is curved or shaped (a fan from the middle would cut across the
        /// underside's hollow and a curled trailing edge). Its vertices copy the attributes
        /// (paint slot) of vertex <paramref name="like"/>.
        /// </summary>
        private static void AddPanel(List<Vector3> outline, Vector3 across, int like, List<Vector3> positions, VertexCopier attributes, List<int> triangles)
        {
            var count = outline.Count;
            for (var face = 0; face < 2; face++)
            {
                var side = face == 0 ? 1f : -1f;
                var offset = across * (0.5f * side * ShellGap);
                var first = positions.Count;
                foreach (var point in outline)
                {
                    positions.Add(point + offset);
                    attributes.Copy(like);
                }

                // Pairs up to the trailing edge (the middle point, or the middle two).
                for (var i = 0; i < (count - 1) / 2; i++)
                {
                    AddQuad(triangles, positions, first + i, first + i + 1, first + count - 1 - i, first + count - 2 - i, across * side);
                }
            }
        }

        /// <summary>
        /// Gives new vertices the attributes of a source mesh's vertex other than the position
        /// and normal (UV channels, colours), so they get the same part paint. The source's
        /// attributes are read from, the new mesh's are collected separately.
        /// </summary>
        private sealed class VertexCopier
        {
            private readonly List<Vector4>[] _sourceUvs = new List<Vector4>[8];
            private readonly List<Vector4>[] _uvs = new List<Vector4>[8];
            private readonly List<Color> _sourceColors;
            private readonly List<Color> _colors;

            public VertexCopier(Mesh source)
            {
                for (var channel = 0; channel < _uvs.Length; channel++)
                {
                    if (source.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel))
                    {
                        _sourceUvs[channel] = new List<Vector4>();
                        source.GetUVs(channel, _sourceUvs[channel]);
                        _uvs[channel] = new List<Vector4>();
                    }
                }

                if (source.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                {
                    _sourceColors = new List<Color>();
                    source.GetColors(_sourceColors);
                    _colors = new List<Color>();
                }
            }

            /// <summary>Adds one new vertex's attributes, copied from source vertex <paramref name="index"/>.</summary>
            public void Copy(int index)
            {
                for (var channel = 0; channel < _uvs.Length; channel++)
                {
                    _uvs[channel]?.Add(_sourceUvs[channel][index]);
                }

                _colors?.Add(_sourceColors[index]);
            }

            public void Apply(Mesh mesh)
            {
                for (var channel = 0; channel < _uvs.Length; channel++)
                {
                    if (_uvs[channel] != null)
                    {
                        mesh.SetUVs(channel, _uvs[channel]);
                    }
                }

                if (_colors != null)
                {
                    mesh.SetColors(_colors);
                }
            }
        }

        /// <summary>
        /// Places the wing and its lines. In flight: the wing's underside middle is up the
        /// lines, its span grows while it inflates. Landing: it floats down like the round
        /// canopy (drop), then lies on the ground as an empty canopy
        /// (<see cref="LandedPoint"/>), chord pointing away from the drood.
        /// </summary>
        private void PlaceRamAirVisuals(Vector3 harness, float drop, float tip, float flatten, Quaternion flyingRotation)
        {
            // A stalled wing, down, has no air to hold it up: it comes down, lies flat (top up,
            // as it hung) and folds by StallShapeGoneCollapse of the collapse, as its stalled shape
            // blends into the folded one (StallShown), instead of standing on its edge on its
            // toppling lines or opening up first.
            if (_trailingLanded)
            {
                var stalledFold = Mathf.Max(_leftStall, _rightStall) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, StallShapeGoneCollapse, _visualCollapse));
                drop = Mathf.Max(drop, stalledFold);
                tip = Mathf.Max(tip, stalledFold);
                flatten = Mathf.Max(flatten, stalledFold);
            }

            // While the pilot chute is thrown the bag is still in the container; then the lines
            // pay out with the bag at their end.
            var lineLength = Mathf.Lerp(0.3f, RamAirLineLength, _extend);
            var flyingPosition = _pilotOut < 1f ? ContainerPosition() : CanopyAnchor(harness) + _canopyDirection * lineLength;
            if (_extend >= 1f)
            {
                _bagRelease = Mathf.Max(_bagRelease, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, BagReleaseInflation, _inflate)));
            }

            var away = Vector3.ProjectOnPlane(_landedGround - transform.position, _landedNormal);
            if (away.sqrMagnitude < 1e-4f)
            {
                away = Vector3.ProjectOnPlane(-transform.forward, _landedNormal);
            }

            // Lying behind the drood with its nose toward it, as it flew (the other way round it
            // would spin half round as it comes down), its rear LandedRearDown lower.
            var groundRotation = Quaternion.LookRotation(-away.normalized, _landedNormal) * Quaternion.AngleAxis(-LandedRearDown, Vector3.right);
            // Raised so the wing's lowest point (the arched tips, the deepest lump or fold)
            // rests on the ground, then lowered LandedSink as it folds flat.
            var wingHeight = Mathf.Lerp(Mathf.Lerp(0.25f, 1f, _inflate), LandedWingHeight, flatten);
            var groundPosition = _landedGround + _landedNormal * (0.01f - LandedSink * flatten - _wingLowest * wingHeight * WingDrawScale);

            // Down, the wing toppling toward its fold stays above where it comes to rest.
            if (drop > 0f)
            {
                var below = Vector3.Dot(groundPosition - flyingPosition, _landedNormal);
                if (below > 0f)
                {
                    flyingPosition += _landedNormal * below;
                }
            }

            _canopyRoot.position = Vector3.Lerp(flyingPosition, groundPosition, drop);
            // In flight the wing is rigged nose-down relative to the lines (about +X, its right
            // axis): the flight model's own pitch (_attitudePitch: rigging, front risers, brakes),
            // drawn WingDrawnNoseUp less and with FrontRiserDrawn of the front risers' part and
            // FrontSurgeDrawn of their surge on pulling (a dip that eases as the dive settles).
            // The flight model's pitch without its surge, then the surge as drawn (smoothed).
            var drawnPitch = _attitudePitch - _surgePitchApplied - WingDrawnNoseUp - (1f - FrontRiserDrawn) * AttitudeFrontPitch * FrontPullAverage + FrontSurgeDrawn * _surgeDrawn;
            var rigged = flyingRotation * Quaternion.AngleAxis(drawnPitch, Vector3.right);
            _canopyRoot.rotation = Quaternion.Slerp(rigged * StallShake(), groundRotation, tip);
            _canopyRoot.localScale = Vector3.one;

            var open = Mathf.Lerp(0.25f, 1f, _inflate);
            if (_canopyMesh != null)
            {
                _canopyMesh.localPosition = Vector3.zero;
                _canopyMesh.localRotation = Quaternion.identity;
                var flying = new Vector3(open, wingHeight, Mathf.Lerp(0.5f, 1f, _inflate)) * WingDrawScale;
                _canopyMesh.localScale = _pilotOut < 1f ? Vector3.one * 0.001f : Vector3.Lerp(WingInBag, flying, _bagRelease);
            }

            // Down, the lines are let go: the trailing edge's held shape goes back to the wing's
            // own over the first part of the collapse (before the wing tips over and folds).
            _trailingFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, TrailingReleaseCollapse, _visualCollapse));
            var timed = DroodProfiler.On;
            var shapeStart = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            ApplyGroundLumps(flatten);
            ApplyWingShape(drop <= 0f);
            var linesStart = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            var camera = GetGameCamera();
            CacheWidthCamera(camera);
            var up = transform.up;
            var riserLength = Mathf.Lerp(0.2f, RiserLength, _extend);
            var cascadeLength = Mathf.Max(lineLength - riserLength, 0.5f);
            var heading = Vector3.ProjectOnPlane(_canopyRoot.forward, _canopyDirection).normalized;

            // The slider comes down as the wing (out of its bag) inflates, and stays down.
            if (_extend >= 1f)
            {
                _sliderDown = Mathf.Max(_sliderDown, _inflate);
            }

            var sliderAlong = Mathf.Lerp(Mathf.Max(cascadeLength - SliderTopGap, SliderRest), SliderRest, Mathf.SmoothStep(0f, 1f, _sliderDown));
            var sliderUp = drop <= 0f && sliderAlong > SliderRest + SliderBendAbove;
            var index = 0;
            for (var side = 0; side < 2; side++)
            {
                var sign = side == 0 ? -1f : 1f;
                var shoulder = RiserAttach(side == 0 ? LeftShoulderPosition(harness) : RightShoulderPosition(harness));

                // The risers point toward the middle of their side's lines.
                var middle = Vector3.zero;
                foreach (var station in LineSpanStations)
                {
                    middle += WingAnchor(0.5f + sign * station * 0.5f, 0.5f);
                }

                middle /= LineSpanStations.Length;
                var riserDirection = (middle - shoulder).normalized;
                var front = shoulder + riserDirection * riserLength + heading * RiserSpread;
                var rear = shoulder + riserDirection * riserLength - heading * RiserSpread;

                // Once the canopy is down, the risers hang from the shoulders to the ground
                // just behind the drood, so the lines lie flat from there.
                if (drop > 0f)
                {
                    var behind = shoulder - up * (Vector3.Dot(shoulder, up) - _groundUnderDrood - 0.02f) - transform.forward * 0.3f;
                    front = Vector3.Lerp(front, behind + transform.right * (sign * 0.05f), drop);
                    rear = Vector3.Lerp(rear, behind + transform.right * (sign * 0.1f), drop);
                }

                _riserTops[side * 2] = transform.InverseTransformPoint(front);
                _riserTops[side * 2 + 1] = transform.InverseTransformPoint(rear);

                // The slider's corners on this side: up the front (A/B) and rear (C/D) line
                // groups from the risers' tops.
                var frontCorner = front + (WingAnchor(0.5f + sign * 0.25f, 0.3f) - front).normalized * sliderAlong;
                var rearCorner = rear + (WingAnchor(0.5f + sign * 0.25f, 0.8f) - rear).normalized * sliderAlong;
                _sliderCorners[side == 0 ? 0 : 1] = frontCorner;
                _sliderCorners[side == 0 ? 3 : 2] = rearCorner;

                // In flight the lines stay taut (they make the wing's shape: pulls, turns, the front
                // risers); only a half collapsing in a stall lets its lines go slack, and a
                // deflated wing or the canopy coming down after landing.
                var slackAllowed = drop > 0f || _starved ? 1f : Mathf.Clamp01(StallCollapseOf((int)sign) / StallDeepCollapse);

                for (var row = 0; row < LineChordStations.Length; row++)
                {
                    var riser = row < 2 ? front : rear;
                    foreach (var station in LineSpanStations)
                    {
                        var anchor = WingAnchor(0.5f + sign * station * 0.5f, LineChordStations[row]);
                        if (sliderUp)
                        {
                            // Through the slider's corner ring.
                            PlaceBentLine(_lines[index++], anchor, row < 2 ? frontCorner : rearCorner, riser, camera);
                        }
                        else
                        {
                            PlaceLine(_lines[index++], anchor, riser, cascadeLength, up, drop, camera, slackAllowed);
                        }
                    }
                }

                // The steering line: from the trailing edge's attachment points to the cascade,
                // from there down the rear riser to the toggle in the hand (to the riser's top when
                // the hand isn't holding it).
                var attached = Vector3.zero;
                foreach (var station in SteeringAttachStations)
                {
                    attached += WingAnchor(0.5f + sign * station * 0.5f, 1f);
                }

                attached /= SteeringAttachStations.Length;
                var toRear = rear - attached;
                var cascade = attached + toRear.normalized * Mathf.Min(SteeringCascadeBelow, toRear.magnitude * 0.5f);
                foreach (var station in SteeringAttachStations)
                {
                    var point = WingAnchor(0.5f + sign * station * 0.5f, 1f);
                    PlaceLine(_lines[index++], point, cascade, Vector3.Distance(point, cascade), up, drop, camera, slackAllowed);
                }

                if (_hangingPose != null && drop <= 0f && !HoldsFrontRisers && _hangingPose.TryGetFist(sign, out var fist))
                {
                    // Down to the guide ring at the top of the rear riser, then to the toggle.
                    PlaceBentLine(_lines[index++], cascade, rear, fist, camera);
                }
                else
                {
                    PlaceLine(_lines[index++], cascade, rear, Mathf.Max(cascadeLength - SteeringCascadeBelow, 0.5f), up, drop, camera, slackAllowed);
                }

                // The risers themselves (straps, see PlaceRiser), flat side fore and aft.
                // Held (W), the front riser runs from its top down through the fist to the harness.
                var faceNormal = drop > 0f ? transform.forward : heading;
                Vector3? frontBend = null;
                if (_hangingPose != null && drop <= 0f && HoldsFrontRisers && _hangingPose.TryGetFist(sign, out var frontFist))
                {
                    frontBend = frontFist;
                }

                PlaceRiser(side * 2, front, frontBend, shoulder, faceNormal);
                PlaceRiser(side * 2 + 1, rear, null, shoulder, faceNormal);
            }

            var extrasStart = timed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            PlaceRamAirExtras(drop, cascadeLength);
            if (timed)
            {
                if (_foldSlot < 0)
                {
                    _foldSlot = DroodProfiler.PartSlot("(visuals) wing fold and shape");
                    _linesSlot = DroodProfiler.PartSlot("(visuals) lines and risers");
                    _extrasSlot = DroodProfiler.PartSlot("(visuals) slider, bag, pilot chute");
                }

                DroodProfiler.Add(_foldSlot, linesStart - shapeStart);
                DroodProfiler.Add(_linesSlot, extrasStart - linesStart);
                DroodProfiler.Add(_extrasSlot, System.Diagnostics.Stopwatch.GetTimestamp() - extrasStart);
            }
        }

        private static float Lowest(Vector3[] vertices)
        {
            var lowest = 0f;
            foreach (var v in vertices)
            {
                lowest = Mathf.Min(lowest, v.y);
            }

            return lowest;
        }

        /// <summary>A taut line from <paramref name="from"/> through <paramref name="ring"/> to <paramref name="to"/>.</summary>
        private void PlaceBentLine(LineRenderer line, Vector3 from, Vector3 ring, Vector3 to, Camera camera)
        {
            if (line.positionCount != 3)
            {
                line.positionCount = 3;
            }

            line.SetPosition(0, from);
            line.SetPosition(1, ring);
            line.SetPosition(2, to);
            line.startWidth = PixelWidth(camera, from);
            line.endWidth = PixelWidth(camera, to);
        }

        /// <summary>
        /// A shroud line of <paramref name="length"/>; only <paramref name="slackAllowed"/> (0..1)
        /// of any slack shows (0: taut, straight).
        /// </summary>
        private void PlaceLine(LineRenderer line, Vector3 from, Vector3 to, float length, Vector3 up, float drop, Camera camera, float slackAllowed)
        {
            var distance = Vector3.Distance(from, to);
            SetLinePoints(line, from, to, Mathf.Lerp(Mathf.Min(length, distance), length, slackAllowed), up, drop);
            line.startWidth = PixelWidth(camera, from);
            line.endWidth = PixelWidth(camera, to);
        }

        /// <summary>A point on the wing's underside in world space, as the wing is placed now.</summary>
        private Vector3 WingAnchor(float span, float chord) => WingWorldPoint(span, UndersideLoop(chord));

        /// <summary>
        /// Where the ram-air hand's riser goes, for <see cref="TryGetRiserGrip"/>: the top of the
        /// rear riser (the steering toggle is on it), or of the front riser while the front
        /// risers are pulled. From the lines as placed last frame.
        /// </summary>
        private Vector3 RamAirGripAnchor(float side)
        {
            var front = _frontPull > Mathf.Max(_leftPull, _rightPull);
            return transform.TransformPoint(_riserTops[(side > 0f ? 2 : 0) + (front ? 0 : 1)]);
        }
        /// <summary>
        /// The slider, the pilot chute with its bridle and the deployment bag, under one object
        /// (shown and hidden with the canopy). The slider takes the drood's paint like the wing,
        /// copying a round-canopy vertex's paint; the pilot chute is a small copy of the round
        /// canopy, all in one paint slot (<see cref="PilotChutePaintSlot"/>); the bag is a pouch
        /// (<see cref="BuildBagMesh"/>).
        /// </summary>
        private void BuildRamAirExtras(GameObject prefab, Mesh dome)
        {
            _ramAirExtras = new GameObject("DroodRamAirExtras");
            _ramAirExtras.transform.SetParent(transform, false);

            // Slider: a double-sided square, its corners set every frame (PlaceRamAirExtras).
            var paint = new VertexCopier(dome);
            _sliderMesh = new Mesh { name = "DroodSlider" };
            var corners = new Vector3[8];
            for (var i = 0; i < corners.Length; i++)
            {
                paint.Copy(0);
            }

            _sliderMesh.vertices = corners;
            paint.Apply(_sliderMesh);
            _sliderMesh.subMeshCount = dome.subMeshCount;
            _sliderMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6 }, 0);
            for (var sub = 1; sub < dome.subMeshCount; sub++)
            {
                _sliderMesh.SetTriangles(new int[0], sub);
            }

            _slider = new GameObject("DroodSlider").transform;
            _slider.SetParent(_ramAirExtras.transform, false);
            _slider.gameObject.AddComponent<MeshFilter>().sharedMesh = _sliderMesh;
            var sliderRenderer = _slider.gameObject.AddComponent<MeshRenderer>();
            sliderRenderer.sharedMaterials = _canopyRenderer != null ? _canopyRenderer.sharedMaterials : prefab.GetComponentInChildren<Renderer>().sharedMaterials;
            AddExtraFabric(sliderRenderer);

            // The game may have swapped in its own copy of the mesh (see BuildVisuals); the
            // slider's corners are moved on the one that is shown.
            var sliderFilter = _slider.GetComponent<MeshFilter>();
            if (sliderFilter.sharedMesh != _sliderMesh && sliderFilter.sharedMesh != null)
            {
                Destroy(_sliderMesh);
                _sliderMesh = sliderFilter.sharedMesh;
            }

            // Pilot chute: the round canopy's mesh with every vertex in one paint slot (a
            // vertex's slot is uv0.w modulo 10; the tens digit is the subpart style).
            _pilotChuteMesh = Instantiate(dome);
            _pilotChuteMesh.name = "DroodPilotChute";
            var uvs = new List<Vector4>();
            _pilotChuteMesh.GetUVs(0, uvs);
            var slotW = -1f;
            foreach (var uv in uvs)
            {
                if (Mathf.FloorToInt(uv.w) % 10 == PilotChutePaintSlot)
                {
                    slotW = uv.w;
                    break;
                }
            }

            if (slotW >= 0f)
            {
                for (var i = 0; i < uvs.Count; i++)
                {
                    var uv = uvs[i];
                    uv.w = slotW;
                    uvs[i] = uv;
                }

                _pilotChuteMesh.SetUVs(0, uvs);
            }

            _pilotChute = new GameObject("DroodPilotChute").transform;
            _pilotChute.SetParent(_ramAirExtras.transform, false);
            _pilotChute.gameObject.AddComponent<MeshFilter>().sharedMesh = _pilotChuteMesh;
            var pilotRenderer = _pilotChute.gameObject.AddComponent<MeshRenderer>();
            pilotRenderer.sharedMaterials = sliderRenderer.sharedMaterials;
            AddExtraFabric(pilotRenderer);

            // Deployment bag: a soft pouch in Trim 2 (BuildBagMesh).
            _bagMesh = BuildBagMesh(dome);
            _bag = new GameObject("DroodDeploymentBag").transform;
            _bag.SetParent(_ramAirExtras.transform, false);
            _bag.gameObject.AddComponent<MeshFilter>().sharedMesh = _bagMesh;
            var bagRenderer = _bag.gameObject.AddComponent<MeshRenderer>();
            bagRenderer.sharedMaterials = sliderRenderer.sharedMaterials;
            AddExtraFabric(bagRenderer);

            _bridle = CreateLine(_lineMaterial);
            _bridle.transform.SetParent(_ramAirExtras.transform, false);
            _pilotCollapse = 0f;
            _pilotTrail = Vector3.zero;

            BuildRiserStraps();
            SetLayerRecursive(_ramAirExtras, gameObject.layer);
        }

        private void AddExtraFabric(Renderer renderer)
        {
            // Like the canopy: part paint, not merged into the part's meshes, no drag.
            _part.PartMaterialScript?.AddRenderer(renderer, true, true, null, false);
            _extraFabricRenderers.Add(renderer);
        }

        /// <summary>
        /// Places the slider at its corners on the line groups (see SliderRest); hidden once the
        /// canopy is down.
        /// </summary>
        private void PlaceRamAirExtras(float drop, float cascadeLength)
        {
            if (_ramAirExtras == null)
            {
                return;
            }

            PlacePilotChute(drop);
            var flying = drop < 0.5f;
            // The slider is folded with the wing in the bag: shown once the wing is out.
            flying &= _bagRelease >= 1f;
            _slider.gameObject.SetActive(flying);
            if (!flying)
            {
                return;
            }

            // Slider: its corners on the four line groups, as placed with the lines.
            var corners = _sliderCorners;

            var normal = Vector3.Cross(corners[1] - corners[0], corners[3] - corners[0]).normalized * (ShellGap * 0.5f);
            for (var i = 0; i < 4; i++)
            {
                _sliderVertices[i] = _slider.InverseTransformPoint(corners[i] + normal);
                _sliderVertices[i + 4] = _slider.InverseTransformPoint(corners[i] - normal);
            }

            _sliderMesh.vertices = _sliderVertices;
            _sliderMesh.RecalculateNormals();
            _sliderMesh.RecalculateBounds();
        }

        /// <summary>
        /// Places the pilot chute, its bridle and the deployment bag through the deployment: held
        /// in the hand, then paying out from the container, pulling the bag, then from the top of
        /// the wing. In the air it trails in the wing's wake, sagging by the gravity it feels
        /// against its drag, swinging with a short lag and fluttering; open until the wing is,
        /// then collapsed by its kill line. Once the canopy is down it lies on the ground behind
        /// the wing.
        /// </summary>
        private void PlacePilotChute(float drop)
        {
            if (_pilotChute == null || _bridle == null)
            {
                return;
            }

            // Open from deployment (Deploy resets it) until the wing is; collapsed it stays so,
            // also if the wing reinflates after a landing (the kill line holds it in).
            if (_inflate >= 1f)
            {
                _pilotCollapse = Mathf.MoveTowards(_pilotCollapse, 1f, Time.deltaTime / PilotCollapseSeconds);
            }

            var collapsed = Mathf.SmoothStep(0f, 1f, _pilotCollapse);

            // In the air: trailing the relative wind, turned PilotTrailBelowWind toward the drood
            // (the wing's downwash behind it), sagging by its weight against its drag.
            var airspeed = _visualAirVelocity.magnitude;
            var dragArea = Mathf.Lerp(PilotOpenDragArea, PilotCollapsedDragArea, collapsed);
            var dragAccel = 0.5f * _visualDensity * airspeed * airspeed * dragArea / PilotChuteMass;
            var airflow = airspeed > 0.1f ? -_visualAirVelocity / airspeed : -_canopyRoot.forward;
            // Only once the wing has formed, and fading out as the wind comes straight up the lines
            // (falling: there is no "toward the drood" side to turn to).
            var turn = PilotTrailBelowWind * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.8f, _inflate))
                * Mathf.InverseLerp(0f, PilotTrailBelowWind, 180f - Vector3.Angle(airflow, -_canopyDirection));
            var wake = Vector3.RotateTowards(airflow, -_canopyDirection, turn * Mathf.Deg2Rad, 0f);
            // Sagging by the gravity the canopy feels (none in free fall or a push-over); where it
            // would point into the wing it drapes over the trailing edge (below).
            var target = wake * dragAccel + (_visualGravity - _visualAcceleration);
            target = target.sqrMagnitude > 1e-6f ? target.normalized : wake;
            if (TouchedDown)
            {
                // Down: straight back toward where it lies behind the wing (the stop's jolt and the
                // lost airflow would first fling it forward).
                var groundUp = _landedNormal.sqrMagnitude > 0.5f ? _landedNormal : transform.up;
                var back = Vector3.ProjectOnPlane(-_canopyRoot.forward, groundUp);
                target = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.ProjectOnPlane(-transform.forward, groundUp).normalized;
            }
            _pilotTrail = _pilotTrail.sqrMagnitude < 0.5f
                ? target
                : Vector3.Slerp(_pilotTrail, target, 1f - Mathf.Exp(-Time.deltaTime / PilotSwingSeconds)).normalized;

            // Where the bridle starts: the container while the pilot chute is thrown, then the top
            // of the bag at the end of the lines, then (as the wing leaves the bag) the wing.
            var outOfBag = _bagRelease > 0.5f;
            var bagCentre = _canopyRoot.position + _canopyDirection * BagCentreUpLines;
            var bagTop = bagCentre + _canopyDirection * (BagSize.z * 0.5f);
            var top = _pilotOut < 1f
                ? ContainerPosition()
                : Vector3.Lerp(bagTop, WingWorldPoint(0.5f, TopLoop(BridleAttachChord)), _bagRelease);

            // In the hand (the throw): folded, the bridle from the container to the hand.
            _rightHand = _rightHand != null ? _rightHand : FindChild(transform, "RightHand");
            if (_handThrow < 1f && _rightHand != null)
            {
                var held = _rightHand.position;
                _pilotReleaseLocal = transform.InverseTransformPoint(held);
                PlaceHeldPilotChute(held, top);
                return;
            }
            var trail = _pilotTrail;

            // Flutter: smooth noise, mostly about the wing's span (up and down), a little about
            // the axis square to it (side to side).
            var flutter = Mathf.Clamp(airspeed / (RamAirTrimSpeed * WingSpeedScale), 0f, 1.5f) * Mathf.Lerp(0.5f, 1f, collapsed);
            var phase = Time.time * PilotFlutterRate;
            var upDown = (Mathf.PerlinNoise(phase, 5.3f) * 2f - 1f) * PilotFlutterUpDown * flutter;
            var sideways = (Mathf.PerlinNoise(phase * 0.8f, 61.7f) * 2f - 1f) * PilotFlutterSideways * flutter;
            var span = Vector3.ProjectOnPlane(_canopyRoot.right, trail);
            if (span.sqrMagnitude > 1e-4f)
            {
                span.Normalize();
                trail = Quaternion.AngleAxis(sideways, Vector3.Cross(trail, span)) * (Quaternion.AngleAxis(upDown, span) * trail);
            }

            // Pointing into the wing (slow: its weight wins), the bridle runs back over the top
            // to the trailing edge and the rest hangs from there; otherwise it runs straight.
            var draped = outOfBag && _pilotOut >= 1f && drop <= 0f && Vector3.Dot(trail, _canopyRoot.up) < BridleMinAboveWing;
            var edge = top;
            Vector3 mouth;
            if (draped)
            {
                edge = WingWorldPoint(0.5f, 0.5f);
                var hang = Vector3.Dot(trail, _canopyRoot.forward) > 0f ? Vector3.ProjectOnPlane(trail, _canopyRoot.forward) : trail;
                hang = hang.sqrMagnitude > 1e-6f ? hang.normalized : -_canopyRoot.up;
                mouth = edge + hang * Mathf.Max(BridleLength - Vector3.Distance(top, edge), 0.1f);
                trail = hang;
            }
            else
            {
                trail = outOfBag ? AboveWing(trail) : trail;
                mouth = top + trail * (BridleLength * Mathf.Max(_pilotOut, 0.05f));
            }

            // Paying out from where the hand let it go.
            if (_pilotOut < 1f)
            {
                mouth = Vector3.Lerp(transform.TransformPoint(_pilotReleaseLocal), mouth, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pilotOut * 2f)));
            }

            // On the ground: behind the wing (its nose points to the drood), on the ground.
            if (drop > 0f && _visualsBuilt)
            {
                var normal = _landedNormal.sqrMagnitude > 0.5f ? _landedNormal : transform.up;
                var away = Vector3.ProjectOnPlane(-_canopyRoot.forward, normal);
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.ProjectOnPlane(-transform.forward, normal).normalized;
                var lying = top + away * BridleLength;
                lying -= normal * (Vector3.Dot(lying - _landedGround, normal) - 0.05f);
                var t = Mathf.SmoothStep(0f, 1f, drop);
                mouth = Vector3.Lerp(mouth, lying, t);
                trail = Vector3.Slerp(trail, away, t).normalized;
                collapsed = Mathf.Max(collapsed, t);
            }

            _pilotChute.position = mouth;
            _pilotChute.rotation = Quaternion.FromToRotation(AxisVector(_meshHeightAxis) * _meshApexSign, trail);

            // Thrown from the container it opens as it catches the air.
            var size = PilotChuteRadius / _meshRadius * Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, _pilotOut));

            // The bag: in the container during the throw, then at the end of the lines, then
            // (empty) on the bridle.
            if (_bag != null)
            {
                // Shown from the end of the throw; as the wing spreads out of it, it slides up the
                // bridle and shrinks away, BagGoneFaster times quicker than the wing comes out.
                var gone = Mathf.Clamp01(_bagRelease * BagGoneFaster);
                _bag.gameObject.SetActive(_pilotOut >= 1f && gone < 1f);
                _bag.localScale = Vector3.one * (1f - gone);
                var onBridle = Vector3.Lerp(top, mouth, BagOnBridle);
                var along = gone > 0f ? Vector3.Slerp(_canopyDirection, trail, gone) : _canopyDirection;
                _bag.position = Vector3.Lerp(bagCentre, onBridle, gone);
                _bag.rotation = Quaternion.LookRotation(along, Vector3.ProjectOnPlane(transform.forward, along).sqrMagnitude > 1e-4f ? transform.forward : transform.up);
            }

            var scale = Vector3.one * (size * Mathf.Lerp(1f, PilotCollapsedWidth, collapsed));
            scale[_meshHeightAxis] = size * Mathf.Lerp(1f, PilotCollapsedLength, collapsed);
            _pilotChute.localScale = scale;

            var camera = GetGameCamera();
            CacheWidthCamera(camera);
            var points = draped ? BridleDrapePieces + 2 : 2;
            if (_bridle.positionCount != points)
            {
                _bridle.positionCount = points;
            }

            _bridle.SetPosition(0, top);
            if (draped)
            {
                // Over the top skin, from where it leaves it to the trailing edge.
                for (var i = 1; i <= BridleDrapePieces; i++)
                {
                    var loop = TopLoop(Mathf.Lerp(BridleAttachChord, 1f, (float)i / BridleDrapePieces));
                    _bridle.SetPosition(i, WingWorldPoint(0.5f, loop) + _canopyRoot.up * BridleOnSkin);
                }
            }

            _bridle.SetPosition(points - 1, mouth);
            _bridle.startWidth = PixelWidth(camera, top);
            _bridle.endWidth = PixelWidth(camera, mouth);
        }

        /// <summary>
        /// The hand throw in progress (0..1: reaching back until HandReachFraction, then
        /// sweeping out, letting go at 1), or -1 when the hand isn't throwing.
        /// </summary>
        internal float HandThrow => _state == State.Deployed && _handThrow < 1f ? _handThrow : -1f;

        /// <summary>The pilot chute pouch at the bottom of the container, where the hand reaches.</summary>
        internal Vector3 PilotPouch => ContainerPosition() + transform.right * PouchRight - transform.up * PouchBelow;

        /// <summary>The middle of the parachute container on the drood's back.</summary>
        private Vector3 ContainerPosition() =>
            HarnessPosition - transform.up * ContainerBelowShoulders - transform.forward * ContainerBehind;

        /// <summary>
        /// The deployment bag: a soft, rounded box <see cref="BagSize"/> big (across, deep, long
        /// along local Z, the lines' end at -Z) with gently bulging sides, like stuffed fabric,
        /// and a closing flap folded over its bottom where the lines come out. Every vertex in
        /// paint slot <see cref="BagPaintSlot"/> (copied from a round-canopy vertex of that slot).
        /// </summary>
        private Mesh BuildBagMesh(Mesh dome)
        {
            var uvs = new List<Vector4>();
            dome.GetUVs(0, uvs);
            var slotVertex = 0;
            for (var i = 0; i < uvs.Count; i++)
            {
                if (Mathf.FloorToInt(uvs[i].w) % 10 == BagPaintSlot)
                {
                    slotVertex = i;
                    break;
                }
            }

            var paint = new VertexCopier(dome);
            var positions = new List<Vector3>();
            var triangles = new List<int>();
            var half = BagSize * 0.5f;

            // The body: a superquadric (BagRoundness: 1 an ellipsoid, toward 0 a box).
            float Power(float value) => Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), BagRoundness);
            const int rings = 12;
            const int segments = 24;
            for (var ring = 0; ring <= rings; ring++)
            {
                var latitude = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, ring / (float)rings);
                for (var segment = 0; segment <= segments; segment++)
                {
                    var longitude = Mathf.Lerp(-Mathf.PI, Mathf.PI, segment / (float)segments);
                    var around = Power(Mathf.Cos(latitude));
                    positions.Add(new Vector3(
                        half.x * around * Power(Mathf.Cos(longitude)),
                        half.y * around * Power(Mathf.Sin(longitude)),
                        half.z * Power(Mathf.Sin(latitude))));
                    paint.Copy(slotVertex);
                }
            }

            var row = segments + 1;
            for (var ring = 0; ring < rings; ring++)
            {
                for (var segment = 0; segment < segments; segment++)
                {
                    var v = ring * row + segment;
                    var middle = (positions[v] + positions[v + 1] + positions[v + row] + positions[v + row + 1]) * 0.25f;
                    AddQuad(triangles, positions, v, v + 1, v + row, v + row + 1, middle);
                }
            }

            // The closing flap: a thin panel folded over the bottom, a little narrower than the
            // bag and tilted, so its edge throws a crease.
            var flapCentre = new Vector3(0f, 0f, -half.z);
            var flapTurn = Quaternion.AngleAxis(BagFlapTilt, Vector3.right);
            var flapHalf = new Vector3(half.x * BagFlapWidth, half.y * BagFlapDepth, BagFlapThickness * 0.5f);
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                foreach (var side in new[] { 1f, -1f })
                {
                    var normal = axis * side;
                    var u = axis == Vector3.forward ? Vector3.right : Vector3.forward;
                    var w = Vector3.Cross(normal, u);
                    var first = positions.Count;
                    for (var corner = 0; corner < 4; corner++)
                    {
                        var su = corner == 0 || corner == 3 ? -1f : 1f;
                        var sw = corner < 2 ? -1f : 1f;
                        var local = Vector3.Scale(normal + u * su + w * sw, flapHalf);
                        positions.Add(flapCentre + flapTurn * local);
                        paint.Copy(slotVertex);
                    }

                    AddQuad(triangles, positions, first, first + 1, first + 3, first + 2, flapTurn * normal);
                }
            }

            var mesh = new Mesh { name = "DroodDeploymentBag" };
            mesh.SetVertices(positions);
            paint.Apply(mesh);
            mesh.subMeshCount = dome.subMeshCount;
            mesh.SetTriangles(triangles, 0);
            for (var sub = 1; sub < dome.subMeshCount; sub++)
            {
                mesh.SetTriangles(new int[0], sub);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The pilot chute held folded in the hand, the bridle from the container to it.</summary>
        private void PlaceHeldPilotChute(Vector3 held, Vector3 container)
        {
            if (_bag != null)
            {
                _bag.gameObject.SetActive(false);
            }

            var along = held - container;
            var direction = along.sqrMagnitude > 1e-6f ? along.normalized : -transform.forward;
            _pilotChute.position = held;
            _pilotChute.rotation = Quaternion.FromToRotation(AxisVector(_meshHeightAxis) * _meshApexSign, direction);
            var size = PilotChuteRadius / _meshRadius * PilotHeldSize;
            var scale = Vector3.one * (size * PilotCollapsedWidth);
            scale[_meshHeightAxis] = size * PilotCollapsedLength;
            _pilotChute.localScale = scale;

            var camera = GetGameCamera();
            CacheWidthCamera(camera);
            if (_bridle.positionCount != 2)
            {
                _bridle.positionCount = 2;
            }

            _bridle.SetPosition(0, container);
            _bridle.SetPosition(1, held);
            _bridle.startWidth = PixelWidth(camera, container);
            _bridle.endWidth = PixelWidth(camera, held);
        }

        /// <summary><paramref name="direction"/>, turned out of the wing if it points into it (see BridleMinAboveWing).</summary>
        private Vector3 AboveWing(Vector3 direction)
        {
            var wingUp = _canopyRoot.up;
            var into = Vector3.Dot(direction, wingUp);
            if (into >= BridleMinAboveWing)
            {
                return direction;
            }

            var flat = Vector3.ProjectOnPlane(direction, wingUp);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : -_canopyRoot.forward;
            return (flat * Mathf.Sqrt(1f - BridleMinAboveWing * BridleMinAboveWing) + wingUp * BridleMinAboveWing).normalized;
        }

        /// <summary>A point on the wing (<see cref="WingPoint"/>) in world space, as the wing is placed now.</summary>
        private Vector3 WingWorldPoint(float span, float loop)
        {
            var local = WingPoint(span, loop);
            if (_wingShaped)
            {
                // Shaped by the lines or a stall: where that has moved it (DroodParachute.Flutter.cs).
                var point = ShapeFixedOf(local, span);
                local = ShapedPoint(local, in point, HalfShapeOf(true), HalfShapeOf(false));
            }

            if (_wingLumps > 0f)
            {
                // On the ground: where the landed shape has moved that point of the fabric.
                local = Vector3.Lerp(local, LandedPoint(local), _wingLumps);
            }

            if (_canopyMesh != null)
            {
                local = _canopyMesh.localPosition + _canopyMesh.localRotation * Vector3.Scale(_canopyMesh.localScale, local);
            }

            return _canopyRoot.position + _canopyRoot.rotation * local;
        }
    }
}
