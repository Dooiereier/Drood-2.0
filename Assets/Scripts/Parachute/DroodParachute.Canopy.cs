using UnityEngine;

namespace Drood2
{
    // Canopy: building the canopy and lines, plain-fabric materials, placing the canopy in flight
    // and landed, its heading.
    public partial class DroodParachute
    {
        /// <summary>Visual step: called from the modifier's game-loop LateUpdate.</summary>
        internal void VisualStep()
        {
            if (_state != State.Deployed || !_visualsBuilt)
            {
                return;
            }

            UpdateCanopyFabric();
            if (_collapse > 0f)
            {
                var speed = IsRamAir && _visualCollapse >= RamAirFoldSecondPartFrom ? RamAirFoldSecondPartSpeed : 1f;
                _visualCollapse = Mathf.MoveTowards(_visualCollapse, 1f, speed * Time.deltaTime / VisualCollapseSeconds);
            }
            UpdateLandedAnchor();
            StepShapeClock();

            // Drawn between the last two physics steps, as far as the drood's body is drawn
            // between them (where Unity's interpolation has put it along its last step; by the
            // clock when it hardly moves): stepped, the canopy's turn went in uneven jumps
            // between frames (one, two or no steps each), seen as judder in a fast turn, most in
            // the Canopy Chase that follows the wing.
            var direction = _canopyDirection;
            var forward = _canopyForward;
            var pitch = _attitudePitch;
            if (_stepDirection.sqrMagnitude > 0.5f && _stepForward.sqrMagnitude > 0.5f && Time.fixedDeltaTime > 0f)
            {
                var between = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                var body = _part?.BodyScript?.RigidBody;
                var step = body != null ? body.position - _stepRoot : Vector3.zero;
                if (step.sqrMagnitude > BetweenMinStep * BetweenMinStep)
                {
                    between = Mathf.Clamp01(Vector3.Dot(transform.position - _stepRoot, step) / step.sqrMagnitude);
                }
                _canopyDirection = Vector3.Slerp(_stepDirection, direction, between).normalized;
                _canopyForward = Vector3.Slerp(_stepForward, forward, between).normalized;
                _attitudePitch = Mathf.Lerp(_stepPitch, pitch, between);
            }

            PlaceVisuals();
            _drawnDirection = _canopyDirection;
            _canopyDirection = direction;
            _canopyForward = forward;
            _attitudePitch = pitch;
        }

        // The canopy's direction, heading and pitch before the last physics step (VisualStep),
        // and the drood's body there.
        private Vector3 _stepDirection;
        private Vector3 _stepRoot;

        // Below this step (m) the share between the steps goes by the clock (VisualStep).
        private const float BetweenMinStep = 0.01f;

        // The lines' direction as last drawn (for the chase camera's centre, UpTheLines).
        private Vector3 _drawnDirection;
        private Vector3 _stepForward;
        private float _stepPitch;

        /// <summary>
        /// The canopy takes the drood's part materials, so it follows the palette colours,
        /// but those also carry the suit's surface textures (the dotted grip pattern). The
        /// canopy gets copies of the game's materials with the detail and normal-map
        /// textures switched off (the same switch as the game's quality setting). Colours
        /// and the rest are re-copied from the game's material now and then, and whenever
        /// the game swaps in a different material (highlight, transparency, repaint).
        /// </summary>
        private void UpdateCanopyFabric()
        {
            var refresh = Time.time >= _nextFabricRefreshTime;
            if (refresh)
            {
                _nextFabricRefreshTime = Time.time + FabricRefreshInterval;
            }

            UpdateFabric(_canopyRenderer, refresh);
            foreach (var renderer in _extraFabricRenderers)
            {
                UpdateFabric(renderer, refresh);
            }
        }

