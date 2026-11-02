using UnityEngine;
using UnityEngine.UI;

namespace Drood2
{
    /// <summary>
    /// A height cue for the flare, without numbers: a colour strip right of the screen's middle
    /// while the drood you are flying hangs under the canopy with the steering display on (AG4,
    /// <see cref="DroodSteeringDisplay"/>: both its steps), its feet below TopHeight above the
    /// ground (fading in over FadeHeight above that). A moving tape: a fixed white mark across
    /// the middle of a window, and behind it the colour scale sliding down as the drood sinks, so
    /// the height now is the colour at the mark (a square beside it shows that colour too). The
    /// scale runs from the ground to TopHeight over TapeLength, on a square-root scale so the last
    /// metres take most of it (and stream past fastest); its colours are bands of the feet's
    /// height: green above ReadyHeight, yellow down to FlareHeight (get ready), orange down to
    /// FinishHeight (flare), red below (finish it), green going on above TopHeight; the ground is a
    /// black line with dark grey below it, so touchdown is seen coming to the mark. Built with the game's UI system on its canvas
    /// (hides with the game's interface), once per flight scene.
    /// </summary>
    internal static class DroodFlareIndicator
    {
        private const float TopHeight = 15f;
        private const float FadeHeight = 3f;
        private const float ReadyHeight = 6f;
        private const float FlareHeight = 3f;
        private const float FinishHeight = 1f;
        // Each band blends into the next over this many metres around their border.
        private const float BlendMetres = 0.4f;

        // Placement (canvas units): right of the middle by this share of the half width.
        private const float RightShare = 0.42f;
        private const float StripHeight = 260f;
        private const float StripWidth = 12f;
        private const float TapeLength = 390f;
        private const int Segments = 80;
        private const float MarkWidth = 30f;
        private const float MarkHeight = 3f;
        private const float DotSize = 16f;
        private const float DotGap = 10f;
        private const float Alpha = 0.85f;

        private static readonly Color Green = new Color(0.2f, 0.75f, 0.3f);
        private static readonly Color Yellow = new Color(0.95f, 0.85f, 0.2f);
        private static readonly Color Orange = new Color(1f, 0.5f, 0.1f);
        private static readonly Color Red = new Color(0.9f, 0.15f, 0.12f);
        private static readonly Color BelowGround = new Color(0.18f, 0.18f, 0.18f);
        private static readonly Color GroundLine = Color.black;
        private const float GroundLineHeight = 4f;

        private const string Layout =
@"<XmlLayout xmlns=""http://www.w3schools.com"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:noNamespaceSchemaLocation=""..\..\XmlLayout.xsd"">
  <Include path=""Ui/Xml/Flight/FlightStyles.xml"" />
  <Panel id=""flare-panel"" color=""rgba(0,0,0,0)"" raycastTarget=""false"" />
</XmlLayout>";

        private static GameObject _panel;
        private static RectTransform _area;
        private static RectTransform _strip;
        private static RectTransform _tape;
        private static CanvasGroup _group;
        private static Image _dot;
        private static bool _building;
        private static bool _failed;
        private static float _laidOutWidth = -1f;

        /// <summary>Each frame (the player's Drood 2.0): shown low under the canopy, else hidden.</summary>
        internal static void Step(DroodParachute parachute, Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript eva)
        {
            // Every Drood 2.0 steps this; only the one flown decides.
            if (eva == null || !eva.IsPlayerCraft)
            {
                return;
            }

            var feet = parachute != null ? parachute.FeetAboveGround : float.PositiveInfinity;
            // Only with the steering display on (AG4: full or the dead band only).
            var show = parachute != null && eva.EvaActive && DroodSteeringDisplay.On(eva) && parachute.IsHanging && parachute.UnderChuteInAir
                && feet < TopHeight + FadeHeight;
            if (!show)
            {
                if (_panel != null && _panel.activeSelf)
                {
                    _panel.SetActive(false);
                }

                return;
            }

            if (!Ready())
            {
                return;
            }

            if (!_panel.activeSelf)
            {
                _panel.SetActive(true);
            }

            LayOut();
            _group.alpha = Alpha * Mathf.Clamp01((TopHeight + FadeHeight - feet) / FadeHeight);
            var height = Mathf.Max(feet, 0f);
            _tape.anchoredPosition = new Vector2(0f, StripHeight * 0.5f - Position(height) * TapeLength);
            _dot.color = ColourAt(height);
        }

