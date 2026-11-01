using UnityEngine;

namespace Drood2
{
    // Lines: shroud line shapes (taut, sagging, lying on the ground) and their on-screen width.
    public partial class DroodParachute
    {
        // One line's points, written in one go (SetPositions) instead of one call per point.
        private readonly Vector3[] _linePoints = new Vector3[LinePoints];

        /// <summary>
        /// Horizontal reach a of the rising part of a lying line, so that
        /// (reach − a) on the ground + the arc length of y = h·(x/a)² over [0, a] equals the
        /// line length. The total shrinks as a grows (a flatter curve uses less rope), so it's
        /// found by bisection. Too much rope for this shape (the drood stands close to the
        /// canopy) gives the steepest curve; too little (at the end of the lines) gives a
        /// curve over the full distance, which is nearly the straight chord.
        /// </summary>
        private static float SolveCurveReach(float reach, float height, float lineLength)
        {
            const float minReach = 0.05f;
            if (reach <= minReach)
            {
                return minReach;
            }

            float Total(float a) => reach - a + ParabolaArc(a, height);

            var low = minReach;
            var high = reach;
            if (Total(low) <= lineLength)
            {
                return low;
            }

            if (Total(high) >= lineLength)
            {
                return high;
            }

            for (var i = 0; i < 20; i++)
            {
                var mid = (low + high) * 0.5f;
                if (Total(mid) > lineLength)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return (low + high) * 0.5f;
        }

        /// <summary>Exact arc length of y = h·(x/a)² for x in [0, a].</summary>
        private static float ParabolaArc(float a, float h)
        {
            if (h < 1e-4f)
            {
                return a;
            }

            var k = 2f * h / a;
            var root = Mathf.Sqrt(1f + k * k);
            return a * (root / 2f + Mathf.Log(k + root) / (2f * k));
        }

        /// <summary>
        /// Draws one line from the canopy rim to a shoulder, blending two shapes by how far
        /// the canopy has come down (<paramref name="down"/>):
        /// - Hanging: the chord with a parabolic sag matching its slack (taut in flight, so
        ///   nearly straight).
        /// - Lying: like a real slack rope, it lies along the ground from the canopy to a
        ///   touchdown point, then rises in a curve (y = h·(x/a)²) that leaves the ground
        ///   tangentially up to the shoulder. The curve's reach a is solved so the ground
        ///   part plus the curve's exact arc length equal the line length
        ///   (<see cref="SolveCurveReach"/>).
        /// The ground along the line is a straight slope between the ground under the canopy
        /// and under the drood (two raycasts, not one per point).
        /// </summary>
        private void SetLinePoints(LineRenderer line, Vector3 rim, Vector3 shoulder, float lineLength, Vector3 up, float down)
        {
            if (line.positionCount != LinePoints)
            {
                line.positionCount = LinePoints;
            }

            // Hanging shape.
            var span = Vector3.Distance(rim, shoulder);
            var slack = Mathf.Max(lineLength - span, 0f);
            var sag = span > 0.01f ? Mathf.Sqrt(3f * span * slack / 8f) : 0f;

            // Lying shape, in (horizontal distance from the rim, height) along the line.
            var groundAtRim = Vector3.Dot(_landedGround, up) + 0.02f;
            var groundAtDrood = _groundUnderDrood + 0.02f;
            var horizontal = Vector3.ProjectOnPlane(shoulder - rim, up);
            var reach = horizontal.magnitude;
            var direction = reach > 1e-3f ? horizontal / reach : Vector3.zero;
            var baseline = rim - up * Vector3.Dot(rim, up);
            var rise = Mathf.Max(Vector3.Dot(shoulder, up) - groundAtDrood, 0f);
            var curveReach = SolveCurveReach(reach, rise, lineLength);
            var touchdown = Mathf.Max(reach - curveReach, 0f);
            var camera = down > 0f ? _widthCamera : null;

            for (var j = 0; j < LinePoints; j++)
            {
                var t = j / (LinePoints - 1f);
                var hanging = Vector3.Lerp(rim, shoulder, t) - up * (sag * 4f * t * (1f - t));
                if (down <= 0f)
                {
                    _linePoints[j] = hanging;
                    continue;
                }

                // Ground points first, then the curve, denser toward the shoulder.
                float along;
                float height;
                if (j < GroundLinePoints)
                {
                    along = touchdown * j / GroundLinePoints;
                    height = Mathf.Lerp(groundAtRim, groundAtDrood, reach > 1e-3f ? along / reach : 0f);
                }
                else
                {
                    var u = (j - GroundLinePoints) / (HangingLinePoints - 1f);
                    along = touchdown + curveReach * u;
                    var ground = Mathf.Lerp(groundAtRim, groundAtDrood, reach > 1e-3f ? along / reach : 1f);
                    var top = Vector3.Dot(shoulder, up);
                    height = ground + (top - ground) * u * u;
                }

                var lying = baseline + direction * along + up * height;
                // The line faces the camera: lifted by half its width, so it doesn't dip into
                // the ground (from a distance it is several cm wide and would show in dashes).
                lying += up * (PixelWidth(camera, lying) * 0.5f);
                if (j == 0)
                {
                    lying = rim;
                }
                else if (j == LinePoints - 1)
                {
                    lying = shoulder;
                }

                _linePoints[j] = Vector3.Lerp(hanging, lying, down);
            }

            line.SetPositions(_linePoints);
        }

        // The camera the line widths are measured for, read once where the lines are placed
        // (CacheWidthCamera, after each GetGameCamera): a lying line sizes each of its points, and the camera's position,
        // field of view and pixel height are engine calls (~1,000 points a frame on the ground).
        private static Camera _widthCamera;
        private static Vector3 _widthCameraPosition;
        private static float _widthPerMetre;

        /// <summary>Reads <paramref name="camera"/> for this frame's line widths.</summary>
        private static void CacheWidthCamera(Camera camera)
        {
            _widthCamera = camera;
            if (camera != null)
            {
                _widthCameraPosition = camera.transform.position;
                _widthPerMetre = 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(camera.pixelHeight, 1);
            }
        }

        /// <summary>
        /// World-space line width that covers ~<see cref="ShroudLinePixels"/> pixels at this
        /// point, thinner further away (ShroudLineFarFraction of that from ShroudLineFadeEnd),
        /// so distant lines don't flicker as solid bars.
        /// </summary>
        private static float PixelWidth(Camera camera, Vector3 point)
        {
            if (camera == null)
            {
                return ShroudLineMinWidth;
            }

            if (!ReferenceEquals(camera, _widthCamera))
            {
                CacheWidthCamera(camera);
            }

            var distance = Vector3.Distance(_widthCameraPosition, point);
            var worldPerPixel = distance * _widthPerMetre;
            var pixels = ShroudLinePixels * Mathf.Lerp(1f, ShroudLineFarFraction, Mathf.InverseLerp(ShroudLineFadeStart, ShroudLineFadeEnd, distance));
            return Mathf.Max(worldPerPixel * pixels, ShroudLineMinWidth);
        }
    }
}
