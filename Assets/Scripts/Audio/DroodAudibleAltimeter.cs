using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;

namespace Drood2
{
    /// <summary>
    /// An audible altimeter in the helmet of the drood you are flying, with two free-fall
    /// warnings modelled on L&amp;B's audibles (Optima II, ProTrack II manuals): the altitude
    /// warning, one WarningSeconds pulsating alarm (a PulseOn beep every PulsePeriod) when passing
    /// WarningHeight (m above the ground), and the alert, a continuous high-pitched siren for as
    /// long as the drood falls below AlertHeight. Like those, they sound only while falling faster
    /// than TriggerSink (m/s, free fall) and not under an open canopy. The tone is a high A
    /// (A7, ToneHz; those audibles beep between 3 and 4 kHz); the siren warbles around it.
    /// The warning sounds again after climbing RearmMargin back above its height. Off with
    /// Settings → Mods → Drood 2.0 → Audible Altimeter.
    ///
    /// It plays past the game's sound mixer group (in first person the game low-passes that
    /// group at 600 Hz, which would mute a 3.5 kHz beep), at the level the mixer would give it
    /// (the master and sound volume settings, as DroodWindSound's helmet voice). Stepped from
    /// Drood 2.0's game loop; paused with the game and while its window is not in focus.
    /// </summary>
    public class DroodAudibleAltimeter : MonoBehaviour
    {
        internal const float WarningHeight = 900f;
        internal const float AlertHeight = 450f;
        private const float RearmMargin = 100f;
        private const float TriggerSink = 13f;
        private const float ToneHz = 3520f;

        // The warning: pulses of PulseOn seconds every PulsePeriod, for WarningSeconds.
        private const float WarningSeconds = 2f;
        private const float PulseOn = 0.12f;
        private const float PulsePeriod = 0.2f;

        // The siren: the tone swept over SirenSweep (share of ToneHz, around it) and back, SirenRate a second.
        private const float SirenSweep = 0.08f;
        private const float SirenRate = 8f;

        // A piezo's edge: each pulse ramps in and out over this long (s); a little third harmonic.
        private const float EdgeSeconds = 0.003f;
        private const float ThirdHarmonic = 0.15f;
        private const float ClipLevel = 0.8f;

        // The game mixer's sound group level: 0.15 × the sound volume setting, times the master.
        private const float SoundMixerScale = 0.15f;

        private static AudioClip _warningClip;
        private static AudioClip _sirenClip;

        private EvaScript _eva;
        private DroodLimbFollower _limbs;
        private DroodParachute _parachute;
        private AudioSource _warning;
        private AudioSource _siren;
        private bool _warningArmed = true;
        private bool _focused = true;
        private bool _paused;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            BuildClips();
            _warning = CreateSource("DroodAudibleWarning", _warningClip, false);
            _siren = CreateSource("DroodAudibleSiren", _sirenClip, true);
        }

        /// <summary>
        /// Each frame from Drood 2.0's game loop (<see cref="Drood2FlightBehaviours"/>), also
        /// while the game is paused (<paramref name="gamePaused"/>).
        /// </summary>
        internal void Step(bool gamePaused)
        {
            var paused = !_focused || gamePaused;
            if (paused != _paused)
            {
                _paused = paused;
                Pause(_warning, paused);
                Pause(_siren, paused);
            }

            if (paused)
            {
                return;
            }

            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            var craft = _eva != null ? _eva.PartScript?.CraftScript : null;
            var setting = Assets.Scripts.ModSettings.Instance?.AudibleAltimeter;
            // Not while attached to something larger (anchored): a craft descending fast would set
            // off every drood aboard.
            _limbs = _limbs != null ? _limbs : GetComponent<DroodLimbFollower>();
            var flying = craft != null && (setting == null || setting.Value) && _eva.IsPlayerCraft && _eva.EvaActive && !_eva.IsGrounded && !_eva.IsInWater
                && (_limbs == null || !_limbs.Anchored);
            var height = flying ? (float)craft.FlightData.AltitudeAboveGroundLevel : float.PositiveInfinity;
            var falling = flying && -(float)craft.FlightData.VerticalSurfaceVelocity > TriggerSink
                && (_parachute == null || !_parachute.CanopyOut);

            if (height > WarningHeight + RearmMargin)
            {
                _warningArmed = true;
            }

            var level = Level();
            _warning.volume = level;
            _siren.volume = level;
            if (_warningArmed && falling && height <= WarningHeight && height > AlertHeight)
            {
                _warningArmed = false;
                _warning.Play();
                DroodLog.Debug($"Audible altimeter: altitude warning at {height:0} m.");
            }
            else if (height <= WarningHeight)
            {
                _warningArmed = false;
            }

            var alert = falling && height <= AlertHeight;
            if (alert && !_siren.isPlaying && _siren.isActiveAndEnabled)
            {
                _warning.Stop();
                _siren.Play();
                DroodLog.Debug($"Audible altimeter: alert at {height:0} m.");
            }
            else if (!alert && _siren.isPlaying)
            {
                _siren.Stop();
            }
        }

