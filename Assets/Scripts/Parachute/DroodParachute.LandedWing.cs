using UnityEngine;

namespace Drood2
{
    // The ram-air wing lying on the ground: an empty canopy, not a wing. Its cells have no air,
    // so the top skin lies on the underside; the edges (nose, trailing edge, tips) lie on the
    // ground; the span is bunched up; the fabric is folded in irregular ridges like a loose
    // accordion, lower toward the trailing edge; one tip is folded over onto the wing. A new
    // seed per landing, so no two look alike. Visual only.
    public partial class DroodParachute
    {
        // Heights and lengths in metres on the ground.
        // The span bunches to this fraction of its length.
        private const float LandedSpanShrink = 0.82f;
        // ... and moves along the span by up to this much (irregular bunching).
        private const float LandedBunch = 0.12f;
        // What is left of the airfoil's thickness (the skins on each other, a little air).
        private const float LandedThin = 0.3f;
        // Within this distance of an edge, the folds and lumps sink to the ground.
        private const float LandedEdgeWidth = 0.35f;
        // The accordion: a ridge every two cells, its height LandedFoldHeight times a random
        // LandedFoldMin..1 per ridge, lower toward the trailing edge (LandedFoldTrailing there);
        // the creases between ridges move along the span by up to LandedFoldJitter cells.
        private const float LandedFoldHeight = 0.22f;
        private const float LandedFoldMin = 0.15f;
        private const float LandedFoldTrailing = 0.35f;
        private const float LandedFoldJitter = 0.35f;
        // Soft lumps, and finer ripples across the chord.
        private const float LandedLumps = 0.05f;
        private const float LandedLumpSize = 1.1f;
        private const float LandedRipples = 0.025f;
        // The trailing edge waves fore and aft by up to this much.
        private const float LandedTrailingWave = 0.2f;
        // One tip folds over onto the wing: this much of the span, the crease this wide and
        // this high (above the ground).
        private const float LandedTipFold = 0.55f;
        private const float LandedTipFoldCrease = 0.12f;
        private const float LandedTipFoldHeight = 0.12f;

        // This landing's shape: noise offset, and which tip folds over (+1 right, -1 left).
        private float _landedSeed;
        private float _landedFoldSide = 1f;

        // The landed wing's vertices (reused), and this landing's shape per built vertex.
        private Vector3[] _lumpVertices;
        private Vector3[] _landedPoints;
        private bool _landedPointsReady;

        /// <summary>
        /// The landed wing's fabric, growing with <paramref name="amount"/> (the flattening):
        /// each vertex moves from the wing's shape to <see cref="LandedPoint"/>. A new shape
        /// per landing; only rewritten while the amount changes.
        /// </summary>
        private void ApplyGroundLumps(float amount)
        {
            if (_wingMesh == null || _wingRestVertices == null || Mathf.Abs(amount - _wingLumps) < 0.02f && (amount > 0f || _wingLumps == 0f))
            {
                return;
            }

            if (_lumpVertices == null || _lumpVertices.Length != _wingRestVertices.Length)
            {
                _lumpVertices = new Vector3[_wingRestVertices.Length];
                _landedPoints = new Vector3[_wingRestVertices.Length];
                _landedPointsReady = false;
            }

            if (_wingLumps <= 0f && amount > 0f)
            {
                _landedSeed = Random.Range(0f, 100f);
                _landedFoldSide = Random.value < 0.5f ? -1f : 1f;
                _landedPointsReady = false;
            }

            // This landing's shape, worked out once (it depends on the seed only).
            if (amount > 0f && !_landedPointsReady)
            {
                for (var i = 0; i < _landedPoints.Length; i++)
                {
                    _landedPoints[i] = LandedPoint(_wingRestVertices[i]);
                }

                _landedPointsReady = true;
            }

            _wingLumps = amount;
            var vertices = _lumpVertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                var v = _wingRestVertices[i];
                vertices[i] = amount > 0f ? Vector3.Lerp(v, _landedPoints[i], amount) : v;
            }

