using ModApi.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Drood2
{
    /// <summary>
    /// The drop challenge's panel at the top of the screen, in the score's green: the running
    /// timer during a jump, the score after the landing. Built once per flight scene from XML
    /// with the game's own UI system (IUserInterface.BuildUserInterfaceFromXml, on the scene's
    /// main canvas, so it hides with the game's interface), in the game's flight-panel style.
    /// Shown and filled by <see cref="DroodDropZone"/>; the text only changes when its value does.
    /// </summary>
    internal static class DroodDropChallengeHud
    {
        private const string Green = "#2E9E4A";
        private const string LightGreen = "#9FDDB2";

        // Below the top edge (UI units), clear of the game's own top panels.
        private const int TopOffset = 70;

        private static readonly string Layout =
@"<XmlLayout xmlns=""http://www.w3schools.com"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:noNamespaceSchemaLocation=""..\..\XmlLayout.xsd"">
  <Include path=""Ui/Xml/Flight/FlightStyles.xml"" />
  <VerticalLayout id=""drop-panel"" class=""flight-panel"" width=""340"" rectAlignment=""UpperCenter"" offsetXY=""0 -" + TopOffset + @""" contentSizeFitter=""vertical"" childForceExpandHeight=""false"" childAlignment=""UpperCenter"" padding=""12 12 6 8"" spacing=""0"" raycastTarget=""false"">
    <TextMeshPro id=""drop-title"" text=""Drop Challenge"" fontSize=""14"" color=""" + LightGreen + @""" alignment=""Center"" preferredHeight=""20"" raycastTarget=""false"" />
    <TextMeshPro id=""drop-main"" text=""0:00"" fontSize=""34"" fontStyle=""Bold"" color=""" + Green + @""" alignment=""Center"" preferredHeight=""42"" raycastTarget=""false"" />
    <TextMeshPro id=""drop-detail"" text="""" fontSize=""14"" color=""" + LightGreen + @""" alignment=""Center"" preferredHeight=""20"" raycastTarget=""false"" />
    <TextMeshPro id=""drop-best"" text="""" fontSize=""14"" fontStyle=""Bold"" color=""" + Green + @""" alignment=""Center"" preferredHeight=""20"" raycastTarget=""false"" />
  </VerticalLayout>
</XmlLayout>";

        private static HudScript _script;
        private static GameObject _panel;
        private static TextMeshProUGUI _title;
        private static TextMeshProUGUI _main;
        private static TextMeshProUGUI _detail;
        private static TextMeshProUGUI _best;
        private static bool _building;
        private static bool _failed;
        private static int _shownSeconds = -1;
        private static string _shownPrompt;

        // The right 270° turn's symbol (a looped arrow, drawn in code), left of the main line,
        // IconSize (UI units) high, IconGap from the text; the text and it centred together.
        private const float IconSize = 26f;
        private const float IconGap = 8f;
        private const int IconTextureSize = 128;
        private static RawImage _icon;
        private static Texture2D _iconTexture;

        /// <summary>The running timer (whole seconds), with <paramref name="prompt"/> under it.</summary>
        internal static void ShowTimer(float seconds, string prompt)
        {
            if (!Ready())
            {
                return;
            }

            var whole = Mathf.FloorToInt(seconds);
            if (whole == _shownSeconds && ReferenceEquals(prompt, _shownPrompt) && _panel.activeSelf)
            {
                return;
            }

            _shownSeconds = whole;
            _shownPrompt = prompt;
            _title.text = "Drop Challenge";
            _main.text = Clock(whole);
            SetLine(_detail, prompt);
            SetLine(_best, string.Empty);
            Show();
        }

        /// <summary>The score after a landing, with its parts and the personal best.</summary>
        internal static void ShowScore(int total, float distance, int grade, float seconds, string best)
        {
            if (!Ready())
            {
                return;
            }

            _shownSeconds = -1;
            _title.text = "Drop Challenge Score";
            _main.text = $"{total}<size=60%> / 1000</size>";
            SetLine(_detail, $"{distance:0.00} m  ·  grade {grade}  ·  {Clock(Mathf.FloorToInt(seconds))}");
            SetLine(_best, best);
            Show();
        }

        /// <summary>A jump that scores nothing, and why.</summary>
        internal static void ShowNoScore(string reason)
        {
            if (!Ready())
            {
                return;
            }

            _shownSeconds = -1;
            _title.text = "Drop Challenge";
            _main.text = "No score";
            SetLine(_detail, reason);
            SetLine(_best, string.Empty);
            Show();
        }

        /// <summary>
        /// Any challenge's lines (the swoop course's), the main one with the right 270° turn's
        /// symbol before it when <paramref name="turnIcon"/>.
        /// </summary>
        internal static void ShowLines(string title, string main, string detail, string best, bool turnIcon = false)
        {
            if (!Ready())
            {
                return;
            }

            _shownSeconds = -1;
            _title.text = title;
            _main.margin = turnIcon ? new Vector4(IconSize + IconGap, 0f, 0f, 0f) : Vector4.zero;
            _main.text = main;
            SetLine(_detail, detail);
            SetLine(_best, best);
            Show();
            PlaceIcon(turnIcon);
        }

        /// <summary>The symbol shown just left of the main line's text, or hidden.</summary>
        private static void PlaceIcon(bool shown)
        {
            if (!shown)
            {
                if (_icon != null && _icon.gameObject.activeSelf)
                {
                    _icon.gameObject.SetActive(false);
                }

                return;
            }

            if (_icon == null)
            {
                var icon = new GameObject("Drood2.TurnIcon", typeof(RectTransform), typeof(RawImage));
                icon.layer = _main.gameObject.layer;
                var rect = (RectTransform)icon.transform;
                rect.SetParent(_main.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(IconSize, IconSize);
                _icon = icon.GetComponent<RawImage>();
                _icon.texture = TurnIconTexture();
                _icon.color = _main.color;
                _icon.raycastTarget = false;
            }

            if (!_icon.gameObject.activeSelf)
            {
                _icon.gameObject.SetActive(true);
            }

            // Just left of the text as laid out now (its bounds in the line's own space).
            _main.ForceMeshUpdate();
            var bounds = _main.textBounds;
            var line = (RectTransform)_main.transform;
            var centre = line.rect.center;
            ((RectTransform)_icon.transform).anchoredPosition = new Vector2(bounds.min.x - IconGap - centre.x, bounds.center.y - centre.y);
        }

        /// <summary>
        /// A looped arrow for a right 270° turn (as on the road sign): a stem up the left, then
        /// round to the right over the top and down, ending in an arrowhead pointing left at the
        /// bottom. White on clear (tinted by the image), antialiased, built once.
        /// </summary>
        private static Texture2D TurnIconTexture()
        {
            if (_iconTexture != null)
            {
                return _iconTexture;
            }

            // In the texture's -1..1 square.
            const float radius = 0.55f;
            const float stroke = 0.15f;
            var centre = new Vector2(0.12f, 0.05f);
            var stemTop = new Vector2(centre.x - radius, centre.y);
            var stemBottom = new Vector2(centre.x - radius, centre.y - 0.55f);
            var arcEnd = new Vector2(centre.x, centre.y - radius);
            var tip = new Vector2(-0.2f, arcEnd.y - 0.04f);
            var baseUp = new Vector2(arcEnd.x + 0.12f, arcEnd.y + 0.3f);
            var baseDown = new Vector2(arcEnd.x + 0.12f, arcEnd.y - 0.3f);
            var pixel = 2f / IconTextureSize;
            var pixels = new Color32[IconTextureSize * IconTextureSize];
            for (var y = 0; y < IconTextureSize; y++)
            {
                for (var x = 0; x < IconTextureSize; x++)
                {
                    var p = new Vector2((x + 0.5f) * pixel - 1f, (y + 0.5f) * pixel - 1f);

                    // The stem, and the arc from the left over the top round to the bottom
                    // (all but its lower left quarter).
                    var distance = DistanceToSegment(p, stemBottom, stemTop);
                    var fromCentre = p - centre;
                    var inArc = !(fromCentre.x < 0f && fromCentre.y < 0f);
                    var toArc = inArc
                        ? Mathf.Abs(fromCentre.magnitude - radius)
                        : Mathf.Min(Vector2.Distance(p, stemTop), Vector2.Distance(p, arcEnd));
                    distance = Mathf.Min(distance, toArc) - stroke;

                    // The arrowhead: a triangle (negative inside).
                    distance = Mathf.Min(distance, TriangleDistance(p, tip, baseUp, baseDown));
                    var alpha = Mathf.Clamp01(0.5f - distance / pixel);
                    pixels[y * IconTextureSize + x] = new Color32(255, 255, 255, (byte)(255f * alpha));
                }
            }

            _iconTexture = new Texture2D(IconTextureSize, IconTextureSize, TextureFormat.RGBA32, false)
            {
                name = "Drood2.TurnIcon",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _iconTexture.SetPixels32(pixels);
            _iconTexture.Apply(false, true);
            return _iconTexture;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Signed distance to a triangle: negative inside, the distance to its edge outside.</summary>
        private static float TriangleDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var edge = Mathf.Min(DistanceToSegment(p, a, b), Mathf.Min(DistanceToSegment(p, b, c), DistanceToSegment(p, c, a)));
            var d1 = Cross(b - a, p - a);
            var d2 = Cross(c - b, p - b);
            var d3 = Cross(a - c, p - c);
            var inside = (d1 >= 0f && d2 >= 0f && d3 >= 0f) || (d1 <= 0f && d2 <= 0f && d3 <= 0f);
            return inside ? -edge : edge;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        internal static void Hide()
        {
            if (_panel != null && _panel.activeSelf)
            {
                _panel.SetActive(false);
            }

            _shownSeconds = -1;
        }

        internal static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

        private static void Show()
        {
            if (!_panel.activeSelf)
            {
                _panel.SetActive(true);
            }
        }

        private static void SetLine(TextMeshProUGUI line, string text)
        {
            line.text = text;
            var shown = !string.IsNullOrEmpty(text);
            if (line.gameObject.activeSelf != shown)
            {
                line.gameObject.SetActive(shown);
            }
        }

        /// <summary>The panel exists (built on first use in each flight scene).</summary>
        private static bool Ready()
        {
            if (_script != null && _panel != null && _title != null && _main != null && _detail != null && _best != null)
            {
                return true;
            }

            if (_building || _failed)
            {
                return false;
            }

            _building = true;
            try
            {
                Assets.Scripts.Game.Instance.UserInterface.BuildUserInterfaceFromXml<HudScript>(Layout, "Drood2.DropChallenge", (script, controller) =>
                {
                    _script = script;
                    var layout = controller.XmlLayout;

                    // Every level from the layout up to its canvas covers the whole canvas, and
                    // the panel is pinned to its top centre (built inside small boxes in the
                    // middle, it showed in the middle of the screen).
                    var levels = new System.Text.StringBuilder();
                    for (var level = layout.GameObject != null ? layout.GameObject.transform : null; level != null; level = level.parent)
                    {
                        if (level.GetComponent<Canvas>() != null)
                        {
                            levels.Append($" canvas '{level.name}' {(level as RectTransform)?.rect.size}");
                            break;
                        }

                        if (level is RectTransform stretch)
                        {
                            levels.Append($" '{level.name}' {stretch.rect.size} ->");
                            stretch.anchorMin = Vector2.zero;
                            stretch.anchorMax = Vector2.one;
                            stretch.offsetMin = Vector2.zero;
                            stretch.offsetMax = Vector2.zero;
                        }
                    }

                    DroodLog.Debug($"Drop challenge panel built; levels up to the canvas:{levels}");

                    _panel = layout.GetElementById("drop-panel")?.GameObject;
                    _title = layout.GetElementById<TextMeshProUGUI>("drop-title");
                    _main = layout.GetElementById<TextMeshProUGUI>("drop-main");
                    _detail = layout.GetElementById<TextMeshProUGUI>("drop-detail");
                    _best = layout.GetElementById<TextMeshProUGUI>("drop-best");
                    _building = false;
                    if (_panel == null || _title == null || _main == null || _detail == null || _best == null)
                    {
                        _failed = true;
                        Debug.LogWarning("[Drood 2.0] The drop challenge panel's layout is missing an element; no panel this flight.");
                        return;
                    }

                    if (_panel.transform is RectTransform pinned)
                    {
                        pinned.anchorMin = new Vector2(0.5f, 1f);
                        pinned.anchorMax = new Vector2(0.5f, 1f);
                        pinned.pivot = new Vector2(0.5f, 1f);
                        pinned.anchoredPosition = new Vector2(0f, -TopOffset);
                    }

                    _panel.SetActive(false);
                }, null);
            }
            catch (System.Exception e)
            {
                _building = false;
                _failed = true;
                Debug.LogWarning($"[Drood 2.0] Couldn't build the drop challenge panel: {e.Message}");
            }

            return false;
        }

        /// <summary>The flight scene ends: the panel goes with its canvas; built anew in the next.</summary>
        internal static void ForgetFlightScene()
        {
            _script = null;
            _panel = null;
            _title = null;
            _main = null;
            _detail = null;
            _best = null;
            _icon = null;
            _building = false;
            _failed = false;
            _shownSeconds = -1;
        }

        // The layout's root script (the game's UI system needs one; it handles no events).
        internal sealed class HudScript : MonoBehaviour
        {
        }
    }
}
