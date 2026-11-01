using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The drop zone's target on the ground: filled bands, red and white (Bands), in the target's
    /// own frame (up from the ground, a fixed tangent), lit and matte like paint on the ground
    /// (Standard), so shadows fall on it. One mesh, draped over the
    /// terrain: each point's height comes from a raycast down, retried every DrapeInterval until
    /// every point has hit the ground (the ground far from the drood may have no colliders yet;
    /// until then a point lies on the target's level). It lies Lift above the ground, more with
    /// the camera's distance (LiftPerMetre), so the depth buffer keeps it in front of the ground
    /// from high up.
    /// </summary>
    internal sealed class DroodDropZoneRings
    {
        // The bands' outer radii (m), from the middle out; even ones red, odd ones white.
        private static readonly float[] Bands = { 4f, 10f, 20f, 40f, 50f };
        // No step between rings of points longer than this (m), so the bands follow the ground.
        private const float MaxRadialStep = 1f;
        private const int Segments = 192;

        // Each point raycast from this high above the target's level, this far down (m).
        private const float RayAbove = 150f;
        private const float RayLength = 300f;
        // A point still without ground this many drapes after the middle found its own is left
        // out (a ray that never hits, e.g. starting inside a structure, would leave it floating
        // at the target's level); until then, far away with no colliders yet, it lies flat.
        private const int MissingAfterRounds = 3;
        private const float DrapeInterval = 1f;
        // Raycasts per frame while draping: a pass over all the points spreads over frames.
        private const int DrapeRaysPerFrame = 1500;
        private int _drapeCursor;
        private bool _drapeAll;

        // Drawn right after the ground (Queue) without writing depth, so what lies on the ground
        // under it and is drawn later (the landed wing's lines: AfterQueue) shows on top of it;
        // it still hides behind what stands on it (its depth test).
        private const int Queue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;

        /// <summary>The draw queue of what should show on the target though it lies under it.</summary>
        internal const int AfterQueue = Queue + 1;

        // Above the ground by Lift, and LiftPerMetre of the camera's distance, up to MaxLift (m).
        private const float Lift = 0.03f;
        private const float LiftPerMetre = 0.0001f;
        private const float MaxLift = 0.3f;

        // Across a step in the ground (a pad's edge, a cliff, a wall) the draped points can't
        // follow: a piece with an edge rising more than MaxStepSlope (height per horizontal
        // metre, 0.58: 30°) plus StepSlack (m) is left out, a clean gap instead of a jagged wall.
        // Ordinary slopes stay; a structure's sloped sides (~40°) end the target at its top.
        private const float MaxStepSlope = 0.58f;
        private const float StepSlack = 0.2f;
        // ... and a piece with a corner more than MaxHeightOff (m) above or below the middle's
        // ground is left out too: the target stays on its own level (no roof, no ground below).
        private const float MaxHeightOff = 0.5f;
        // Of what is left, only the pieces joined to the middle (through shared corners) are
        // drawn: the target stays on the surface its middle lies on and ends at its edge (a pad's
        // edge, a cliff), not carried on below or above it.
        private readonly int[] _group;

        // Painted cloth's colours, not pure ones (pure white and a saturated red read as neon
        // under the game's light): a brick red and an off-white, their contrast keeping the
        // rings readable from height.
        private static readonly Color Red = new Color(0.58f, 0.1f, 0.07f);
        private static readonly Color White = new Color(0.76f, 0.75f, 0.71f);

        private readonly Transform _root;
        private readonly Mesh _mesh;
        // Made here, so destroyed here (Unity doesn't free materials made in code with their object).
        private readonly Material[] _materials;
        // The points across the target's frame (x east, z north; y filled by draping), as drawn
        // (lifted), and which have found the ground.
        private readonly Vector3[] _points;
        private readonly Vector3[] _drawn;
        private readonly bool[] _draped;
        // Every piece per colour (triangles, three indices each), and those drawn (no step).
        private readonly int[] _redAll;
        private readonly int[] _whiteAll;
        private readonly System.Collections.Generic.List<int> _redDrawn = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> _whiteDrawn = new System.Collections.Generic.List<int>();
        private float _nextDrape;
        private bool _allDraped;
        private int _roundsSinceMiddle = -1;
        private float _lift = -1f;

        // The drood's body: its colliders (and those of parts on it) aren't ground.
        private readonly Rigidbody _droodBody;

        internal DroodDropZoneRings(int layer, Rigidbody droodBody)
        {
            _droodBody = droodBody;
            _root = new GameObject("Drood2.DropZoneTarget").transform;
            _root.gameObject.layer = layer;

            // Rings of points: every band edge, with steps of at most MaxRadialStep between.
            var radii = new System.Collections.Generic.List<float>();
            var inner = 0f;
            foreach (var outer in Bands)
            {
                var steps = Mathf.Max(1, Mathf.CeilToInt((outer - inner) / MaxRadialStep));
                for (var s = 1; s <= steps; s++)
                {
                    radii.Add(Mathf.Lerp(inner, outer, s / (float)steps));
                }

                inner = outer;
            }

            _points = new Vector3[1 + radii.Count * Segments];
            for (var r = 0; r < radii.Count; r++)
            {
                for (var k = 0; k < Segments; k++)
                {
                    var a = 2f * Mathf.PI * k / Segments;
                    _points[1 + r * Segments + k] = new Vector3(Mathf.Cos(a) * radii[r], 0f, Mathf.Sin(a) * radii[r]);
                }
            }

            _drawn = new Vector3[_points.Length];
            _draped = new bool[_points.Length];
            _group = new int[_points.Length];

            // Triangles per band colour: the middle fan, then the strips between rings.
            var red = new System.Collections.Generic.List<int>();
            var white = new System.Collections.Generic.List<int>();
            for (var r = 0; r < radii.Count; r++)
            {
                var middle = (r == 0 ? 0f : radii[r - 1]) * 0.5f + radii[r] * 0.5f;
                var band = 0;
                while (band < Bands.Length - 1 && middle > Bands[band])
                {
                    band++;
                }

                var list = band % 2 == 0 ? red : white;
                for (var k = 0; k < Segments; k++)
                {
                    var next = (k + 1) % Segments;
                    var a = 1 + r * Segments + k;
                    var b = 1 + r * Segments + next;
                    if (r == 0)
                    {
                        list.Add(0);
                        list.Add(b);
                        list.Add(a);
                        continue;
                    }

                    var c = 1 + (r - 1) * Segments + k;
                    var d = 1 + (r - 1) * Segments + next;
                    list.Add(c);
                    list.Add(b);
                    list.Add(a);
                    list.Add(c);
                    list.Add(d);
                    list.Add(b);
                }
            }

            _redAll = red.ToArray();
            _whiteAll = white.ToArray();
            _mesh = new Mesh { name = "Drood2.DropZoneTarget" };
            _mesh.vertices = _points;
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(red, 0);
            _mesh.SetTriangles(white, 1);
            _mesh.RecalculateBounds();

            // Normals up (the target's own frame): lit like flat ground, the drape's slopes small.
            var normals = new Vector3[_points.Length];
            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.up;
            }

            _mesh.normals = normals;
            var shader = Shader.Find("Standard");
            _root.gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _root.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            _materials = new[] { MakeMaterial(shader, Red), MakeMaterial(shader, White) };
            renderer.sharedMaterials = _materials;
        }

        private static Material MakeMaterial(Shader shader, Color color)
        {
            var material = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "Drood 2.0 drop zone", color = color };
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0f);
                material.SetFloat("_Metallic", 0f);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetInt("_ZWrite", 0);
            }

            material.renderQueue = Queue;
            return material;
        }

        /// <summary>The target at <paramref name="centre"/> (world), its up and east (world).</summary>
        internal void Place(Vector3 centre, Vector3 up, Vector3 east)
        {
            if (!_root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(true);
            }

            var north = Vector3.Cross(east, up);
            _root.SetPositionAndRotation(centre, Quaternion.LookRotation(north, up));

            var camera = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera?.NearCamera;
            var lift = camera != null
                ? Mathf.Min(Lift + Vector3.Distance(camera.transform.position, centre) * LiftPerMetre, MaxLift)
                : Lift;
            // A pass over the points runs over frames (DrapeRaysPerFrame); when it ends the shape
            // is rewritten, and the next pass waits DrapeInterval.
            var drape = false;
            if (!_allDraped && (_drapeCursor > 0 || Time.time >= _nextDrape))
            {
                drape = Drape(up);
                if (drape)
                {
                    _nextDrape = Time.time + DrapeInterval;
                }
            }

            // Only rewritten when draped again or the lift changes noticeably.
            if (drape || Mathf.Abs(lift - _lift) > _lift * 0.1f)
            {
                _lift = lift;
                for (var i = 0; i < _points.Length; i++)
                {
                    _drawn[i] = _points[i] + Vector3.up * lift;
                }

                _mesh.vertices = _drawn;
                if (drape)
                {
                    JoinWithoutSteps();
                    _cutRadius = OuterCut();
                    LeaveOutSteps(_redAll, _redDrawn, 0);
                    LeaveOutSteps(_whiteAll, _whiteDrawn, 1);
                }

                _mesh.RecalculateBounds();
            }
        }

        /// <summary>
        /// Groups the points joined by pieces without a step (union-find over the pieces of both
        /// colours), so <see cref="LeaveOutSteps"/> keeps only the middle's group.
        /// </summary>
        private void JoinWithoutSteps()
        {
            for (var i = 0; i < _group.Length; i++)
            {
                _group[i] = i;
            }

            JoinPieces(_redAll);
            JoinPieces(_whiteAll);
        }

        private void JoinPieces(int[] all)
        {
            for (var t = 0; t + 2 < all.Length; t += 3)
            {
                if (Flat(all, t))
                {
                    Join(all[t], all[t + 1]);
                    Join(all[t + 1], all[t + 2]);
                }
            }
        }

        private void Join(int a, int b)
        {
            a = Group(a);
            b = Group(b);
            if (a != b)
            {
                _group[a] = b;
            }
        }

        private int Group(int point)
        {
            while (_group[point] != point)
            {
                _group[point] = _group[_group[point]];
                point = _group[point];
            }

            return point;
        }

        /// <summary>A piece (three indices from <paramref name="t"/>) with no step along its edges, on the middle's level.</summary>
        private bool Flat(int[] all, int t) => !Step(all[t], all[t + 1]) && !Step(all[t + 1], all[t + 2]) && !Step(all[t + 2], all[t])
            && Level(all[t]) && Level(all[t + 1]) && Level(all[t + 2]);

        /// <summary>A point within MaxHeightOff of the middle's ground, and not missing (MissingAfterRounds).</summary>
        private bool Level(int point) => Mathf.Abs(_points[point].y - _points[0].y) <= MaxHeightOff
            && (_draped[point] || _roundsSinceMiddle < MissingAfterRounds);

        /// <summary>
        /// The pieces of one colour without those across a step (MaxStepSlope) or apart from the
        /// middle's surface (<see cref="JoinWithoutSteps"/>).
        /// </summary>
        private void LeaveOutSteps(int[] all, System.Collections.Generic.List<int> drawn, int subMesh)
        {
            drawn.Clear();
            var middle = Group(0);
            for (var t = 0; t + 2 < all.Length; t += 3)
            {
                if (Flat(all, t) && Group(all[t]) == middle && Radius(all, t) < _cutRadius)
                {
                    drawn.Add(all[t]);
                    drawn.Add(all[t + 1]);
                    drawn.Add(all[t + 2]);
                }
            }

            _mesh.SetTriangles(drawn, subMesh);
        }

        // The outer white and red bands (from OuterBandsFrom) go, each with those outside it, when
        // less than OuterBandMinShare of its area would be drawn (a ragged arc reads worse than none).
        private const int OuterBandsFrom = 3;
        private const float OuterBandMinShare = 0.5f;
        private float _cutRadius = float.MaxValue;

        /// <summary>A piece's distance from the middle (its corners' average, in the target's plane).</summary>
        private float Radius(int[] all, int t)
        {
            var c = (_points[all[t]] + _points[all[t + 1]] + _points[all[t + 2]]) / 3f;
            return new Vector2(c.x, c.z).magnitude;
        }

        /// <summary>The radius from which nothing is drawn (OuterBandMinShare), or none.</summary>
        private float OuterCut()
        {
            var middle = Group(0);
            for (var band = OuterBandsFrom; band < Bands.Length; band++)
            {
                float inner = Bands[band - 1], outer = Bands[band], drawn = 0f, total = 0f;
                foreach (var all in new[] { _redAll, _whiteAll })
                {
                    for (var t = 0; t + 2 < all.Length; t += 3)
                    {
                        var r = Radius(all, t);
                        if (r < inner || r >= outer)
                        {
                            continue;
                        }

                        var a = _points[all[t]];
                        var b = _points[all[t + 1]];
                        var c = _points[all[t + 2]];
                        var area = Mathf.Abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z));
                        total += area;
                        if (Flat(all, t) && Group(all[t]) == middle)
                        {
                            drawn += area;
                        }
                    }
                }

                if (total > 0f && drawn < OuterBandMinShare * total)
                {
                    return inner;
                }
            }

            return float.MaxValue;
        }

        private bool Step(int a, int b)
        {
            var across = new Vector2(_points[a].x - _points[b].x, _points[a].z - _points[b].z).magnitude;
            return Mathf.Abs(_points[a].y - _points[b].y) > MaxStepSlope * across + StepSlack;
        }

        /// <summary>Part of a pass raycasting each point not yet on the ground (true when the pass ends); the heights in the target's frame.</summary>
        private bool Drape(Vector3 up)
        {
            if (_drapeCursor == 0)
            {
                _drapeAll = true;
            }

            var rays = 0;
            for (; _drapeCursor < _points.Length && rays < DrapeRaysPerFrame; _drapeCursor++)
            {
                var i = _drapeCursor;
                if (_draped[i])
                {
                    continue;
                }

                rays++;

                var flat = _root.TransformPoint(new Vector3(_points[i].x, 0f, _points[i].z));
                if (GroundBelow(flat + up * RayAbove, -up, out var ground))
                {
                    _points[i].y = _root.InverseTransformPoint(ground).y;
                    _draped[i] = true;
                }
                else
                {
                    _drapeAll = false;
                }
            }

            if (_drapeCursor < _points.Length)
            {
                return false;
            }

            // A pass is done.
            _drapeCursor = 0;
            _allDraped = _drapeAll;
            if (_draped[0])
            {
                _roundsSinceMiddle++;
            }

            return true;
        }

        // Many hits fit: a long ray through a structure meets several colliders (unsorted).
        private readonly RaycastHit[] _hits = new RaycastHit[32];

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

            if (_mesh != null)
            {
                Object.Destroy(_mesh);
            }

            foreach (var material in _materials)
            {
                if (material != null)
                {
                    Object.Destroy(material);
                }
            }
        }
    }
}