        private void UpdateFabric(Renderer renderer, bool refresh)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetSharedMaterials(_materialBuffer);
            var changed = false;
            for (var i = 0; i < _materialBuffer.Count; i++)
            {
                var material = _materialBuffer[i];
                if (material == null)
                {
                    continue;
                }

                if (_fabricSources.TryGetValue(material, out var source))
                {
                    // Already ours: refresh it from the game's material now and then.
                    if (refresh && source != null)
                    {
                        CopyAsFabric(source, material);
                    }

                    continue;
                }

                // A game material: swap in (or create) its plain-fabric copy.
                if (!_fabricMaterials.TryGetValue(material, out var fabric) || fabric == null)
                {
                    fabric = new Material(material) { name = material.name + " (Drood 2.0 canopy fabric)" };
                    _fabricMaterials[material] = fabric;
                    _fabricSources[fabric] = material;
                }

                CopyAsFabric(material, fabric);
                _materialBuffer[i] = fabric;
                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = _materialBuffer.ToArray();
            }
        }

        private static void CopyAsFabric(Material source, Material fabric)
        {
            fabric.CopyPropertiesFromMaterial(source);
            fabric.renderQueue = source.renderQueue;
            fabric.DisableKeyword("DETAIL_TEXTURES_ON");
            fabric.DisableKeyword("NORMAL_MAPS_ON");
        }

        /// <summary>
        /// Places the canopy and shroud lines on the drood's current pose. Called from
        /// <see cref="VisualStep"/> at the end of the late chain, after the IK solve and the pose
        /// blend, so the lines end at the frame's final shoulders. Only reads state.
        /// </summary>
        // On the ground the canopy hangs from where the harness stood (in the drood's own frame,
        // remembered while airborne) and follows the shoulders only LandedCanopyFollow of the way:
        // a drood collapsing or falling flat pulls its lines over, not the canopy along.
        private const float LandedCanopyFollow = 0.2f;
        private Vector3 _harnessLocal;
        private bool _hasHarnessLocal;

        /// <summary>The canopy is down and folded on the ground (or there is none out).</summary>
        internal bool CanopyFolded => _state != State.Deployed || _visualCollapse >= 1f;

        /// <summary>Where the canopy's lines hang from: the harness in the air, see LandedCanopyFollow down.</summary>
        private Vector3 CanopyAnchor(Vector3 harness)
        {
            if (!TouchedDown)
            {
                _harnessLocal = transform.InverseTransformPoint(harness);
                _hasHarnessLocal = true;
                return harness;
            }

            return _hasHarnessLocal ? Vector3.Lerp(transform.TransformPoint(_harnessLocal), harness, LandedCanopyFollow) : harness;
        }

