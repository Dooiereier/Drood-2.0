using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;
using UnityEngine.Audio;

namespace Drood2
{
    /// <summary>
    /// Wind noise for the drood you are flying: generated, not recorded (no sound file).
    /// Its loudness follows the dynamic pressure (silent in thin air and on the ground, a soft
    /// rush under the canopy, a roar at terminal speed), its brightness the airspeed, with a
    /// little slow variation. A layer on top of the game's own wind noise, which plays for
    /// every player craft, a drood included (CraftAudio.UpdateWindNoise: a 3D source,
    /// clamp01(speed / 200 · density) · 0.2, about 0.07 at terminal speed); that one is kept.
    ///
    /// Played by Unity's own audio engine, with no script code on the audio thread: two
    /// seamless noise loops built once (a deep rush, and a hiss that is mixed in with speed),
    /// whose volumes are set each frame. (A script audio callback would stutter whenever the
    /// garbage collector stops the script threads.)
    ///
    /// Two routes, both playing while the wind is heard, crossfaded on a camera switch: the
    /// outside voice through the game's sound mixer group (IAudioPlayer.GetGameMixerGroup), so
    /// the game's volumes apply; the helmet voice past it, because in first person the game
    /// low-passes that whole group at 600 Hz (its helmet/cockpit effect, see juno-facts). The
    /// helmet voice plays at the mixer's level (the master and sound volume settings applied
    /// here as the mixer does) through a milder low-pass of its own (HelmetCutoff).
    /// Volume: Settings → Mods → Drood 2.0 → Wind Volume. Stepped from Drood 2.0's game loop;
    /// paused with the game and while its window is not in focus.
    /// </summary>
    public class DroodWindSound : MonoBehaviour
    {
        // Dynamic pressure (Pa) where the wind starts to be heard, and where it is full.
        private const float QuietPressure = 15f;
        private const float FullPressure = 2500f;
        // Loudness curve: (log pressure fraction)^LoudnessPower.
        private const float LoudnessPower = 2.5f;
        // Level (RMS) of the rush and of the hiss at full gain; the hiss comes in from
        // HissFromSpeed to HissFullSpeed (m/s).
        private const float RushLevel = 0.2f;
        private const float HissLevel = 0.09f;
        private const float HissFromSpeed = 10f;
        private const float HissFullSpeed = 55f;
        // The helmet in first person: a low-pass at this cutoff (Hz).
        private const float HelmetCutoff = 2500f;
        // The game mixer's sound group level: 0.15 × the sound volume setting
        // (AudioPlayer.SetSoundVolume), times the master volume.
        private const float SoundMixerScale = 0.15f;
        // Slow variation of the loudness (fraction, Hz): outside and in the helmet.
        private const float OutsideVariation = 0.04f;
        private const float HelmetVariation = 0.08f;
        private const float VariationRate = 0.25f;
        // Volumes follow their targets over about this long (s); a camera switch, RouteSeconds.
        private const float RampSeconds = 0.15f;
        private const float RouteSeconds = 0.03f;
        // The loops: length (s), the part crossfaded into the start (s), their RMS in the clip.
        private const float LoopSeconds = 8f;
        private const float LoopFadeSeconds = 0.5f;
        private const float RushClipRms = 0.35f;
        private const float HissClipRms = 0.3f;

        private static AudioClip _rushClip;
        private static AudioClip _hissClip;

        private EvaScript _eva;
        private DroodLimbFollower _limbs;
        private Voice _outside;
        private Voice _helmet;
        private AudioMixerGroup _gameMixer;
        private float _gain;
        private float _hiss;
        private float _route;
        private float _noiseSeed;
        private bool _focused = true;
        private bool _paused;
        private bool _logged;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _gameMixer = Assets.Scripts.Game.Instance?.AudioPlayer?.GetGameMixerGroup();
            BuildClips();
            _outside = new Voice(transform, "DroodWindOutside", _gameMixer, 0f);
            _helmet = new Voice(transform, "DroodWindHelmet", null, HelmetCutoff);
            _noiseSeed = Random.value * 100f;
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
                _outside.SetPaused(paused);
                _helmet.SetPaused(paused);
            }

            if (paused)
            {
                return;
            }