            _wingMesh.vertices = vertices;
            _wingLowest = Lowest(vertices);
            _wingMesh.RecalculateNormals();
            WeldSeamNormals(_wingMesh);
            _wingMesh.RecalculateBounds();
        }

        /// <summary>
        /// Where a point of the wing (<see cref="WingPoint"/> space) lies on the ground, in the
        /// same space, its height divided by LandedWingHeight (the wing is drawn squashed by
        /// that). The span arch is laid flat.
        /// </summary>
        private Vector3 LandedPoint(Vector3 v)
        {
            // The point's place on the wing: span (0..1 tip to tip), height above the
            // underside's arch, chord (0 nose, 1 trailing edge).
            var arc = WingArcDegrees * Mathf.Deg2Rad;
            var radius = WingSpan / arc;
            var span = Mathf.Atan2(v.x, v.y + radius) / arc + 0.5f;
            var height = new Vector2(v.x, v.y + radius).magnitude - radius;
            var localChord = WingChord * PlanformScale(Mathf.Clamp01(span));
            var leadingEdge = 0.5f * WingChord - (WingChord - localChord) * 0.8f;
            var chord = Mathf.Clamp01((leadingEdge - v.z) / localChord);
            var seed = _landedSeed;

            var halfSpan = WingSpan * LandedSpanShrink * 0.5f;
            var along = (span - 0.5f) * 2f * halfSpan + LandedBunch * Noise(span * 3f + seed, chord * 1.5f);
            var fromTip = halfSpan - along * _landedFoldSide;

            // The accordion: ridges two cells apart, creases at the ground in between.
            var phase = (span * WingCells + LandedFoldJitter * Noise(span * 2.3f + seed, 0.5f)) * 0.5f;
            var ridge = Mathf.Floor(phase);
            var tri = 1f - Mathf.Abs(2f * (phase - ridge) - 1f);
            var ridgeHeight = LandedFoldHeight * Mathf.Lerp(LandedFoldMin, 1f, Mathf.PerlinNoise(ridge * 0.73f + seed, 0.37f));
            var fold = ridgeHeight * Mathf.Sin(tri * Mathf.PI * 0.5f) * Mathf.Lerp(1f, LandedFoldTrailing, chord);
            // Flat where the folded tip lies.
            fold *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LandedTipFold, LandedTipFold * 2.2f, fromTip));

            var lumps = LandedLumps * Noise(along / LandedLumpSize + seed, v.z / LandedLumpSize + 7.7f)
                + LandedRipples * Noise(along / 0.5f + seed, chord * 4f + 3.3f);
            var toEdge = Mathf.Min(Mathf.Min(chord, 1f - chord) * localChord, Mathf.Min(span, 1f - span) * WingSpan);
            var edge = Mathf.SmoothStep(0f, 1f, toEdge / LandedEdgeWidth);
            var y = Mathf.Max(edge * (fold + lumps), 0f) + LandedThin * Mathf.Max(height, 0f);
            var z = v.z + LandedTrailingWave * chord * chord * Noise(along * 0.9f + seed, 0.3f);

            // The folded tip: turned over the crease (a rotation, so the fabric's faces keep
            // their sides), its underside up.
            var flip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LandedTipFold + LandedTipFoldCrease, LandedTipFold - LandedTipFoldCrease, fromTip));
            if (flip > 0f)
            {
                var crease = _landedFoldSide * (halfSpan - LandedTipFold);
                var angle = Mathf.PI * flip * _landedFoldSide;
                var cos = Mathf.Cos(angle);
                var sin = Mathf.Sin(angle);
                var x = along - crease;
                var up = y - LandedTipFoldHeight;
                along = crease + x * cos - up * sin;
                y = LandedTipFoldHeight + x * sin + up * cos;
            }

            return new Vector3(along, y / LandedWingHeight, z);
        }

        /// <summary>Perlin noise from -1 to 1.</summary>
        private static float Noise(float x, float y) => Mathf.PerlinNoise(x, y) * 2f - 1f;
    }
}
