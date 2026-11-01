using System.Collections.Generic;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The swoop course on the ground (<see cref="DroodSwoopCourse"/>), in the course's frame (x to
    /// the right, y up, z down the lane from the entry gate): the gate's two poles, their tops at
    /// the gate's height (nothing between them to fly under), striped orange and white every
    /// StripeHeight as height marks; a line across the lane every
    /// LineSpacing (the gate's and every RedEvery-th red, the others white) and a marker on both
    /// edges at each line. Lines and markers are draped over the ground (raycasts down, retried
    /// every DrapeInterval until each has hit), the lines lying Lift above it like the drop
    /// zone's target (painted, no depth written). No colliders.
    /// </summary>
    internal sealed class DroodSwoopCourseMarks
    {
        private const float PoleHeight = DroodSwoopCourse.GateHeight;

        private const float GatePoleRadius = 0.09f;
        private const float StripeHeight = 1f;
        private const float LineSpacing = 10f;
        private const int RedEvery = 5;
        private const float LineWidth = 0.3f;
        private const float GateLineWidth = 0.54f;
        private const int LinePoints = 11;
        private const float MarkerSize = 0.3f;

        private const float RayAbove = 50f;
        private const float RayLength = 100f;
        private const float DrapeInterval = 1f;
        private const float Lift = 0.03f;
        private const float LiftPerMetre = 0.0001f;
        private const float MaxLift = 0.3f;

        private static readonly Color Orange = new Color(0.8f, 0.35f, 0.08f);
        private static readonly Color Red = new Color(0.58f, 0.1f, 0.07f);
        private static readonly Color White = new Color(0.76f, 0.75f, 0.71f);

        private readonly Rigidbody _droodBody;
        private readonly Transform _root;
        private readonly Mesh _lines;
        private readonly List<Material> _materials = new List<Material>();

        // The probes (x, z in the course's frame): each line's points, then the markers', then
        // the poles'; their ground heights and whether they have found the ground.
        private readonly Vector2[] _probes;
        private readonly float[] _heights;
        private readonly bool[] _draped;
        private readonly int _lineProbes;
        private readonly Transform[] _standing;
        private readonly float[] _standingBase;
        private readonly bool[] _standingCylinder;
        private readonly Vector3[] _lineVertices;
        private readonly RaycastHit[] _hits = new RaycastHit[32];
        private float _nextDrape;
        private bool _allDraped;
        private float _lift = -1f;

        internal DroodSwoopCourseMarks(int layer, Rigidbody droodBody)
        {
            _droodBody = droodBody;
            _root = new GameObject("Drood2.SwoopCourse").transform;
            _root.gameObject.layer = layer;
            var standard = Shader.Find("Standard");
            var orange = MakeMaterial(standard, Orange, false);
            var redPaint = MakeMaterial(standard, Red, true);
            var whitePaint = MakeMaterial(standard, White, true);

            var lineCount = Mathf.FloorToInt(DroodSwoopCourse.LaneLength / LineSpacing) + 1;
            var probes = new List<Vector2>();
            for (var l = 0; l < lineCount; l++)
            {
                for (var p = 0; p < LinePoints; p++)
                {
                    probes.Add(new Vector2(Mathf.Lerp(-0.5f, 0.5f, p / (LinePoints - 1f)) * DroodSwoopCourse.LaneWidth, l * LineSpacing));
                }
            }

            _lineProbes = probes.Count;
            var standing = new List<Transform>();
            var bases = new List<float>();
            for (var l = 0; l < lineCount; l++)
            {
                for (var s = -1; s <= 1; s += 2)
                {
                    probes.Add(new Vector2(s * (DroodSwoopCourse.LaneWidth * 0.5f + MarkerSize), l * LineSpacing));
                    standing.Add(MakePart(PrimitiveType.Cube, orange, new Vector3(MarkerSize, MarkerSize, MarkerSize), layer));
                    bases.Add(0f);
                }
            }

            // The gate's poles, each a stack of stripes.
            var white = MakeMaterial(standard, White, false);
            var stripes = Mathf.RoundToInt(PoleHeight / StripeHeight);
            for (var s = -1; s <= 1; s += 2)
            {
                var at = new Vector2(s * DroodSwoopCourse.GateWidth * 0.5f, 0f);
                for (var k = 0; k < stripes; k++)
                {
                    probes.Add(at);
                    standing.Add(MakePart(PrimitiveType.Cylinder, k % 2 == 0 ? orange : white, new Vector3(GatePoleRadius * 2f, StripeHeight * 0.5f, GatePoleRadius * 2f), layer));
                    bases.Add(k * StripeHeight);
                }
            }

            _probes = probes.ToArray();
            _heights = new float[_probes.Length];
            _draped = new bool[_probes.Length];
            _standing = standing.ToArray();
            _standingBase = bases.ToArray();
            _standingCylinder = new bool[_standing.Length];
            for (var i = 0; i < _standing.Length; i++)
            {
                _standingCylinder[i] = _standing[i].GetComponent<MeshFilter>()?.sharedMesh?.name == "Cylinder";
            }

            // The lines: a strip LineWidth wide along each line's points.
            _lineVertices = new Vector3[_lineProbes * 2];
            var redTriangles = new List<int>();
            var whiteTriangles = new List<int>();
            for (var l = 0; l < lineCount; l++)
            {
                var list = l % RedEvery == 0 ? redTriangles : whiteTriangles;
                for (var p = 0; p < LinePoints - 1; p++)
                {
                    var a = (l * LinePoints + p) * 2;
                    list.Add(a);
                    list.Add(a + 1);
                    list.Add(a + 2);
                    list.Add(a + 2);
                    list.Add(a + 1);
                    list.Add(a + 3);
                }
            }

            var normals = new Vector3[_lineVertices.Length];
            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.up;
            }

            _lines = new Mesh { name = "Drood2.SwoopCourseLines" };
            WriteLines(Lift);
            _lines.normals = normals;
            _lines.subMeshCount = 2;
            _lines.SetTriangles(redTriangles, 0);
            _lines.SetTriangles(whiteTriangles, 1);
            _lines.RecalculateBounds();
            var lines = new GameObject("Drood2.SwoopCourseLines");
            lines.layer = layer;
            lines.transform.SetParent(_root, false);
            lines.AddComponent<MeshFilter>().sharedMesh = _lines;
            var renderer = lines.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sharedMaterials = new[] { redPaint, whitePaint };
            Arrange();
        }

        /// <summary>The course at <paramref name="origin"/> (world, the gate's ground), its up and lane direction.</summary>
        internal void Place(Vector3 origin, Vector3 up, Vector3 forward)
        {
            if (!_root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(true);
            }

            _root.SetPositionAndRotation(origin, Quaternion.LookRotation(forward, up));
            var drape = !_allDraped && Time.time >= _nextDrape && Drape(up);
            var camera = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera?.NearCamera;
            var lift = camera != null ? Mathf.Min(Lift + Vector3.Distance(camera.transform.position, origin) * LiftPerMetre, MaxLift) : Lift;
            if (drape || Mathf.Abs(lift - _lift) > _lift * 0.1f)
            {
                _lift = lift;
                WriteLines(lift);
                _lines.RecalculateBounds();
            }

            if (drape)
            {
                Arrange();
            }
        }

        internal void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(false);
            }
        }

        internal void Destroy()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }

            if (_lines != null)
            {
                Object.Destroy(_lines);
            }

            foreach (var material in _materials)
            {
                Object.Destroy(material);
            }
        }

        /// <summary>One raycast down per probe not yet on the ground (true: any found it).</summary>
        private bool Drape(Vector3 up)
        {
            _nextDrape = Time.time + DrapeInterval;
            var found = false;
            var all = true;
            for (var i = 0; i < _probes.Length; i++)
            {
                if (_draped[i])
                {
                    continue;
                }

                var from = _root.TransformPoint(new Vector3(_probes[i].x, RayAbove, _probes[i].y));
                if (GroundBelow(from, -up, out var ground))
                {
                    _heights[i] = _root.InverseTransformPoint(ground).y;
                    _draped[i] = true;
                    found = true;
                }
                else
                {
                    all = false;
                }
            }

            _allDraped = all;
            return found;
        }

        private bool GroundBelow(Vector3 from, Vector3 direction, out Vector3 ground)
        {
            ground = default;
            var count = Physics.RaycastNonAlloc(from, direction, _hits, RayLength, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.distance >= best || (_droodBody != null && hit.rigidbody == _droodBody))
                {
                    continue;
                }

                best = hit.distance;
                ground = hit.point;
            }

            return best < float.MaxValue;
        }

        private void WriteLines(float lift)
        {
            for (var i = 0; i < _lineProbes; i++)
            {
                var y = _heights[i] + lift;
                // The gate's line (the first) wider.
                var half = (i < LinePoints ? GateLineWidth : LineWidth) * 0.5f;
                _lineVertices[i * 2] = new Vector3(_probes[i].x, y, _probes[i].y - half);
                _lineVertices[i * 2 + 1] = new Vector3(_probes[i].x, y, _probes[i].y + half);
            }

            _lines.vertices = _lineVertices;
        }

        /// <summary>The markers and poles (each stripe its height up) on their ground.</summary>
        private void Arrange()
        {
            for (var i = 0; i < _standing.Length; i++)
            {
                var probe = _probes[_lineProbes + i];
                // A cylinder is twice its scale tall, a cube its scale.
                var half = _standing[i].localScale.y * (_standingCylinder[i] ? 1f : 0.5f);
                _standing[i].localPosition = new Vector3(probe.x, _heights[_lineProbes + i] + _standingBase[i] + half, probe.y);
            }
        }

        private Transform MakePart(PrimitiveType type, Material material, Vector3 scale, int layer)
        {
            var part = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.layer = layer;
            part.transform.SetParent(_root, false);
            part.transform.localScale = scale;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return part.transform;
        }

        private Material MakeMaterial(Shader shader, Color color, bool paint)
        {
            var material = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "Drood 2.0 swoop course", color = color };
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", paint ? 0f : 0.3f);
                material.SetFloat("_Metallic", 0f);
            }

            if (paint)
            {
                if (material.HasProperty("_ZWrite"))
                {
                    material.SetInt("_ZWrite", 0);
                }

                material.renderQueue = DroodDropZoneRings.AfterQueue - 1;
            }

            _materials.Add(material);
            return material;
        }
    }
}
