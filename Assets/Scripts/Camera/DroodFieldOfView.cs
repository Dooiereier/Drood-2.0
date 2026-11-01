using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// The first-person field of view from the mod setting (Settings → Mods → Drood 2.0 →
    /// First-Person Field of View). The drood's eyes are its CameraVantage, whose field of view is
    /// a private value from the part XML (70); it is set from the setting whenever they differ
    /// (<see cref="DroodReflection"/>). The game reads it only when first person is selected
    /// (FirstPersonCameraController.OnSelected), so in first person the camera's own field of view
    /// is set too: a change applies at once.
    /// </summary>
    public class DroodFieldOfView : MonoBehaviour
    {
        private CameraVantageData _vantage;

        /// <summary>Called from the modifier's game-loop Update.</summary>
        internal void Step()
        {
            var settings = Assets.Scripts.ModSettings.Instance;
            if (settings?.FirstPersonFieldOfView == null)
            {
                return;
            }

            var eva = GetComponent<EvaScript>();
            if (_vantage == null)
            {
                _vantage = eva?.PartScript?.GetModifier<CameraVantageScript>()?.Data;
                if (_vantage == null)
                {
                    return;
                }
            }

            var wanted = settings.FirstPersonFieldOfView.Value;
            if (!Mathf.Approximately(_vantage.FieldOfView, wanted))
            {
                DroodReflection.SetFieldOfView(_vantage, wanted);
                if (eva != null && eva.IsPlayerCraft && eva.IsFpsActive)
                {
                    var camera = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera;
                    if (camera != null)
                    {
                        camera.FieldOfView = wanted;
                    }
                }

                DroodLog.Debug($"First-person field of view {wanted:0}°{(eva != null && eva.IsFpsActive ? " (applied to the camera now)" : string.Empty)}.");
            }
        }
    }
}