            var target = TargetGain(out var speed);
            var dt = Time.unscaledDeltaTime;
            var ramp = 1f - Mathf.Exp(-dt / RampSeconds);
            _gain = Mathf.Lerp(_gain, target, ramp);
            _hiss = Mathf.Lerp(_hiss, Mathf.InverseLerp(HissFromSpeed, HissFullSpeed, speed), ramp);
            var firstPerson = _eva != null && _eva.IsFpsActive && _gameMixer != null;
            _route = Mathf.MoveTowards(_route, firstPerson ? 1f : 0f, dt / RouteSeconds);

            // The helmet voice at the level the game mixer would give it.
            var audio = Assets.Scripts.Game.Instance?.Settings?.Game?.Audio;
            var master = audio != null ? audio.MasterVolume.Value : 1f;
            var sound = audio != null ? audio.SoundVolume.Value : 1f;
            var swell = Mathf.PerlinNoise(Time.unscaledTime * VariationRate, _noiseSeed) * 2f - 1f;
            _outside.Set(_gain * (1f - _route) * (1f + OutsideVariation * swell), _hiss, 1f);
            _helmet.Set(_gain * _route * (1f + HelmetVariation * swell), _hiss, Mathf.Clamp01(master * sound * SoundMixerScale));

            if (!_logged && target > 0f)
            {
                _logged = true;
                DroodLog.Debug($"Wind sound: game mixer group {(_gameMixer != null ? "found" : "missing")}.");
            }
        }

        /// <summary>The wind's gain now (0 = silent), with the airspeed.</summary>
        private float TargetGain(out float speed)
        {
            speed = 0f;
            var part = _eva != null ? _eva.PartScript : null;
            var craft = part?.CraftScript;
            // Attached to something larger (anchored) the drood flies with that craft, whose own
            // wind noise plays; each drood aboard would add its layer, in a cabin too.
            _limbs = _limbs != null ? _limbs : GetComponent<DroodLimbFollower>();
            if (craft == null || !_eva.IsPlayerCraft || !_eva.EvaActive || _eva.IsGrounded || _eva.IsInWater || (_limbs != null && _limbs.Anchored))
            {
                return 0f;
            }

            var setting = Assets.Scripts.ModSettings.Instance?.WindVolume;
            var volume = setting != null ? setting.Value : 1f;
            var density = (float)craft.AtmosphereSample.AirDensity;
            speed = ((Vector3)craft.SurfaceVelocity).magnitude;
            var pressure = 0.5f * density * speed * speed;
            if (pressure <= QuietPressure || volume <= 0f)
            {
                return 0f;
            }

            var loudness = Mathf.Clamp01(Mathf.Log(pressure / QuietPressure) / Mathf.Log(FullPressure / QuietPressure));
            return Mathf.Pow(loudness, LoudnessPower) * volume;
        }

        /// <summary>Paused at once when the game window loses focus.</summary>
        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            if (!focused && _outside != null && !_paused)
            {
                _paused = true;
                _outside.SetPaused(true);
                _helmet.SetPaused(true);
            }
        }

        private void OnDestroy()
        {
            _outside?.Destroy();
            _helmet?.Destroy();
        }

        /// <summary>The two noise loops, built once (rebuilt if Unity unloaded them).</summary>
        private static void BuildClips()
        {
            if (_rushClip != null && _hissClip != null)
            {
                return;
            }

            var rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var random = new System.Random(11);
            var length = (int)(LoopSeconds * rate);
            var fade = (int)(LoopFadeSeconds * rate);
            var rush = new float[(length + fade) * 2];
            var hiss = new float[(length + fade) * 2];
            var lowA = 1f - Mathf.Exp(-2f * Mathf.PI * 500f / rate);
            var rumbleA = 1f - Mathf.Exp(-2f * Mathf.PI * 70f / rate);
            var hissLowA = 1f - Mathf.Exp(-2f * Mathf.PI * 1500f / rate);
            var hissTopA = 1f - Mathf.Exp(-2f * Mathf.PI * 6000f / rate);
            var low1 = new float[2];
            var low2 = new float[2];
            var hissLow = new float[2];
            var hissTop = new float[2];
            var rumble = 0f;
            for (var i = 0; i < length + fade; i++)
            {
                // Partly the same noise in both ears: wide, but not two separate sounds.
                var common = (float)random.NextDouble() * 2f - 1f;
                rumble += (common - rumble) * rumbleA;
                for (var c = 0; c < 2; c++)
                {
                    var input = 0.6f * common + 0.4f * ((float)random.NextDouble() * 2f - 1f);

                    // The rush: deep, two-pole low-passed noise with a little rumble under it.
                    low1[c] += (input - low1[c]) * lowA;
                    low2[c] += (low1[c] - low2[c]) * lowA;
                    rush[i * 2 + c] = low2[c] + 0.3f * rumble;

                    // The hiss: the noise above ~1.5 kHz, softened above ~6 kHz.
                    hissLow[c] += (input - hissLow[c]) * hissLowA;
                    hissTop[c] += (input - hissLow[c] - hissTop[c]) * hissTopA;
                    hiss[i * 2 + c] = hissTop[c];
                }
            }

            _rushClip = MakeLoop("DroodWindRush", rush, length, fade, rate, RushClipRms);
            _hissClip = MakeLoop("DroodWindHiss", hiss, length, fade, rate, HissClipRms);
        }

        /// <summary>
        /// A seamless stereo loop of <paramref name="length"/> frames: the extra
        /// <paramref name="fade"/> frames at the end are crossfaded into the start (equal power:
        /// the two ends are unrelated noise), scaled to <paramref name="rms"/>.
        /// </summary>
        private static AudioClip MakeLoop(string name, float[] samples, int length, int fade, int rate, float rms)
        {
            var loop = new float[length * 2];
            System.Array.Copy(samples, loop, loop.Length);
            for (var i = 0; i < fade; i++)
            {
                var t = (i + 0.5f) / fade;
                for (var c = 0; c < 2; c++)
                {
                    loop[i * 2 + c] = samples[i * 2 + c] * Mathf.Sqrt(t) + samples[(length + i) * 2 + c] * Mathf.Sqrt(1f - t);
                }
            }

            var sum = 0.0;
            foreach (var v in loop)
            {
                sum += v * v;
            }

            var scale = rms / Mathf.Max((float)System.Math.Sqrt(sum / loop.Length), 1e-9f);
            for (var i = 0; i < loop.Length; i++)
            {
                loop[i] = Mathf.Clamp(loop[i] * scale, -1f, 1f);
            }

            var clip = AudioClip.Create(name, length, 2, rate, false);
            clip.SetData(loop, 0);
            return clip;
        }

        /// <summary>
        /// One route: the rush and the hiss loops on that route, each playing while audible.
        /// </summary>
        private sealed class Voice
        {
            private readonly AudioSource _rush;
            private readonly AudioSource _hiss;
            private bool _paused;

            public Voice(Transform parent, string name, AudioMixerGroup mixer, float lowPass)
            {
                _rush = CreateSource(parent, name + "Rush", _rushClip, mixer, lowPass);
                _hiss = CreateSource(parent, name + "Hiss", _hissClip, mixer, lowPass);
            }

            /// <summary>
            /// The wind's <paramref name="gain"/> on this route, the hiss share 0..1 and the
            /// route's level.
            /// </summary>
            public void Set(float gain, float hiss, float level)
            {
                SetVolume(_rush, gain * RushLevel / RushClipRms * level);
                SetVolume(_hiss, gain * hiss * HissLevel / HissClipRms * level);
            }

            public void SetPaused(bool paused)
            {
                _paused = paused;
                Pause(_rush, paused);
                Pause(_hiss, paused);
            }

            public void Destroy()
            {
                if (_rush != null)
                {
                    Object.Destroy(_rush.gameObject);
                }

                if (_hiss != null)
                {
                    Object.Destroy(_hiss.gameObject);
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

            private void SetVolume(AudioSource source, float volume)
            {
                source.volume = Mathf.Clamp01(volume);
                // Only while its object is active (not while the drood sits in a craft).
                if (volume > 1e-4f && !source.isPlaying && !_paused && source.isActiveAndEnabled)
                {
                    // A random point in the loop, so the sources don't repeat each other.
                    source.timeSamples = Random.Range(0, source.clip.samples);
                    source.Play();
                }
                else if (volume <= 1e-4f && source.isPlaying)
                {
                    source.Stop();
                }
            }

            private static AudioSource CreateSource(Transform parent, string name, AudioClip clip, AudioMixerGroup mixer, float lowPass)
            {
                var child = new GameObject(name);
                child.transform.SetParent(parent, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                source.priority = 64;
                source.volume = 0f;
                source.clip = clip;
                source.outputAudioMixerGroup = mixer;
                if (lowPass > 0f)
                {
                    var filter = child.AddComponent<AudioLowPassFilter>();
                    filter.cutoffFrequency = lowPass;
                    filter.lowpassResonanceQ = 1f;
                }

                return source;
            }
        }
    }
}
