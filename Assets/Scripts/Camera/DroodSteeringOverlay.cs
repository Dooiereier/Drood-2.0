using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Drood2
{
    /// <summary>
    /// The mouse steering's zones on screen (<see cref="DroodMouseSteering"/>), in any view, only
    /// as AG4 chooses (<see cref="DroodSteeringDisplay"/>: full, the dead band only, off; it
    /// doesn't come up by itself). While skydiving or under a canopy a thin
    /// box around the screen's middle, always shown: the dead band (no input inside it). Under the
    /// ram-air also an outer box (full input on and beyond it), labelled up and down ("front
    /// risers", "toggles"), and beside them two bars with the left and right lines' pull (toggles
    /// down from the middle, the flare's deeper pull on below them, front risers up), a mark
    /// where a slow line stalls; these at Bright while the mouse moves or a pull changes, fading
    /// to Faint after IdleSeconds. The game's own cursor is the stick. Built with the game's UI
    /// system on its canvas (hides with the game's interface), once per flight scene.
    /// </summary>
    internal static class DroodSteeringOverlay
    {
        private const float Bright = 0.85f;
        private const float Faint = 0.25f;
        private const float IdleSeconds = 3f;
        private const float FadeSeconds = 0.5f;
        private const float LineWidth = 2f;
        private const float BarWidth = 6f;
        private const float BarGap = 24f;
        private const float MarkLength = 14f;

        private static readonly Color Dead = new Color(0.8f, 0.8f, 0.8f, 0.5f);
        private static readonly Color Full = new Color(0.9f, 0.9f, 0.9f, 0.9f);
        private static readonly Color BarBack = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color Brakes = new Color(0.95f, 0.75f, 0.2f, 0.9f);
        private static readonly Color Front = new Color(0.35f, 0.7f, 1f, 0.9f);
        private static readonly Color Stall = new Color(0.9f, 0.2f, 0.15f, 0.9f);
        private static readonly Color Flare = new Color(1f, 0.45f, 0.1f, 0.9f);

        // The flare's depth beyond full brakes: the bar goes on below the full pull, at most
        // FlareBarShare of the brakes' length.
        private const float FlareBarShare = 0.5f;

        private const string Layout =
@"<XmlLayout xmlns=""http://www.w3schools.com"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:noNamespaceSchemaLocation=""..\..\XmlLayout.xsd"">
  <Include path=""Ui/Xml/Flight/FlightStyles.xml"" />
  <Panel id=""steer-panel"" color=""rgba(0,0,0,0)"" raycastTarget=""false"">
    <TextMeshPro id=""steer-up"" text="""" fontSize=""13"" color=""#D0D0D0"" alignment=""Center"" width=""160"" height=""18"" raycastTarget=""false"" />
    <TextMeshPro id=""steer-down"" text="""" fontSize=""13"" color=""#D0D0D0"" alignment=""Center"" width=""160"" height=""18"" raycastTarget=""false"" />
  </Panel>
</XmlLayout>";

        private static GameObject _panel;
        private static RectTransform _area;
        private static RectTransform _fading;
        private static CanvasGroup _group;
        private static TextMeshProUGUI _upLabel;
        private static TextMeshProUGUI _downLabel;
        private static RectTransform[] _deadBox;
        private static RectTransform[] _fullBox;
        private static RectTransform[] _barBacks;
        private static RectTransform[] _barDown;
        private static RectTransform[] _barUp;
        private static RectTransform[] _barFlare;
        private static RectTransform[] _stallMarks;
        private static bool _building;
        private static bool _failed;

        private static Vector2 _laidOutFor;
        private static float _laidOutZones = -1f;
        private static float _barScale;
        private static float _barUpScale;
        private static Vector3 _lastMouse;
        private static float _lastLeft;
        private static float _lastRight;
        private static float _activeAt = float.NegativeInfinity;

        /// <summary>Each frame (the player's Drood 2.0): shown while it applies, else hidden.</summary>
        internal static void Step(DroodParachute parachute, Assets.Scripts.Craft.Parts.Modifiers.Eva.EvaScript eva, DroodFreeFall freeFall)
        {
            // Every Drood 2.0 steps this; only the one flown decides (another one would hide it).
            if (eva != null && !eva.IsPlayerCraft)
            {
                return;
            }

            DroodSteeringDisplay.Step(eva);
            var show = parachute != null && eva != null && eva.IsPlayerCraft
                && DroodSteeringDisplay.On(eva) && (parachute.IsHanging || (freeFall != null && freeFall.Weight > 0f));
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
                _activeAt = Time.unscaledTime;
            }

            LayOut();
            var canopy = parachute.IsHanging && parachute.IsRamAirChosen && !DroodSteeringDisplay.DeadbandOnly;
            if (_fading.gameObject.activeSelf != canopy)
            {
                _fading.gameObject.SetActive(canopy);
                _activeAt = Time.unscaledTime;
            }

            if (!canopy)
            {
                return;
            }

            var left = parachute.LeftPull;
            var right = parachute.RightPull;
            var frontLeft = parachute.FrontPullOf(-1);
            var frontRight = parachute.FrontPullOf(1);
            var mouse = Input.mousePosition;
            if ((mouse - _lastMouse).sqrMagnitude > 4f || Mathf.Abs(left - _lastLeft) > 0.01f || Mathf.Abs(right - _lastRight) > 0.01f)
            {
                _activeAt = Time.unscaledTime;
            }

            _lastMouse = mouse;
            _lastLeft = left;
            _lastRight = right;
            SetBar(_barDown[0], left, true);
            SetBar(_barDown[1], right, true);
            SetBar(_barUp[0], frontLeft, false);
            SetBar(_barUp[1], frontRight, false);
            SetFlare(0, left, parachute.FlareOf(-1));
            SetFlare(1, right, parachute.FlareOf(1));

            var target = Time.unscaledTime - _activeAt < IdleSeconds ? Bright : Faint;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime / FadeSeconds);
        }

        /// <summary>The flight scene ends: the overlay goes with its canvas; built anew in the next.</summary>
        internal static void ForgetFlightScene()
        {
            _panel = null;
            _area = null;
            _group = null;
            _fading = null;
            _upLabel = null;
            _downLabel = null;
            _building = false;
            _failed = false;
            _laidOutZones = -1f;
        }

        /// <summary>The boxes and bars placed for the screen's size and the player's dead bands (only when they change).</summary>
        private static void LayOut()
        {
            DroodMouseSteering.Zones(DroodParachute.MouseForwardDeadband, out var side, out var up, out var down, out var upIsBack);
            var size = _area.rect.size;
            var zones = side * 7f + up * 13f + down * 17f + (upIsBack ? 1f : 0f);
            if (size == _laidOutFor && Mathf.Approximately(zones, _laidOutZones))
            {
                return;
            }

            _laidOutFor = size;
            _laidOutZones = zones;
            var half = size * 0.5f;
            Box(_deadBox, side * half.x, up * half.y, down * half.y);
            var sideFull = DroodMouseSteering.FullAt(side) * half.x;
            var upFull = DroodMouseSteering.FullAt(up) * half.y;
            var downFull = DroodMouseSteering.FullAt(down) * half.y;
            Box(_fullBox, sideFull, upFull, downFull);
            _upLabel.text = upIsBack ? "toggles" : "front risers";
            _downLabel.text = upIsBack ? "front risers" : "toggles";
            ((RectTransform)_upLabel.transform).anchoredPosition = new Vector2(0f, upFull + 14f);
            ((RectTransform)_downLabel.transform).anchoredPosition = new Vector2(0f, -downFull - 14f);

            // The bars beside the outer box: the toggles' pull down from the middle (to the
            // box's bottom at a full pull), the front risers' up (to its top).
            _barScale = downFull;
            _barUpScale = upFull;
            for (var i = 0; i < 2; i++)
            {
                var x = (i == 0 ? -1f : 1f) * (sideFull + BarGap);
                var flareRoom = FlareBarShare * downFull;
                Place(_barBacks[i], new Vector2(x, (upFull - downFull - flareRoom) * 0.5f), new Vector2(BarWidth, upFull + downFull + flareRoom));
                Place(_barFlare[i], new Vector2(x, -downFull), new Vector2(BarWidth, 0f));
                _barFlare[i].pivot = new Vector2(0.5f, 1f);
                Place(_barDown[i], new Vector2(x, 0f), new Vector2(BarWidth, 0f));
                Place(_barUp[i], new Vector2(x, 0f), new Vector2(BarWidth, 0f));
                _barUp[i].pivot = new Vector2(0.5f, 0f);
                _barDown[i].pivot = new Vector2(0.5f, 1f);
                Place(_stallMarks[i], new Vector2(x, -DroodParachute.StallPullShare * downFull), new Vector2(MarkLength, LineWidth));
            }
        }

        /// <summary>The flare's part of one side's bar, below its toggle pull.</summary>
        private static void SetFlare(int side, float pull, float flare)
        {
            var bar = _barFlare[side];
            var top = -Mathf.Clamp01(pull) * _barScale;
            var length = Mathf.Clamp01(flare) * FlareBarShare * _barScale;
            if (Mathf.Abs(bar.anchoredPosition.y - top) > 0.5f || Mathf.Abs(bar.sizeDelta.y - length) > 0.5f)
            {
                bar.anchoredPosition = new Vector2(bar.anchoredPosition.x, top);
                bar.sizeDelta = new Vector2(BarWidth, length);
            }
        }

        private static void SetBar(RectTransform bar, float pull, bool down)
        {
            var length = Mathf.Clamp01(pull) * (down ? _barScale : _barUpScale);
            if (Mathf.Abs(bar.sizeDelta.y - length) > 0.5f)
            {
                bar.sizeDelta = new Vector2(BarWidth, length);
            }
        }

        /// <summary>A box of four lines around the middle: <paramref name="x"/> to each side, <paramref name="up"/> above, <paramref name="down"/> below.</summary>
        private static void Box(RectTransform[] lines, float x, float up, float down)
        {
            Place(lines[0], new Vector2(0f, up), new Vector2(2f * x + LineWidth, LineWidth));
            Place(lines[1], new Vector2(0f, -down), new Vector2(2f * x + LineWidth, LineWidth));
            Place(lines[2], new Vector2(-x, (up - down) * 0.5f), new Vector2(LineWidth, up + down));
            Place(lines[3], new Vector2(x, (up - down) * 0.5f), new Vector2(LineWidth, up + down));
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static RectTransform Piece(string name, Color color)
        {
            var parent = _fading != null ? _fading : _area;
            var piece = new GameObject(name, typeof(RectTransform), typeof(Image));
            piece.layer = _panel.layer;
            var rect = (RectTransform)piece.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            var image = piece.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform[] Pieces(string name, Color color, int count)
        {
            var pieces = new RectTransform[count];
            for (var i = 0; i < count; i++)
            {
                pieces[i] = Piece(name, color);
            }

            return pieces;
        }

        /// <summary>The overlay exists (built on first use in each flight scene).</summary>
        private static bool Ready()
        {
            if (_panel != null && _area != null && _fading != null && _group != null)
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
                Assets.Scripts.Game.Instance.UserInterface.BuildUserInterfaceFromXml<OverlayScript>(Layout, "Drood2.SteeringOverlay", (script, controller) =>
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

                    _panel = layout.GetElementById("steer-panel")?.GameObject;
                    _upLabel = layout.GetElementById<TextMeshProUGUI>("steer-up");
                    _downLabel = layout.GetElementById<TextMeshProUGUI>("steer-down");
                    if (_panel == null || _upLabel == null || _downLabel == null)
                    {
                        _failed = true;
                        _panel = null;
                        Debug.LogWarning("[Drood 2.0] The steering overlay's layout is missing an element; no overlay this flight.");
                        return;
                    }

                    _area = (RectTransform)_panel.transform;
                    _area.anchorMin = Vector2.zero;
                    _area.anchorMax = Vector2.one;
                    _area.offsetMin = Vector2.zero;
                    _area.offsetMax = Vector2.zero;
                    _deadBox = Pieces("Drood2.SteerDeadBand", Dead, 4);

                    // What fades, together: a layer over the whole panel.
                    var fading = new GameObject("Drood2.SteerFading", typeof(RectTransform), typeof(CanvasGroup));
                    fading.layer = _panel.layer;
                    _fading = (RectTransform)fading.transform;
                    _fading.SetParent(_area, false);
                    _fading.anchorMin = Vector2.zero;
                    _fading.anchorMax = Vector2.one;
                    _fading.offsetMin = Vector2.zero;
                    _fading.offsetMax = Vector2.zero;
                    _group = fading.GetComponent<CanvasGroup>();
                    _group.interactable = false;
                    _group.blocksRaycasts = false;
                    _group.alpha = Bright;
                    foreach (var label in new[] { _upLabel, _downLabel })
                    {
                        var rect = (RectTransform)label.transform;
                        rect.SetParent(_fading, false);
                        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                    }

                    _fullBox = Pieces("Drood2.SteerFull", Full, 4);
                    _barBacks = Pieces("Drood2.SteerBar", BarBack, 2);
                    _barDown = Pieces("Drood2.SteerBrakes", Brakes, 2);
                    _barUp = Pieces("Drood2.SteerFront", Front, 2);
                    _barFlare = Pieces("Drood2.SteerFlare", Flare, 2);
                    _stallMarks = Pieces("Drood2.SteerStall", Stall, 2);
                    _laidOutZones = -1f;
                    _panel.SetActive(false);
                    DroodLog.Debug("Steering overlay built.");
                }, null);
            }
            catch (System.Exception e)
            {
                _building = false;
                _failed = true;
                Debug.LogWarning($"[Drood 2.0] Couldn't build the steering overlay: {e.Message}");
            }

            return false;
        }

        /// <summary>The layout's script (the game's UI system needs one; nothing in it).</summary>
        internal sealed class OverlayScript : MonoBehaviour
        {
        }
    }
}