        /// <summary>The flight scene ends: the cue goes with its canvas; built anew in the next.</summary>
        /// <summary>While the game is paused: hidden (the next Step shows it again if it applies).</summary>
        internal static void HideWhilePaused()
        {
            if (_panel != null && _panel.activeSelf)
            {
                _panel.SetActive(false);
            }
        }

        internal static void ForgetFlightScene()
        {
            _panel = null;
            _area = null;
            _strip = null;
            _tape = null;
            _group = null;
            _dot = null;
            _building = false;
            _failed = false;
            _laidOutWidth = -1f;
        }

        /// <summary>Where a height sits on the strip (0 bottom, 1 top).</summary>
        private static float Position(float height)
        {
            return Mathf.Sqrt(Mathf.Clamp01(height / TopHeight));
        }

        private static Color ColourAt(float height)
        {
            var colour = Blend(Red, Orange, height, FinishHeight);
            colour = Blend(colour, Yellow, height, FlareHeight);
            return Blend(colour, Green, height, ReadyHeight);
        }

        /// <summary><paramref name="above"/> over <paramref name="below"/> from <paramref name="border"/> up, blended over BlendMetres.</summary>
        private static Color Blend(Color below, Color above, float height, float border)
        {
            return Color.Lerp(below, above, Mathf.SmoothStep(0f, 1f, (height - border) / BlendMetres + 0.5f));
        }

        /// <summary>The strip placed for the screen's width (only when it changes).</summary>
        private static void LayOut()
        {
            var width = _area.rect.width;
            if (Mathf.Approximately(width, _laidOutWidth))
            {
                return;
            }

            _laidOutWidth = width;
            _strip.anchoredPosition = new Vector2(width * 0.5f * RightShare, 0f);
        }