        private void PlaceVisuals()
        {
            if (_state != State.Deployed || !_visualsBuilt)
            {
                return;
            }

            var harness = HarnessPosition;
            var lineLength = Mathf.Lerp(1f, LineLength, _extend);
            // The round canopy fills out eased (slow at first, quicker, settling), from a slim bundle.
            var fill = IsRamAir ? _inflate : _inflate * _inflate * (3f - 2f * _inflate);
            var flyingRadius = IsRamAir ? Mathf.Lerp(DeflatedRadius, InflatedRadius, _inflate) : Mathf.Lerp(StartRadius, InflatedRadius, fill);
            var distance = Mathf.Sqrt(Mathf.Max(lineLength * lineLength - flyingRadius * flyingRadius, 0.25f));

            // In flight: canopy up the lines, opening facing the drood.
            var flyingPosition = CanopyAnchor(harness) + _canopyDirection * distance;
            var flyingRotation = CanopyRotation(_canopyDirection);

            // Landing, in overlapping phases. Unloaded, the canopy sinks and drifts downwind
            // at a steady pace, easing in and settling softly (drop), keeping most of its
            // shape; near the ground it falls over away from the drood (tip) and flattens
            // into a long strip (flatten).
            var c = _visualCollapse;
            var sink = Mathf.InverseLerp(0f, 0.85f, c);
            var drop = SteadyEase(sink, 0.15f, 0.3f);
            var tip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.9f, c));
            var flatten = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, c));
            if (IsRamAir)
            {
                PlaceRamAirVisuals(harness, drop, tip, flatten, flyingRotation);
                return;
            }

            // Lying: canopy axis (root up, skirt to apex) along the ground away from the
            // drood, root forward (the squashed axis) along the ground normal, with a slight
            // roll so it isn't perfectly symmetric.
            var away = Vector3.ProjectOnPlane(_landedGround - transform.position, _landedNormal);
            if (away.sqrMagnitude < 1e-4f)
            {
                away = Vector3.ProjectOnPlane(-transform.forward, _landedNormal);
            }

            away.Normalize();
            // In the air there is no ground yet (its normal is zero): the flying turn.
            var groundRotation = _landedNormal.sqrMagnitude > 1e-6f && away.sqrMagnitude > 1e-6f
                ? Quaternion.AngleAxis(8f, away) * Quaternion.LookRotation(_landedNormal, away)
                : flyingRotation;
            var groundPosition = _landedGround + _landedNormal * LandedThickness;

            var floatingRadius = flyingRadius * (1f - FloatingDeflation * drop);
            var radius = Mathf.Lerp(floatingRadius, LandedRadius, flatten);
            _rimRadius = radius * _meshSkirtShare;
            _canopyRoot.position = Vector3.Lerp(flyingPosition, groundPosition, drop);
            _canopyRootLocal = transform.InverseTransformPoint(_canopyRoot.position);
            _canopyRoot.rotation = Quaternion.Slerp(flyingRotation, groundRotation, tip);
            // Squash the dome's thickness (root Z is the ground normal once down).
            var squash = Mathf.Lerp(1f, LandedThickness / radius, flatten);
            _canopyRoot.localScale = new Vector3(1f, 1f, squash);

            if (_canopyMesh != null)
            {
                var length = IsRamAir ? 1f : Mathf.Lerp(BagLength, 1f, _extend);
                var height = Mathf.Lerp(CanopyHeight * length, LandedLength, flatten);
                var radialScale = radius / _meshRadius;
                var heightScale = height / _meshHeight;
                var scale = new Vector3(radialScale, radialScale, radialScale);
                scale[_meshHeightAxis] = heightScale;
                _canopyMesh.localScale = scale;

                // Put the mesh's rim at the canopy root.
                _canopyMesh.localPosition = Vector3.up * (-_meshRimCoordinate * _meshApexSign * heightScale);
            }

            // In flight the canopy's local +X is the drood's right (CanopyRotation), so each
            // line has a fixed shoulder: the right half of the rim goes to the right
            // shoulder, the left half to the left. The half-step offset keeps lines off the
            // front/back midline, where a line could belong to either side. Once down the
            // canopy lies in any direction, so each line goes to the shoulder on its side.
            var camera = GetGameCamera();
            CacheWidthCamera(camera);
            var rightShoulder = RiserAttach(RightShoulderPosition(harness));
            var leftShoulder = RiserAttach(LeftShoulderPosition(harness));
            var down = drop > 0.5f;
            var up = transform.up;
            for (var i = 0; i < _lines.Length; i++)
            {
                // At a hem point (the gores' lowest corners), or evenly round the skirt.
                var hem = _hemAngles != null && i < _hemAngles.Length;
                var angle = hem ? _hemAngles[i] : (i + 0.5f) * Mathf.PI * 2f / _lines.Length;
                var share = hem ? _hemShares[i] : _meshSkirtShare;
                var x = Mathf.Cos(angle);
                var local = new Vector3(x * radius * share, 0f, Mathf.Sin(angle) * radius * share * squash);
                var rim = _canopyRoot.position + _canopyRoot.rotation * local;
                var shoulder = down
                    ? (Vector3.Dot(rim - harness, transform.right) >= 0f ? rightShoulder : leftShoulder)
                    : (x >= 0f ? rightShoulder : leftShoulder);
                SetLinePoints(_lines[i], rim, shoulder, lineLength, up, drop);
                _lines[i].startWidth = PixelWidth(camera, rim);
                _lines[i].endWidth = PixelWidth(camera, shoulder);
            }

            // The steering lines, from the back of the rim to the toggles, while the hands hold them.
            for (var side = 0; side < _toggleLines.Length; side++)
            {
                var sign = side == 0 ? -1f : 1f;
                var fist = Vector3.zero;
                var held = _hangingPose != null && drop <= 0f && _hangingPose.TryGetFist(sign, out fist);
                _toggleLines[side].enabled = held;
                if (held)
                {
                    var rim = _canopyRoot.position + _canopyRoot.rotation * RoundToggleRim(sign);
                    _toggleLines[side].SetPosition(0, rim);
                    _toggleLines[side].SetPosition(1, fist);
                    _toggleLines[side].startWidth = PixelWidth(camera, rim);
                    _toggleLines[side].endWidth = PixelWidth(camera, fist);
                }
            }
        }

        /// <summary>
        /// 0..1 progress with a steady middle: speeds up over the first
        /// <paramref name="easeIn"/> fraction, runs at constant speed, and slows to a stop
        /// over the last <paramref name="easeOut"/> fraction (trapezoidal speed profile).
        /// </summary>
        private static float SteadyEase(float t, float easeIn, float easeOut)
        {
            t = Mathf.Clamp01(t);
            var speed = 1f / (1f - easeIn / 2f - easeOut / 2f);
            if (t < easeIn)
            {
                return speed * t * t / (2f * easeIn);
            }

            if (t > 1f - easeOut)
            {
                var r = 1f - t;
                return 1f - speed * r * r / (2f * easeOut);
            }

            return speed * (t - easeIn / 2f);
        }

        private static readonly Color LineColor = new Color(0.9f, 0.9f, 0.88f);

        private LineRenderer CreateLine(Material material)
        {
            var line = new GameObject("DroodShroudLine").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.gameObject.layer = gameObject.layer;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = ShroudLineMinWidth;
            line.endWidth = ShroudLineMinWidth;
            line.sharedMaterial = material;
            line.startColor = LineColor;
            line.endColor = LineColor;
            line.generateLightingData = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        private void BuildVisuals()
        {
            if (_visualsBuilt)
            {
                return;
            }

            _visualsBuilt = true;

            // Parented to the part so it follows floating-origin shifts. The world pose
            // is set every LateUpdate.
            _canopyRoot = new GameObject("DroodCanopy").transform;
            _canopyRoot.SetParent(transform, false);

            var prefab = Resources.Load<GameObject>(CanopyResource);
            Mesh dome = null;
            if (prefab != null)
            {
                var mesh = Instantiate(prefab, _canopyRoot, false);
                var meshFilter = mesh.GetComponentInChildren<MeshFilter>();
                _canopyRenderer = mesh.GetComponentInChildren<Renderer>();
                if (meshFilter != null && meshFilter.sharedMesh != null && _canopyRenderer != null)
                {
                    MeasureCanopyMesh(meshFilter.sharedMesh.bounds);
                    MeasureSkirt(meshFilter.sharedMesh);
                    _canopyMesh = mesh.transform;
                    _canopyMesh.localRotation = Quaternion.FromToRotation(AxisVector(_meshHeightAxis) * _meshApexSign, Vector3.up);
                    if (!IsRamAir)
                    {
                        MeasureHem(meshFilter.sharedMesh, _canopyMesh.localRotation);
                    }
                    if (IsRamAir)
                    {
                        dome = meshFilter.sharedMesh;
                        _wingMesh = BuildWingMesh(dome);
                        meshFilter.sharedMesh = _wingMesh;
                    }

                    // Colours and textures come from the drood's part materials (slots 15/16,
                    // the same defaults as the stock parachute). Excluded from mesh combining
                    // because it moves on its own, and from the drag model because drag
                    // is applied above.
                    _part.PartMaterialScript?.AddRenderer(_canopyRenderer, true, true, null, false);

                    // Taking the renderer over, the game may swap in its own copy of the mesh
                    // (RendererMaterialMap); the landed shape must bend the one that is shown.
                    if (IsRamAir && meshFilter.sharedMesh != _wingMesh && meshFilter.sharedMesh != null)
                    {
                        Destroy(_wingMesh);
                        _wingMesh = meshFilter.sharedMesh;
                    }

                    if (IsRamAir && _wingRestVertices != null)
                    {
                        PrepareWingShape();
                    }
                }
                else
                {
                    Destroy(mesh);
                }
            }
            else
            {
                Debug.LogWarning($"[Drood 2.0] Canopy mesh '{CanopyResource}' not found; drawing lines only.");
            }

            SetLayerRecursive(_canopyRoot.gameObject, gameObject.layer);

            // Lit lines (Unity's Standard shader, which the game ships), so the sun, shade and
            // night darken them like everything else (unlit, they look like they glow). Fallback:
            // the unlit Sprites/Default.
            var lit = Shader.Find("Standard");
            var shader = lit != null ? lit : Shader.Find("Sprites/Default");
            var lineMaterial = shader != null ? new Material(shader) { name = "Drood 2.0 lines" } : null;
            _lineMaterial = lineMaterial;
            if (lit != null && lineMaterial != null)
            {
                lineMaterial.color = LineColor;
                lineMaterial.SetFloat("_Glossiness", 0.1f);
                lineMaterial.SetFloat("_Metallic", 0f);
            }

            // Drawn after the drop zone's target: lying on the ground they'd be under it.
            if (lineMaterial != null)
            {
                lineMaterial.renderQueue = DroodDropZoneRings.AfterQueue;
            }
            _lines = new LineRenderer[IsRamAir ? RamAirLineCount : _hemAngles != null ? _hemAngles.Length : ShroudLineCount];
            for (var i = 0; i < _lines.Length; i++)
            {
                _lines[i] = CreateLine(lineMaterial);
            }

            // Round canopy: a steering line per side, from the back of the rim to the toggle in
            // the hand (the ram-air's run through its line cascade).
            _toggleLines = IsRamAir ? new LineRenderer[0] : new[] { CreateLine(lineMaterial), CreateLine(lineMaterial) };

            if (IsRamAir && prefab != null && dome != null)
            {
                BuildRamAirExtras(prefab, dome);
            }
        }

        /// <summary>
        /// The stock canopy mesh's axes and size aren't documented, so measure them: the
        /// dome's height axis is the one whose extent differs most from the other two.
        /// The apex is on the side the bounds centre leans to, and the rim is the other end.
        /// </summary>
        private void MeasureCanopyMesh(Bounds bounds)
        {
            var size = bounds.size;
            var bestDifference = -1f;
            for (var axis = 0; axis < 3; axis++)
            {
                var others = (size[(axis + 1) % 3] + size[(axis + 2) % 3]) * 0.5f;
                var difference = Mathf.Abs(size[axis] - others);
                if (difference > bestDifference)
                {
                    bestDifference = difference;
                    _meshHeightAxis = axis;
                }
            }

            _meshApexSign = bounds.center[_meshHeightAxis] < 0f ? -1f : 1f;
            _meshHeight = Mathf.Max(size[_meshHeightAxis], 0.001f);
            _meshRadius = Mathf.Max(Mathf.Max(size[(_meshHeightAxis + 1) % 3], size[(_meshHeightAxis + 2) % 3]) * 0.5f, 0.001f);
            _meshRimCoordinate = _meshApexSign > 0f ? bounds.min[_meshHeightAxis] : bounds.max[_meshHeightAxis];
        }

        /// <summary>
        /// The skirt's radius as a share of the dome's widest (the lines attach at the hem): the
        /// vertices within SkirtBand of the height from the rim, their widest radius.
        /// </summary>
        private void MeasureSkirt(Mesh mesh)
        {
            const float SkirtBand = 0.04f;
            if (!mesh.isReadable)
            {
                Log("Round canopy: its mesh isn't readable; the lines go to its widest radius.");
                return;
            }

            var a = (_meshHeightAxis + 1) % 3;
            var b = (_meshHeightAxis + 2) % 3;
            var centre = mesh.bounds.center;
            var skirt = 0f;
            foreach (var vertex in mesh.vertices)
            {
                if (Mathf.Abs(vertex[_meshHeightAxis] - _meshRimCoordinate) <= SkirtBand * _meshHeight)
                {
                    skirt = Mathf.Max(skirt, new Vector2(vertex[a] - centre[a], vertex[b] - centre[b]).magnitude);
                }
            }

            if (skirt > 0f)
            {
                _meshSkirtShare = Mathf.Clamp01(skirt / _meshRadius);
            }

            Log($"Round canopy: skirt {skirt:0.000} of the widest {_meshRadius:0.000} (mesh units): the lines at {_meshSkirtShare:0.00} of the radius.");
        }

        /// <summary>
        /// The round canopy's hem points: the vertices within HemBand of the height from the
        /// rim (the gores' lowest corners), grouped by their angle around the canopy (in the
        /// canopy's own axes, through <paramref name="toCanopy"/>); each group's middle angle and
        /// radius. Between HemPointsMin and HemPointsMax points, else the lines stay evenly round.
        /// </summary>
        private void MeasureHem(Mesh mesh, Quaternion toCanopy)
        {
            const float HemBand = 0.02f;
            const float GroupGap = 4f * Mathf.Deg2Rad;
            const int HemPointsMin = 6;
            const int HemPointsMax = 40;
            if (!mesh.isReadable)
            {
                return;
            }

            var a = (_meshHeightAxis + 1) % 3;
            var b = (_meshHeightAxis + 2) % 3;
            var centre = mesh.bounds.center;
            var points = new System.Collections.Generic.List<Vector2>();
            foreach (var vertex in mesh.vertices)
            {
                if (Mathf.Abs(vertex[_meshHeightAxis] - _meshRimCoordinate) > HemBand * _meshHeight)
                {
                    continue;
                }

                var across = Vector3.zero;
                across[a] = vertex[a] - centre[a];
                across[b] = vertex[b] - centre[b];
                var inCanopy = toCanopy * across;
                points.Add(new Vector2(Mathf.Atan2(inCanopy.z, inCanopy.x), across.magnitude));
            }

            if (points.Count == 0)
            {
                return;
            }

            points.Sort((p, q) => p.x.CompareTo(q.x));

            // Groups of points closer than GroupGap in angle (the last may wrap onto the first).
            var angles = new System.Collections.Generic.List<float>();
            var shares = new System.Collections.Generic.List<float>();
            var start = 0;
            for (var i = 1; i <= points.Count; i++)
            {
                if (i < points.Count && points[i].x - points[i - 1].x < GroupGap)
                {
                    continue;
                }

                float sumX = 0f, sumY = 0f, sumR = 0f;
                for (var j = start; j < i; j++)
                {
                    sumX += Mathf.Cos(points[j].x);
                    sumY += Mathf.Sin(points[j].x);
                    sumR += points[j].y;
                }

                angles.Add(Mathf.Atan2(sumY, sumX));
                shares.Add(sumR / (i - start) / _meshRadius);
                start = i;
            }

            if (angles.Count > 1 && points[points.Count - 1].x - points[0].x > 2f * Mathf.PI - GroupGap)
            {
                // The first and last groups are one, across the ±π seam.
                var last = angles.Count - 1;
                var joined = Mathf.Atan2(Mathf.Sin(angles[0]) + Mathf.Sin(angles[last]), Mathf.Cos(angles[0]) + Mathf.Cos(angles[last]));
                angles[0] = joined;
                shares[0] = (shares[0] + shares[last]) / 2f;
                angles.RemoveAt(last);
                shares.RemoveAt(last);
            }

            if (angles.Count < HemPointsMin || angles.Count > HemPointsMax)
            {
                Log($"Round canopy: {angles.Count} hem point groups found; the lines stay evenly round.");
                return;
            }

            _hemAngles = angles.ToArray();
            _hemShares = shares.ToArray();
            Log($"Round canopy: {_hemAngles.Length} hem points, at {shares[0]:0.00} of the widest radius: a line to each.");
        }

        private static Camera GetGameCamera()
        {
            return Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera?.NearCamera;
        }

        /// <summary>
        /// Canopy orientation with its axis along <paramref name="up"/> and its twist tied
        /// to the canopy's heading (<see cref="_canopyForward"/>, turned by the steering
        /// lines): local +Z is that heading and local +X its right. A plain FromToRotation
        /// picks an arbitrary twist that jumps as the direction changes, so the lines would swap
        /// shoulders.
        /// </summary>
        private Quaternion CanopyRotation(Vector3 up)
        {
            var forward = Vector3.ProjectOnPlane(_canopyForward, up);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.ProjectOnPlane(-transform.up, up);
            }

            return Quaternion.LookRotation(forward.normalized, up);
        }

        /// <summary>
        /// A point on the risers <paramref name="distance"/> from <paramref name="shoulder"/>, and
        /// the direction of the line there (up, toward the canopy), for the hanging pose's hand: in
        /// the middle of that shoulder's fan of lines (toward FanCentre of the rim radius on that
        /// side; the canopy's local ±X is the drood's right, see PlaceVisuals), where a jumper
        /// holds the risers. From the canopy as placed last frame, relative to the drood (in world
        /// space it would lag behind at speed). False when there's no flying canopy to grip.
        /// </summary>
        internal bool TryGetRiserGrip(Vector3 shoulder, float distance, out Vector3 grip, out Vector3 lineDirection)
        {
            grip = default;
            lineDirection = default;
            if (_state != State.Deployed || !_visualsBuilt || _visualCollapse > 0f)
            {
                return false;
            }

            var side = Vector3.Dot(shoulder - HarnessPosition, transform.right) >= 0f ? 1f : -1f;
            var rim = IsRamAir
                ? RamAirGripAnchor(side)
                : transform.TransformPoint(_canopyRootLocal) + _canopyRoot.rotation * new Vector3(side * _rimRadius * FanCentre, 0f, 0f);
            var attach = RiserAttach(shoulder);
            var toRim = rim - attach;
            if (toRim.sqrMagnitude < 1e-4f)
            {
                return false;
            }

            lineDirection = toRim.normalized;
            grip = attach + lineDirection * distance;
            return true;
        }

        /// <summary>
        /// Where the lines or risers leave the harness on <paramref name="shoulder"/>'s side: on
        /// top of the shoulder, out of the dark arc over the shoulder joint.
        /// From the shoulder bone (the arm's joint, at the outside of the shoulder): RiserInward
        /// toward the neck, RiserUp above it, RiserForward in front of it.
        /// </summary>
        internal Vector3 RiserAttach(Vector3 shoulder)
        {
            var inward = Vector3.Dot(shoulder - HarnessPosition, transform.right) >= 0f ? -transform.right : transform.right;
            return shoulder + inward * RiserInward + transform.up * RiserUp + transform.forward * RiserForward;
        }

        // The rig's "Left"/"Right" bone names are resolved against the drood's actual right
        // axis, so a mirrored rig can't cross the lines.
        private Vector3 RightShoulderPosition(Vector3 fallback)
        {
            if (_leftShoulder == null || _rightShoulder == null)
            {
                return fallback;
            }

            return Vector3.Dot(_rightShoulder.position - _leftShoulder.position, transform.right) >= 0f
                ? _rightShoulder.position
                : _leftShoulder.position;
        }

        private Vector3 LeftShoulderPosition(Vector3 fallback)
        {
            if (_leftShoulder == null || _rightShoulder == null)
            {
                return fallback;
            }

            return Vector3.Dot(_rightShoulder.position - _leftShoulder.position, transform.right) >= 0f
                ? _leftShoulder.position
                : _rightShoulder.position;
        }

        private static Vector3 AxisVector(int axis)
        {
            var v = Vector3.zero;
            v[axis] = 1f;
            return v;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }
    }
}
