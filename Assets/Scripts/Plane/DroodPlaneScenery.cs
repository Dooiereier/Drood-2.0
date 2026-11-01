using System.Collections.Generic;
using Assets.Scripts.Craft;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using ModApi.Craft.Parts;
using ModApi.Flight.Sim;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The jump plane as scenery. Built once it is wanted (<see cref="Want"/>), outside flight, where
    /// none of its parts can join the flight scene: the craft file bundled with the mod
    /// (<see cref="CraftPath"/>, under ModData → Other Assets) is built the way the menu builds its craft preview
    /// (Game.CraftLoader.LoadCraftImmediate, then CraftBuilder.CreateCraftScript without body
    /// scripts, so without physics); its renderers are copied into a template that outlives the
    /// scenes, and the built craft is destroyed. <see cref="DroodPlaneRide"/> flies copies of it.
    /// The log says what the build held and what was copied.
    /// </summary>
    internal static class DroodPlaneScenery
    {
        // The jump plane: a Cessna Caravan by ExcaliburAerospace (SimpleRockets.com), credited in
        // the README and the mod's description; its craft file, as a text asset.
        private const string CraftPath = "Assets/Crafts/JumpPlane.xml";


        private static GameObject _template;
        private static bool _built;


        // Set once a Drood 2.0 has had the Drop Zone Challenge: from then on (this and later
        // sessions) the plane is built in the first scene outside flight, also for a save loaded
        // straight from the menu. A first such flight, with no scene before it, starts without.
        private const string WantedKey = "Drood20.PlaneWanted";
        private static bool _wanted;

        /// <summary>A Drood 2.0 has the Drop Zone Challenge: the plane is built now, or in the next scene outside flight.</summary>
        internal static void Want()
        {
            if (!_wanted)
            {
                _wanted = true;
                PlayerPrefs.SetInt(WantedKey, 1);
                PlayerPrefs.Save();
            }

            // Between the game loop's steps: its parts register in the game loop being run.
            if (!_built && !_buildAsked && !Assets.Scripts.Game.InFlightScene)
            {
                _buildAsked = true;
                var host = new GameObject("Drood2.PlaneBuildAsked");
                Object.DontDestroyOnLoad(host);
                host.AddComponent<Runner>().StartCoroutine(BuildNextFrame(host));
            }
        }

        private static bool _buildAsked;

        private static System.Collections.IEnumerator BuildNextFrame(GameObject host)
        {
            yield return null;
            _buildAsked = false;
            BuildOutsideFlight();
            Object.Destroy(host);
        }

        /// <summary>From the mod's scene load: built if wanted earlier.</summary>
        internal static void OnSceneLoaded()
        {
            _wanted = _wanted || PlayerPrefs.GetInt(WantedKey, 0) != 0;
            if (_wanted)
            {
                BuildOutsideFlight();
            }
        }

        private static void BuildOutsideFlight()
        {
            if (_built || Assets.Scripts.Game.InFlightScene)
            {
                return;
            }

            _built = true;
            GameObject built = null;
            try
            {
                var file = Assets.Scripts.Mod.Instance?.ResourceLoader?.LoadAsset<TextAsset>(CraftPath);
                if (file == null)
                {
                    Debug.LogWarning($"[Drood 2.0] The jump plane's craft file ({CraftPath}) is missing from the mod; no plane.");
                    return;
                }

                var data = Assets.Scripts.Game.Instance.CraftLoader.LoadCraftImmediate(System.Xml.Linq.XElement.Parse(file.text.TrimStart('\uFEFF')));
                if (data == null)
                {
                    Debug.LogWarning("[Drood 2.0] The jump plane's craft file didn't load; no plane.");
                    return;
                }

                var craft = CraftBuilder.CreateCraftScript(data, false, false);
                built = craft != null ? craft.gameObject : null;
                if (built == null)
                {
                    Debug.LogWarning("[Drood 2.0] Jump plane: the craft builder returned nothing.");
                    return;
                }

                // Out of sight while its parts settle (part styles, doors, a seated pilot's pose)
                // over the next frames; animated off screen too (as the game does for a pilot,
                // EvaScript.SetAnimatorAlwaysAnimate), or the pilot stays in its T-pose.
                built.transform.position = OutOfSight;
                foreach (var animator in built.GetComponentsInChildren<Animator>(true))
                {
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                foreach (var skinned in built.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skinned.updateWhenOffscreen = true;
                }

                // A seated pilot gets its seat's animation (the game loads it only in flight; in
                // this build the pilot isn't linked to its seat, so a seat's).
                string seatPath = null;
                foreach (var compartment in built.GetComponentsInChildren<Assets.Scripts.Craft.Parts.Modifiers.Eva.CrewCompartmentScript>(true))
                {
                    if (!string.IsNullOrEmpty(compartment.CrewLoadedAnimationControllerPath))
                    {
                        seatPath = compartment.CrewLoadedAnimationControllerPath;
                        break;
                    }
                }

                foreach (var pilot in built.GetComponentsInChildren<EvaScript>(true))
                {
                    var path = pilot.CrewCompartment?.CrewLoadedAnimationControllerPath;
                    path = string.IsNullOrEmpty(path) ? seatPath : path;
                    var controller = string.IsNullOrEmpty(path) ? null : Resources.Load<RuntimeAnimatorController>(path);
                    var animator = pilot.GetComponentInChildren<Animator>(true);
                    if (controller != null && animator != null)
                    {
                        animator.runtimeAnimatorController = controller;
                    }

                }
                var host = new GameObject("Drood2.PlaneBuild");
                Object.DontDestroyOnLoad(host);
                host.AddComponent<Runner>().StartCoroutine(CopyAfterSettling(built, host));
                built = null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Jump plane: build failed: {e}");
            }
            finally
            {
                if (built != null)
                {
                    built.SetActive(false);
                    Object.Destroy(built);
                }
            }
        }

        // Where the built craft waits, and for how many frames.
        private static readonly Vector3 OutOfSight = new Vector3(0f, -10000f, 0f);
        private const int SettleFrames = 5;

        private static System.Collections.IEnumerator CopyAfterSettling(GameObject built, GameObject host)
        {
            for (var i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }

            try
            {
                if (built != null)
                {
                    FindEngineSound(built);
                    built.transform.position = Vector3.zero;
                    _template = Copy(built);
                }
                else
                {
                    // Built again when next wanted outside flight.
                    _built = false;
                    Debug.LogWarning("[Drood 2.0] Jump plane: the build was gone before it was copied (scene change); building again later.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Jump plane: copy failed: {e}");
            }
            finally
            {
                if (built != null)
                {
                    built.SetActive(false);
                    Object.Destroy(built);
                }

                Object.Destroy(host);
            }
        }

        // A host for the coroutine.
        private class Runner : MonoBehaviour
        {
        }

        /// <summary>
        /// The visible part meshes (a seated pilot's body baked in its pose) in a template that outlives the scene, coloured by baking:
        /// each vertex keeps its material slot (0..4) in the last digit of the whole part of uv0.w
        /// (a rover wheel's are 11..13 and 20; the game's part
        /// shader looks the colour up from data only the flight craft's part groups supply), so
        /// each mesh is copied with its triangles grouped by slot into submeshes, each drawn with
        /// a Standard material made from that slot's theme material (PartData.GetMaterial):
        /// colour, metallic, smoothness, emission; glass with a transparent shader.
        /// Labels, attach points, depth masks, effects, and fully transparent
        /// materials (invisible in the game, used to hide helper parts) are left out.
        /// </summary>
        private static GameObject Copy(GameObject built)
        {
            var template = new GameObject("Drood2.PlaneTemplate");
            template.SetActive(false);
            Object.DontDestroyOnLoad(template);
            var root = built.transform;
            var standard = Shader.Find("Standard");
            if (standard == null)
            {
                Debug.LogWarning("[Drood 2.0] Jump plane: no Standard shader.");
                return template;
            }

            var baked = new Dictionary<PartMaterial, Material>();
            var uvs = new List<Vector4>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var bySlot = new Dictionary<int, List<int>>();
            var bakedBodies = new List<Mesh>();
            int copied = 0, skipped = 0, labels = 0;
            foreach (var renderer in built.GetComponentsInChildren<Renderer>(false))
            {
                // A skinned body (a seated drood or tourist, the pilot) is baked in its pose.
                var skinned = renderer as SkinnedMeshRenderer;
                Mesh source = null;
                var toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                if (skinned != null)
                {
                    if (skinned.sharedMesh != null)
                    {
                        source = new Mesh();
                        skinned.BakeMesh(source, true);
                        bakedBodies.Add(source);
                        toRoot = root.worldToLocalMatrix * Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                    }
                }
                else if (renderer is MeshRenderer)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    source = filter != null ? filter.sharedMesh : null;
                }

                var shaderName = renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : string.Empty;
                var part = renderer.GetComponentInParent<Assets.Scripts.Craft.Parts.PartScript>();
                var partType = part?.Data?.PartType?.Id;
                if (shaderName.Contains("TMPro") && renderer.enabled && source != null && source.isReadable && part?.Data != null
                    && CopyLabel(renderer, source, part, template.transform, toRoot))
                {
                    labels++;
                    continue;
                }

                if (!renderer.enabled || source == null || !source.isReadable || part?.Data == null
                    || !IsSurface(shaderName) || shaderName.Contains("TMPro")
                    || partType == Drood2Part.PartTypeId)
                {
                    skipped++;
                    continue;
                }

                source.GetUVs(0, uvs);
                source.GetVertices(vertices);
                source.GetNormals(normals);
                if (uvs.Count != vertices.Count)
                {
                    skipped++;
                    continue;
                }

                foreach (var list in bySlot.Values)
                {
                    list.Clear();
                }

                for (var sub = 0; sub < source.subMeshCount; sub++)
                {
                    var indices = source.GetTriangles(sub);
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var slot = Mathf.Clamp(Mathf.FloorToInt(uvs[indices[i]].w) % 10, 0, 4);
                        if (!bySlot.TryGetValue(slot, out var list))
                        {
                            list = new List<int>();
                            bySlot[slot] = list;
                        }

                        list.Add(indices[i]);
                        list.Add(indices[i + 1]);
                        list.Add(indices[i + 2]);
                    }
                }

                var mesh = new Mesh { name = source.name };
                mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                if (normals.Count == vertices.Count)
                {
                    mesh.SetNormals(normals);
                }

                var materials = new List<Material>();
                var subMeshes = new List<List<int>>();
                foreach (var pair in bySlot)
                {
                    if (pair.Value.Count == 0)
                    {
                        continue;
                    }

                    var partMaterial = part.Data.GetMaterial((PartMeshMaterialLevel)pair.Key);
                    if (partMaterial == null || partMaterial.TransparencyStrength >= InvisibleTransparency)
                    {
                        continue;
                    }

                    if (!baked.TryGetValue(partMaterial, out var material))
                    {
                        material = Bake(standard, partMaterial);
                        baked[partMaterial] = material;
                    }

                    materials.Add(material);
                    subMeshes.Add(pair.Value);
                }

                if (subMeshes.Count == 0)
                {
                    Object.Destroy(mesh);
                    skipped++;
                    continue;
                }

                mesh.subMeshCount = subMeshes.Count;
                for (var i = 0; i < subMeshes.Count; i++)
                {
                    mesh.SetTriangles(subMeshes[i], i);
                }

                mesh.RecalculateBounds();
                if (normals.Count != vertices.Count)
                {
                    mesh.RecalculateNormals();
                }

                var copy = new GameObject(renderer.name);
                var parent = PropellerPivot(part, template.transform, root);
                copy.transform.SetParent(parent, false);
                var toParent = parent == template.transform ? toRoot : Matrix4x4.TRS(parent.localPosition, parent.localRotation, Vector3.one).inverse * toRoot;
                copy.transform.localPosition = toParent.GetColumn(3);
                copy.transform.localRotation = toParent.rotation;
                copy.transform.localScale = toParent.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                var copiedRenderer = copy.AddComponent<MeshRenderer>();
                copiedRenderer.sharedMaterials = materials.ToArray();
                copiedRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                copiedRenderer.receiveShadows = true;
                copied++;
            }

            foreach (var propeller in template.GetComponentsInChildren<DroodPlanePropeller>(true))
            {
                propeller.FindAxis();
            }

            foreach (var body in bakedBodies)
            {
                Object.Destroy(body);
            }

            var unsupported = 0;
            foreach (var pair in baked)
            {
                unsupported += pair.Value.shader.isSupported ? 0 : 1;
            }

            DroodLog.Debug($"Jump plane built: {copied} meshes, {labels} labels, {baked.Count} materials ({unsupported} with an unsupported shader), {skipped} renderers left out; labels with {(_text != null ? _text.name : "no shader")}.");
            return template;
        }

        // Part renderers that aren't the part's surface: the designer's attach points, depth
        // masks, effects. Every other part renderer is copied, whatever its shader: another mod
        // may replace the game's part shader (Enhanced Nozzle Glow's "EnhancedNozzleGlow/PartShader").
        private static readonly string[] NotSurfaces = { "AttachPoint", "DepthMask", "Particles" };

        private static bool IsSurface(string shaderName)
        {
            foreach (var not in NotSurfaces)
            {
                if (shaderName.Contains(not))
                {
                    return false;
                }
            }

            return shaderName.Length > 0;
        }

        /// <summary>
        /// The parent for a part's copied meshes: the template, or for a propeller its pivot (at
        /// the part, turned by <see cref="DroodPlanePropeller"/>), made on first use.
        /// </summary>
        private static Transform PropellerPivot(Assets.Scripts.Craft.Parts.PartScript part, Transform template, Transform root)
        {
            if (part.GetComponentInChildren<Assets.Scripts.Craft.Parts.Modifiers.PropellerAssemblyScript>(true) == null)
            {
                return template;
            }

            var name = "Drood2.Propeller " + part.Data.Id;
            var existing = template.Find(name);
            if (existing != null)
            {
                return existing;
            }

            var pivot = new GameObject(name).transform;
            pivot.SetParent(template, false);
            var toRoot = root.worldToLocalMatrix * part.transform.localToWorldMatrix;
            pivot.localPosition = toRoot.GetColumn(3);
            pivot.localRotation = toRoot.rotation;
            pivot.gameObject.AddComponent<DroodPlanePropeller>();
            return pivot;
        }

        // Text (a label's) with the first of these the game includes: its own text shader takes
        // its colour from part data like the part shader (black when copied); these take the
        // face colour from the material. The lit one first: shaded like the fuselage, and the only
        // one with a depth test in the world (the others' ZTest is unity_GUIZTestMode, a global for
        // screen UI: always on top). Seen at large angles in the shade its letters still draw as black
        // boxes (cause not found; accepted). No perspective filter, and only the side facing the view drawn.
        private static readonly string[] TextShaders = { "TextMeshPro/Distance Field (Surface)", "TextMeshPro/Distance Field", "TextMeshPro/Mobile/Distance Field" };
        private static Shader _text;
        private static bool _textLooked;

        /// <summary>
        /// A label's text mesh copied with its material on a plain text shader, the face in the
        /// label's first material (false: no text shader in the game).
        /// </summary>
        private static bool CopyLabel(Renderer renderer, Mesh source, Assets.Scripts.Craft.Parts.PartScript part, Transform template, Matrix4x4 toRoot)
        {
            if (!_textLooked)
            {
                _textLooked = true;
                foreach (var name in TextShaders)
                {
                    var shader = Shader.Find(name);
                    if (_text == null && shader != null && shader.isSupported)
                    {
                        _text = shader;
                    }
                }
            }

            if (_text == null || renderer.sharedMaterial == null)
            {
                return false;
            }

            var material = new Material(renderer.sharedMaterial) { shader = _text, name = "Drood2.Plane label" };
            material.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Back);
            material.SetFloat("_PerspectiveFilter", 0f);
            var face = part.Data.GetMaterial(PartMeshMaterialLevel.Primary);
            if (face != null && material.HasProperty("_FaceColor"))
            {
                var colour = face.Color;
                colour.a = 1f;
                material.SetColor("_FaceColor", colour);
            }

            var copy = new GameObject(renderer.name);
            copy.transform.SetParent(template, false);
            copy.transform.localPosition = toRoot.GetColumn(3);
            copy.transform.localRotation = toRoot.rotation;
            copy.transform.localScale = toRoot.lossyScale;
            // The game's text shader ignores the vertex colours (the part data lives elsewhere);
            // the plain one multiplies the face by them: white.
            var mesh = Object.Instantiate(source);
            var white = new Color32[mesh.vertexCount];
            for (var i = 0; i < white.Length; i++)
            {
                white[i] = new Color32(255, 255, 255, 255);
            }

            mesh.colors32 = white;
            copy.AddComponent<MeshFilter>().sharedMesh = mesh;
            var copied = copy.AddComponent<MeshRenderer>();
            copied.sharedMaterial = material;
            copied.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return true;
        }

        // A theme material this transparent is invisible in the game.
        private const float InvisibleTransparency = 0.999f;

        // Glass alpha: the opaque share (1 - transparency) times GlassAlphaShare, at least
        // MinGlassAlpha: the only transparent shader (Sprites/Default) is unlit and draws both
        // faces, so a pane's layers add up.
        private const float GlassAlphaShare = 0.35f;
        private const float MinGlassAlpha = 0.04f;

        // Shaders for glass, the first the game includes: Standard's own fade mode is a variant
        // the game doesn't ship (the glass drew solid), these are transparent in themselves.
        private static readonly string[] GlassShaders = { "Legacy Shaders/Transparent/Specular", "Legacy Shaders/Transparent/Diffuse", "Sprites/Default" };
        private static Shader _glass;
        private static bool _glassLooked;

        /// <summary>A material with a theme material's look: Standard, or a transparent shader for glass.</summary>
        private static Material Bake(Shader standard, PartMaterial partMaterial)
        {
            // Chosen by eye, not the game's values: its part shader gets the theme colour made
            // linear (Theme.RefreshMaterial) and lights it its own way. Standard, given the plain
            // colour, gets those same linear values but came out too dark (the benches, #460000,
            // black). Given in gamma, it gets the theme's raw values: lighter than the game's.
            var colour = QualitySettings.activeColorSpace == ColorSpace.Linear ? partMaterial.Color.gamma : partMaterial.Color;
            if (partMaterial.TransparencyStrength > 0f)
            {
                if (!_glassLooked)
                {
                    _glassLooked = true;
                    foreach (var name in GlassShaders)
                    {
                        var shader = Shader.Find(name);
                        if (_glass == null && shader != null && shader.isSupported)
                        {
                            _glass = shader;
                        }
                    }
                }

                if (_glass != null)
                {
                    colour.a = Mathf.Max(MinGlassAlpha, (1f - partMaterial.TransparencyStrength) * GlassAlphaShare);
                    var glass = new Material(_glass) { name = "Drood2.Plane " + partMaterial.Name, color = colour };
                    if (glass.HasProperty("_Shininess"))
                    {
                        glass.SetFloat("_Shininess", Mathf.Lerp(0.03f, 1f, Mathf.Clamp01(partMaterial.Smoothness)));
                    }

                    glass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    return glass;
                }
            }

            var material = new Material(standard) { name = "Drood2.Plane " + partMaterial.Name, color = colour };
            material.SetFloat("_Metallic", Mathf.Clamp01(partMaterial.Metallic));
            material.SetFloat("_Glossiness", Mathf.Clamp01(partMaterial.Smoothness));
            if (partMaterial.EmissionStrength > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", colour * partMaterial.EmissionStrength);
            }

            return material;
        }

        /// <summary>The plane is built.</summary>
        internal static bool Ready => _template != null;

        /// <summary>The plane's engine sound (its propeller's own loop, else a motor's or engine's clip), or null.</summary>
        internal static AudioClip EngineClip { get; private set; }

        /// <summary>The engine loop three times as high (and as quick), as long and as seamless, or null.</summary>
        internal static AudioClip EngineHighClip { get; private set; }

        /// <summary>The engine sound's propeller blade count (the game pitches its loop by it), 0 for a motor's or engine's clip.</summary>
        internal static int EngineBlades { get; private set; }

        /// <summary>
        /// The sounds the built plane carries (logged); the propeller's kept as its engine sound
        /// (the game raises its volume with the throttle in flight: built, it's at 0), else a
        /// motor's or engine's.
        /// </summary>
        private static void FindEngineSound(GameObject built)
        {
            AudioSource engine = null;
            var engineRank = 0;
            EngineBlades = 0;
            foreach (var source in built.GetComponentsInChildren<AudioSource>(true))
            {
                var partType = source.GetComponentInParent<Assets.Scripts.Craft.Parts.PartScript>()?.Data?.PartType?.Id ?? "?";
                var rank = partType.Contains("Propeller") ? 2 : partType.Contains("Motor") || partType.Contains("Engine") ? 1 : 0;
                if (source.clip != null && source.loop && rank > engineRank)
                {
                    engine = source;
                    engineRank = rank;
                    var propeller = source.GetComponentInParent<Assets.Scripts.Craft.Parts.PartScript>()?.Data?.GetModifier<Assets.Scripts.Craft.Parts.Modifiers.PropellerAssemblyData>();
                    EngineBlades = propeller != null ? propeller.BladeCount : 0;
                }
            }

            EngineClip = engine != null ? EvenOut(engine.clip) : null;
            EngineHighClip = EngineClip != null ? ThreeTimesHigher(EngineClip) : null;
        }

        // The engine loop evened out (EvenOut): its loudness measured over EvenWindowSeconds of
        // the clip (played ~16-20 times faster: ~30 ms heard, longer than the blade beat, which
        // stays) and brought to the clip's average, peaks above PeakLimit times the average
        // rounded off; never raised more than MaxGain.
        private const float EvenWindowSeconds = 0.5f;
        private const float PeakLimit = 2f;
        private const float MaxGain = 4f;

        /// <summary>
        /// The engine loop with an even loudness and no spikes (see EvenWindowSeconds), as a new
        /// clip, still seamless (the windows wrap around); the clip itself if it can't be read.
        /// </summary>
        private static AudioClip EvenOut(AudioClip clip)
        {
            var raw = new float[clip.samples * clip.channels];
            if (clip.samples <= 0 || !clip.GetData(raw, 0))
            {
                DroodLog.Debug($"Jump plane: the engine clip '{clip.name}' can't be read; played as it is.");
                return clip;
            }

            var count = clip.samples;
            var mono = new float[count];
            var total = 0.0;
            for (var i = 0; i < count; i++)
            {
                var sum = 0f;
                for (var c = 0; c < clip.channels; c++)
                {
                    sum += raw[i * clip.channels + c];
                }

                mono[i] = sum / clip.channels;
                total += mono[i] * mono[i];
            }

            var average = (float)System.Math.Sqrt(total / count);
            if (average <= 1e-6f)
            {
                return clip;
            }

            // The loudness around each sample: a running sum of squares over the window, wrapping.
            var half = Mathf.Clamp(Mathf.RoundToInt(EvenWindowSeconds * clip.frequency * 0.5f), 1, count / 2);
            var window = 0.0;
            for (var k = -half; k <= half; k++)
            {
                var v = mono[(k % count + count) % count];
                window += v * v;
            }

            var evened = new float[count];
            var limit = PeakLimit * average;
            var evenedTotal = 0.0;
            for (var i = 0; i < count; i++)
            {
                var local = (float)System.Math.Sqrt(System.Math.Max(window, 0.0) / (2 * half + 1));
                var gain = Mathf.Min(average / Mathf.Max(local, 1e-6f), MaxGain);
                var y = mono[i] * gain;
                y = limit * (float)System.Math.Tanh(y / limit);
                evened[i] = y;
                evenedTotal += y * y;

                var leaving = mono[((i - half) % count + count) % count];
                var entering = mono[(i + half + 1) % count];
                window += entering * entering - leaving * leaving;
            }

            // The original's loudness.
            var scale = average / Mathf.Max((float)System.Math.Sqrt(evenedTotal / count), 1e-6f);
            for (var i = 0; i < count; i++)
            {
                evened[i] *= scale;
            }

            var result = AudioClip.Create(clip.name + " (even)", count, 1, clip.frequency, false);
            result.SetData(evened, 0);
            return result;
        }

        /// <summary>
        /// <paramref name="clip"/> (mono) three times as quick, so three times as high at the same
        /// pitch: every third sample (averaged with its neighbours against aliasing), wrapping
        /// round the loop three times, so it's as long and as seamless.
        /// </summary>
        private static AudioClip ThreeTimesHigher(AudioClip clip)
        {
            var count = clip.samples;
            var source = new float[count * clip.channels];
            if (count <= 0 || clip.channels != 1 || !clip.GetData(source, 0))
            {
                return null;
            }

            var high = new float[count];
            for (var i = 0; i < count; i++)
            {
                var at = (int)(3L * i % count);
                high[i] = (source[(at - 1 + count) % count] + source[at] + source[(at + 1) % count]) / 3f;
            }

            var result = AudioClip.Create(clip.name + " (x3)", count, 1, clip.frequency, false);
            result.SetData(high, 0);
            return result;
        }

        /// <summary>A copy of the plane for this flight scene, on <paramref name="layer"/>, shown.</summary>
        internal static GameObject CreateInstance(int layer)
        {
            if (_template == null)
            {
                return null;
            }

            var instance = Object.Instantiate(_template);
            instance.name = "Drood2.PlaneScenery";
            SetLayer(instance.transform, layer);
            instance.SetActive(true);
            return instance;
        }

        private static void SetLayer(Transform transform, int layer)
        {
            transform.gameObject.layer = layer;
            foreach (Transform child in transform)
            {
                SetLayer(child, layer);
            }
        }
    }
}
