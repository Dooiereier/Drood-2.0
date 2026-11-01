using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using System.Collections;
using ModApi.Craft.Parts;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// While translation mode is on in weightlessness (off the ground, out of the water and
    /// the atmosphere), Drood 2.0's first-person view (its CameraVantage) pitches up. It eases
    /// back when translation mode is switched off or the drood is no longer weightless; on the
    /// ground and in the atmosphere translation mode leaves the view alone.
    ///
    /// Off the ground the game turns the body to the first-person camera's whole orientation
    /// each physics step (EvaScript.UpdateKinematicTurning), so a pitched camera would pitch the
    /// body, the view with it, over and over. The pitch is therefore taken off the game camera
    /// again once the frame is drawn (UnpitchAfterFrame), before the next physics step reads it;
    /// the camera controller puts it back from CameraPosition in its next Update.
    ///
    /// The vantage's rotation offset is only applied once, at flight start, to its private
    /// "CameraPosition" transform. FirstPersonCameraController rebuilds the camera rotation
    /// from that transform every frame (mouse look on top), so this component rotates it
    /// directly.
    ///
    /// Keyboard: the translation toggle lives in the "FlightCraft" map, which the game
    /// disables for droods, so the player's binding for it (T by default) is read here via
    /// <see cref="DroodKeys"/>, skipped if the game's own toggle fired, and ignored under the
    /// canopy. The nav panel's translation button works too.
    /// </summary>
    public class DroodLookUp : MonoBehaviour
    {
        // The translation-mode key when the game has no keyboard binding for it.
        private static readonly KeyCode[] LookUpKeys = { KeyCode.T };

        /// <summary>
        /// Degrees the view pitches up in translation mode. Short of 90°: the game's first-person
        /// camera turns the view's forward into a rotation about the drood's up
        /// (Quaternion.LookRotation), which spins wildly as the two become parallel.
        /// </summary>
        public const float LookUpAngle = 80f;

        /// <summary>Easing rate per second; 4 ≈ a quarter-second transition.</summary>
        private const float Response = 4f;

        /// <summary>Air density (kg/m³) below which the drood counts as out of the atmosphere.</summary>
        private const float NoAirDensity = 1e-4f;

        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();

        private EvaScript _eva;
        private IPartScript _part;
        private CameraVantageScript _vantage;
        private DroodParachute _parachute;
        private Transform _cameraPosition;
        private Quaternion _baseRotation;
        private float _currentAngle;
        private float _drawnPitch;

        /// <summary>Current look-up pitch in degrees (eases toward <see cref="LookUpAngle"/>).</summary>
        public float CurrentAngle => _currentAngle;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _part = _eva.PartScript;
        }

        private void OnEnable()
        {
            StartCoroutine(UnpitchAfterFrame());
        }

        /// <summary>Called from the modifier's game-loop Update.</summary>
        internal void Step()
        {
            // Added after this component (Drood2FlightBehaviours), so looked up here.
            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            var hanging = _parachute != null && _parachute.IsHanging;
            HandleKeyboardToggle(hanging);

            // The vantage creates CameraPosition in its FlightStart, which may run after this
            // component is added, so look it up lazily.
            if (_cameraPosition == null && !TryFindCameraPosition())
            {
                return;
            }

            var controls = _part?.CommandPod?.Controls;
            // Not under the canopy: there the mouse looks around freely (FreeLookUnderCanopyPatch).
            var target = controls != null && controls.TranslationModeEnabled && _eva.EvaActive && !hanging && Weightless()
                ? LookUpAngle
                : 0f;
            _currentAngle = Mathf.Lerp(_currentAngle, target, 1f - Mathf.Exp(-Response * Time.unscaledDeltaTime));

            // Negative pitch about the vantage's local X is up.
            _cameraPosition.localRotation = _baseRotation * Quaternion.Euler(-_currentAngle, 0f, 0f);
            _drawnPitch = _eva.IsPlayerCraft && _eva.IsFpsActive ? _currentAngle : 0f;
        }

        // Off the ground, out of the water and the atmosphere.
        private bool Weightless()
        {
            var craft = _part?.CraftScript?.FlightData;
            return !_eva.IsGrounded && !_eva.IsInWater && craft != null && craft.AtmosphereSample.AirDensity < NoAirDensity;
        }

        // After each frame is drawn: the look-up's pitch off the game camera (see the class
        // comment), so the body never follows it.
        private IEnumerator UnpitchAfterFrame()
        {
            while (true)
            {
                yield return EndOfFrame;
                if (_drawnPitch <= 0f)
                {
                    continue;
                }

                var camera = Assets.Scripts.Game.Instance?.FlightScene?.ViewManager?.GameView?.GameCamera?.Transform;
                if (camera != null)
                {
                    camera.rotation *= Quaternion.Euler(_drawnPitch, 0f, 0f);
                }

                _drawnPitch = 0f;
            }
        }

        private void OnDestroy()
        {
            if (_cameraPosition != null)
            {
                _cameraPosition.localRotation = _baseRotation;
            }
        }

        private bool TryFindCameraPosition()
        {
            _vantage = _vantage != null ? _vantage : _part?.GetModifier<CameraVantageScript>();
            if (_vantage == null)
            {
                return false;
            }

            _cameraPosition = _vantage.CameraPosition;
            if (_cameraPosition == null)
            {
                return false;
            }

            _baseRotation = _cameraPosition.localRotation;
            return true;
        }

        // The player's translation-mode binding (DroodKeys), not under the canopy: there the
        // look-up is off, and toggling the hidden state would snap the view up on landing.
        private void HandleKeyboardToggle(bool hanging)
        {
            var controls = _part?.CommandPod?.Controls;
            var inputs = Assets.Scripts.Game.Instance?.Inputs;
            if (hanging || controls == null || !DroodKeys.CanUseKeys(_eva)
                || !DroodKeys.BindingDown(inputs?.ToggleTranslationMode, LookUpKeys)
                || inputs?.ToggleTranslationMode?.GetButtonDownIfEnabled() == true)
            {
                return;
            }

            controls.ToggleTranslationMode();
        }
    }
}
