using UnityEngine;

namespace Drood2
{
    // Ram-air risers: flat black webbing straps (1" wide), from the harness on the shoulder up
    // to where the line groups start. Visual only; placed with the lines (PlaceRamAirVisuals).
    public partial class DroodParachute
    {
        // Strap size (m): the width faces fore and aft (as on a real rig, the toggles sit on
        // the back of the rear risers), the thickness is the webbing's.
        private const float RiserWidth = 0.025f;
        private const float RiserThickness = 0.004f;
        // The straps run on this far (m) past the harness point, into the shoulder pad, so no
        // gap shows between them and the drood in any pose: on along the strap when it comes
        // down to the shoulder, straight down into the pad when it leaves it downward (the
        // canopy on the ground; on along it, the end stuck up out of the shoulder).
        private const float RiserTuck = 0.05f;
        private static readonly Color RiserColor = new Color(0.035f, 0.035f, 0.04f);

        // Two segments per riser (front left, rear left, front right, rear right): the second
        // only while the riser bends through the fist (front risers held).
        private const int RiserSegments = 2;
        private Transform[] _riserStraps = new Transform[0];
        private Mesh _strapMesh;
        private Material _strapMaterial;

        /// <summary>
        /// The four riser straps, under the ram-air extras (shown and destroyed with them).
        /// </summary>
        private void BuildRiserStraps()
        {
            _strapMesh = BuildStrapMesh();
            var shader = Shader.Find("Standard");
            _strapMaterial = shader != null ? new Material(shader) { name = "Drood 2.0 risers", color = RiserColor } : _lineMaterial;
            if (shader != null)
            {
                _strapMaterial.SetFloat("_Glossiness", 0.3f);
                _strapMaterial.SetFloat("_Metallic", 0f);
                _strapMaterial.renderQueue = DroodDropZoneRings.AfterQueue;
            }

            _riserStraps = new Transform[4 * RiserSegments];
            for (var i = 0; i < _riserStraps.Length; i++)
            {
                var strap = new GameObject("DroodRiser").transform;
                strap.SetParent(_ramAirExtras.transform, false);
                strap.gameObject.AddComponent<MeshFilter>().sharedMesh = _strapMesh;
                var renderer = strap.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _strapMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _riserStraps[i] = strap;
            }
        }

        private void DestroyRiserStraps()
        {
            if (_strapMesh != null)
            {
                Destroy(_strapMesh);
            }

            if (_strapMaterial != null && _strapMaterial != _lineMaterial)
            {
                Destroy(_strapMaterial);
            }
        }

        /// <summary>
        /// Riser <paramref name="riser"/> (0-3) from <paramref name="top"/> to the harness at
        /// <paramref name="bottom"/>, through <paramref name="bend"/> when given; its flat side
        /// faces along <paramref name="faceNormal"/>.
        /// </summary>
        private void PlaceRiser(int riser, Vector3 top, Vector3? bend, Vector3 bottom, Vector3 faceNormal)
        {
            if (_riserStraps.Length == 0)
            {
                return;
            }

            var first = _riserStraps[riser * RiserSegments];
            var second = _riserStraps[riser * RiserSegments + 1];
            if (bend.HasValue)
            {
                PlaceStrap(first, top, bend.Value, faceNormal);
                PlaceStrap(second, bend.Value, Tucked(bend.Value, bottom), faceNormal);
            }
            else
            {
                PlaceStrap(first, top, Tucked(top, bottom), faceNormal);
            }

            second.gameObject.SetActive(bend.HasValue);
        }

        /// <summary>
        /// <paramref name="bottom"/> moved RiserTuck into the shoulder (see RiserTuck), coming
        /// from <paramref name="from"/>.
        /// </summary>
        private Vector3 Tucked(Vector3 from, Vector3 bottom)
        {
            var along = bottom - from;
            if (along.sqrMagnitude < 1e-6f)
            {
                return bottom;
            }

            var down = -transform.up;
            return bottom + (Vector3.Dot(along, down) > 0f ? along.normalized : down) * RiserTuck;
        }

        private static void PlaceStrap(Transform strap, Vector3 from, Vector3 to, Vector3 faceNormal)
        {
            var along = to - from;
            var length = along.magnitude;
            strap.gameObject.SetActive(length > 1e-3f);
            if (length <= 1e-3f)
            {
                return;
            }

            var normal = Vector3.ProjectOnPlane(faceNormal, along);
            strap.position = from;
            strap.rotation = normal.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(along, normal) : Quaternion.LookRotation(along);
            strap.localScale = new Vector3(RiserWidth, RiserThickness, length);
        }

        /// <summary>
        /// A box, x (width) and y (thickness) centred, z (length) from 0 to 1; flat-shaded.
        /// </summary>
        private static Mesh BuildStrapMesh()
        {
            var vertices = new Vector3[24];
            var normals = new Vector3[24];
            var triangles = new int[36];
            var faces = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (var f = 0; f < faces.Length; f++)
            {
                var n = faces[f];
                var u = f < 4 ? Vector3.forward : Vector3.right;
                var v = Vector3.Cross(n, u);
                for (var c = 0; c < 4; c++)
                {
                    var su = c == 0 || c == 3 ? -0.5f : 0.5f;
                    var sv = c < 2 ? -0.5f : 0.5f;
                    var p = n * 0.5f + u * su + v * sv;
                    vertices[f * 4 + c] = new Vector3(p.x, p.y, p.z + 0.5f);
                    normals[f * 4 + c] = n;
                }

                // u × v = n, so (0, 1, 2) runs counter-clockwise seen from outside: Unity wants
                // clockwise.
                var t = f * 6;
                triangles[t] = f * 4;
                triangles[t + 1] = f * 4 + 2;
                triangles[t + 2] = f * 4 + 1;
                triangles[t + 3] = f * 4;
                triangles[t + 4] = f * 4 + 3;
                triangles[t + 5] = f * 4 + 2;
            }

            var mesh = new Mesh { name = "DroodRiserStrap", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