        /// <summary>The level the game mixer would give a sound now.</summary>
        private static float Level()
        {
            var audio = Assets.Scripts.Game.Instance?.Settings?.Game?.Audio;
            var master = audio != null ? audio.MasterVolume.Value : 1f;
            var sound = audio != null ? audio.SoundVolume.Value : 1f;
            return Mathf.Clamp01(master * sound * SoundMixerScale);
        }

        /// <summary>Paused at once when the game window loses focus.</summary>
        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            if (!focused && _warning != null && !_paused)
            {
                _paused = true;
                Pause(_warning, true);
                Pause(_siren, true);
            }
        }

        private void OnDestroy()
        {
            if (_warning != null)
            {
                Destroy(_warning.gameObject);
            }

            if (_siren != null)
            {
                Destroy(_siren.gameObject);
            }
        }

        private static void Pause(AudioSource source, bool paused)
        {
            if (paused)
            {
                source.Pause();
            }
            else
            {
                source.UnPause();
            }
        }

        private AudioSource CreateSource(string name, AudioClip clip, bool loop)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 32;
            source.volume = 0f;
            source.clip = clip;
            return source;
        }

        /// <summary>The warning's pulses and the siren's loop, built once (rebuilt if Unity unloaded them).</summary>
        private static void BuildClips()
        {
            if (_warningClip != null && _sirenClip != null)
            {
                return;
            }

            var rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var edge = EdgeSeconds * rate;

            var warning = new float[(int)(WarningSeconds * rate)];
            for (var i = 0; i < warning.Length; i++)
            {
                var time = (float)i / rate;
                var inPulse = time % PulsePeriod;
                var envelope = inPulse < PulseOn
                    ? Mathf.Clamp01(Mathf.Min(inPulse * rate, (PulseOn - inPulse) * rate) / edge)
                    : 0f;
                warning[i] = envelope * Tone(2f * Mathf.PI * ToneHz * time);
            }

            // Whole sweeps in one second, its mean ToneHz: the phase at the end is whole cycles,
            // so the loop is seamless.
            var siren = new float[rate];
            var phase = 0.0;
            for (var i = 0; i < siren.Length; i++)
            {
                var time = (float)i / rate;
                var frequency = ToneHz * (1f - SirenSweep * 0.5f * Mathf.Cos(2f * Mathf.PI * SirenRate * time));
                siren[i] = Tone((float)phase);
                phase += 2.0 * System.Math.PI * frequency / rate;
            }

            _warningClip = AudioClip.Create("DroodAudibleWarning", warning.Length, 1, rate, false);
            _warningClip.SetData(warning, 0);
            _sirenClip = AudioClip.Create("DroodAudibleSiren", siren.Length, 1, rate, false);
            _sirenClip.SetData(siren, 0);
        }

        private static float Tone(float phase)
        {
            return ClipLevel / (1f + ThirdHarmonic) * (Mathf.Sin(phase) + ThirdHarmonic * Mathf.Sin(3f * phase));
        }
    }
}