        private static RectTransform Piece(string name, Transform parent, Vector2 position, Vector2 size, Color color, Vector2 pivot)
        {
            var piece = new GameObject(name, typeof(RectTransform), typeof(Image));
            piece.layer = _panel.layer;
            var rect = (RectTransform)piece.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = piece.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>The cue exists (built on first use in each flight scene).</summary>
        private static bool Ready()
        {
            if (_panel != null && _strip != null && _group != null)
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
                Assets.Scripts.Game.Instance.UserInterface.BuildUserInterfaceFromXml<IndicatorScript>(Layout, "Drood2.FlareIndicator", (script, controller) =>
                {
                    _building = false;
                    var layout = controller.XmlLayout;

                    // Every level from the layout up to its canvas covers the whole canvas.
                    for (var level = layout.GameObject != null ? layout.GameObject.transform : null; level != null && level.GetComponent<Canvas>() == null; level = level.parent)
                    {
                        if (level is RectTransform stretch)
                        {
                            stretch.anchorMin = Vector2.zero;
                            stretch.anchorMax = Vector2.one;
                            stretch.offsetMin = Vector2.zero;
                            stretch.offsetMax = Vector2.zero;
                        }
                    }

                    _panel = layout.GetElementById("flare-panel")?.GameObject;
                    if (_panel == null)
                    {
                        _failed = true;
                        Debug.LogWarning("[Drood 2.0] The flare cue's layout is missing its panel; no flare cue this flight.");
                        return;
                    }

                    _area = (RectTransform)_panel.transform;
                    _area.anchorMin = Vector2.zero;
                    _area.anchorMax = Vector2.one;
                    _area.offsetMin = Vector2.zero;
                    _area.offsetMax = Vector2.zero;
                    _group = _panel.AddComponent<CanvasGroup>();
                    _group.interactable = false;
                    _group.blocksRaycasts = false;

                    // The cue, centred on the screen's middle; in it the window, showing only its
                    // part of the tape.
                    var strip = new GameObject("Drood2.FlareStrip", typeof(RectTransform));
                    strip.layer = _panel.layer;
                    _strip = (RectTransform)strip.transform;
                    _strip.SetParent(_area, false);
                    _strip.anchorMin = _strip.anchorMax = new Vector2(0.5f, 0.5f);
                    _strip.pivot = new Vector2(0.5f, 0.5f);
                    _strip.sizeDelta = new Vector2(StripWidth, StripHeight);
                    var window = new GameObject("Drood2.FlareWindow", typeof(RectTransform), typeof(RectMask2D));
                    window.layer = _panel.layer;
                    var windowRect = (RectTransform)window.transform;
                    windowRect.SetParent(_strip, false);
                    windowRect.anchorMin = Vector2.zero;
                    windowRect.anchorMax = Vector2.one;
                    windowRect.offsetMin = Vector2.zero;
                    windowRect.offsetMax = Vector2.zero;

                    // The tape: the ground at its pivot, half a window of dark grey below it, the
                    // scale up to TopHeight, half a window of green above; the ground's line on top.
                    var tape = new GameObject("Drood2.FlareTape", typeof(RectTransform));
                    tape.layer = _panel.layer;
                    _tape = (RectTransform)tape.transform;
                    _tape.SetParent(windowRect, false);
                    _tape.anchorMin = _tape.anchorMax = new Vector2(0.5f, 0f);
                    _tape.pivot = new Vector2(0.5f, 0f);
                    _tape.sizeDelta = new Vector2(StripWidth, TapeLength);
                    Piece("Drood2.FlareBelow", _tape, new Vector2(0f, -StripHeight * 0.5f), new Vector2(StripWidth, StripHeight * 0.5f + 0.5f), BelowGround, new Vector2(0.5f, 0f));
                    Piece("Drood2.FlareAbove", _tape, new Vector2(0f, TapeLength), new Vector2(StripWidth, StripHeight * 0.5f), Green, new Vector2(0.5f, 0f));
                    for (var i = 0; i < Segments; i++)
                    {
                        var middle = (i + 0.5f) / Segments;
                        var height = middle * middle * TopHeight;
                        Piece("Drood2.FlareBand", _tape, new Vector2(0f, i * TapeLength / Segments), new Vector2(StripWidth, TapeLength / Segments + 0.5f), ColourAt(height), new Vector2(0.5f, 0f));
                    }

                    Piece("Drood2.FlareGround", _tape, Vector2.zero, new Vector2(StripWidth, GroundLineHeight), GroundLine, new Vector2(0.5f, 0.5f));

                    // The fixed mark across the window's middle, the square of the colour now beside it.
                    Piece("Drood2.FlareMark", _strip, new Vector2(0f, StripHeight * 0.5f), new Vector2(MarkWidth, MarkHeight), Color.white, new Vector2(0.5f, 0.5f));
                    _dot = Piece("Drood2.FlareNow", _strip, new Vector2(-(MarkWidth * 0.5f + DotGap + DotSize * 0.5f), StripHeight * 0.5f), new Vector2(DotSize, DotSize), Green, new Vector2(0.5f, 0.5f)).GetComponent<Image>();
                    _laidOutWidth = -1f;
                    _panel.SetActive(false);
                    DroodLog.Debug("Flare cue built.");
                }, null);
            }
            catch (System.Exception e)
            {
                _building = false;
                _failed = true;
                Debug.LogWarning($"[Drood 2.0] Couldn't build the flare cue: {e.Message}");
            }

            return false;
        }

        /// <summary>The layout's script (the game's UI system needs one; nothing in it).</summary>
        internal sealed class IndicatorScript : MonoBehaviour
        {
        }
    }
}
