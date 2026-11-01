using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// A copied plane's propeller pivot (<see cref="DroodPlaneScenery"/>), drawn as a spinning
    /// propeller looks: the blades (its meshes reaching past BladeFrom of the radius) hidden, and
    /// in their place a blur disc the size of the blades, dark, see-through and darker toward the
    /// tips, turning slowly on its own (solid blades at the frame rate strobe: they seem to stand
    /// or crawl). The hub and spinner stay. The
    /// pivot turns about the axis across which its meshes are thinnest (the blades' disc). Set up
    /// once in the template; turned by <see cref="DroodPlaneRide"/> (no Update of its own).
    /// </summary>
    public class DroodPlanePropeller : MonoBehaviour
    {
        // Turns per second as drawn: below the real ~30, off the frame rate's multiples.
        private const float TurnsPerSecond = 14.5f;

        // Meshes reaching past this share of the radius are blades: hidden.
        private const float BladeFrom = 0.5f;

        // The disc: from DiscHub of the radius out, its alpha DiscAlphaMiddle mid-way and
        // DiscAlphaTips near the tips, its grey DiscGrey (DiscShade of it there, darker), fading
        // out at the edge; it
        // turns DiscTurnsPerSecond, its shading varying DiscRipple around (DiscLobes times).
        private const float DiscHub = 0.15f;
        private const float DiscAlphaMiddle = 0.1f;
        private const float DiscAlphaTips = 0.2f;
        private const float DiscGrey = 0.22f;
        private const float DiscShade = 0.5f;
        private const float DiscTurnsPerSecond = 0.6f;
        private const float DiscRipple = 0.25f;
        private const int DiscLobes = 3;
        private const int DiscSegments = 64;

        [SerializeField]
        private Vector3 _axis = Vector3.forward;

        [SerializeField]
        private Quaternion _rest = Quaternion.identity;

        [SerializeField]
        private Transform _disc;

        [SerializeField]
        private Quaternion _discRest = Quaternion.identity;

        private float _angle;
        private float _discAngle;

        /// <summary>
        /// In the template: the axis and size from the meshes, the blades made faint, the disc
        /// built (its mesh and material live as long as the template).
        /// </summary>
        internal void FindAxis()
        {
            _rest = transform.localRotation;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            var filters = GetComponentsInChildren<MeshFilter>(true);
            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                var toPivot = ToPivot(filter.transform);
                foreach (var vertex in mesh.vertices)
                {
                    var point = toPivot.MultiplyPoint3x4(vertex);
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            }

            var size = max - min;
            _axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;

            // The blades' reach from the axis, and where along it they turn.
            var radius = 0f;
            var along = 0f;
            var outer = 0;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                var toPivot = ToPivot(filter.transform);
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var point = toPivot.MultiplyPoint3x4(vertex);
                    radius = Mathf.Max(radius, Vector3.ProjectOnPlane(point, _axis).magnitude);
                }
            }

            var blades = 0;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                var toPivot = ToPivot(filter.transform);
                var reach = 0f;
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var point = toPivot.MultiplyPoint3x4(vertex);
                    var off = Vector3.ProjectOnPlane(point, _axis).magnitude;
                    reach = Mathf.Max(reach, off);
                    if (off > BladeFrom * radius)
                    {
                        along += Vector3.Dot(point, _axis);
                        outer++;
                    }
                }

                if (reach > BladeFrom * radius && filter.TryGetComponent<MeshRenderer>(out var renderer))
                {
                    renderer.enabled = false;
                    blades++;
                }
            }

            along = outer > 0 ? along / outer : 0f;
            BuildDisc(radius, along);
        }

        private Matrix4x4 ToPivot(Transform child) => transform.worldToLocalMatrix * child.localToWorldMatrix;

        /// <summary>The blur disc beside the pivot (it turns on its own): rings of vertices, alpha in their colours.</summary>
        private void BuildDisc(float radius, float along)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || radius <= 0f || transform.parent == null)
            {
                return;
            }

            float[] rings = { DiscHub, 0.55f, 0.9f, 1f };
            float[] alphas = { 0f, DiscAlphaMiddle, DiscAlphaTips, 0f };
            float[] shades = { 1f, 1f, DiscShade, DiscShade };
            var vertices = new Vector3[rings.Length * (DiscSegments + 1)];
            var colours = new Color[vertices.Length];
            for (var r = 0; r < rings.Length; r++)
            {
                for (var s = 0; s <= DiscSegments; s++)
                {
                    var angle = s * Mathf.PI * 2f / DiscSegments;
                    var ripple = 1f + DiscRipple * Mathf.Cos(angle * DiscLobes);
                    var i = r * (DiscSegments + 1) + s;
                    vertices[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (rings[r] * radius);
                    var shade = DiscGrey * shades[r];
                    colours[i] = new Color(shade, shade, shade, Mathf.Clamp01(alphas[r] * ripple));
                }
            }

            var triangles = new int[(rings.Length - 1) * DiscSegments * 6];
            var t = 0;
            for (var r = 0; r < rings.Length - 1; r++)
            {
                for (var s = 0; s < DiscSegments; s++)
                {
                    var a = r * (DiscSegments + 1) + s;
                    var b = a + DiscSegments + 1;
                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = a + 1;
                    triangles[t++] = a + 1;
                    triangles[t++] = b;
                    triangles[t++] = b + 1;
                }
            }

            var mesh = new Mesh { name = "Drood2.Plane propeller disc", vertices = vertices, colors = colours, triangles = triangles };
            mesh.RecalculateBounds();
            _disc = new GameObject("Drood2.PropellerDisc").transform;
            _disc.SetParent(transform.parent, false);
            _disc.localPosition = transform.localPosition + transform.localRotation * (_axis * along);
            _discRest = transform.localRotation * Quaternion.FromToRotation(Vector3.forward, _axis);
            _disc.localRotation = _discRest;
            _disc.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = _disc.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(shader) { name = "Drood2.Plane propeller disc", renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        internal void Turn(float dt)
        {
            _angle = Mathf.Repeat(_angle + 360f * TurnsPerSecond * dt, 360f);
            transform.localRotation = _rest * Quaternion.AngleAxis(_angle, _axis);
            if (_disc != null)
            {
                _discAngle = Mathf.Repeat(_discAngle + 360f * DiscTurnsPerSecond * dt, 360f);
                _disc.localRotation = _discRest * Quaternion.AngleAxis(_discAngle, Vector3.forward);
            }
        }
    }
}
