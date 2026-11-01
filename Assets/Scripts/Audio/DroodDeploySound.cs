using Assets.Scripts.Craft.Parts.Modifiers.Eva;
using UnityEngine;
using UnityEngine.Audio;

namespace Drood2
{
    /// <summary>
    /// The canopy opening sound, for the drood you are flying: built once from the game's own
    /// parachute clip (AudioLibrary.Flight.ParachuteDeployed, "Audio/Sounds/Parachute"), so
    /// the mod ships no sound file. Two layers:
    /// 1. the stock clip reshaped: a soft fabric rustle first (the canopy leaving the bag),
    ///    then the clip slower and lower, bright at the start and deep after, roughened (less
    ///    of its regular buzz), held and cut short, with a soft tail;
    /// 2. the stock clip itself, softer, from StockDelay.
    /// Its loudest moment is just above the wind at terminal speed (DeployLevel).
    ///
    /// Stalled, the canopy flaps: a loop (FlapLoopSeconds) built from the same clip, short grains of
    /// its loudest part (FlapGrainMin-FlapGrainMax s long, starting every FlapGapMin-FlapGapMax s,
    /// each at its own strength, the whole rising and falling irregularly every FlapSwellSeconds),
    /// so the fabric flaps on without a start or an end. It plays while the ram-air is stalled
    /// more than StallFlapFrom, at up to StallFlapLevel of the opening's level at a full stall,
    /// fading over FlapFadeSeconds, its pitch drifting by up to StallFlapPitch.
    ///
    /// Routed like the wind (<see cref="DroodWindSound"/>): through the game's sound mixer
    /// group in third person, past it in first person (the game muffles that group at 600 Hz
    /// there) at the mixer's level. Paused with the game, and while its window is not in focus.
    /// </summary>
    public class DroodDeploySound : MonoBehaviour
    {
        // Loudest 50 ms of the sound (RMS): the wind at terminal speed is about 0.18.
        private const float DeployLevel = 0.25f;
        // The reshaped clip: speed (and pitch), the bright-to-deep change (s), bass boost and
        // top cut after it, the rough layer, the hold (rising from HoldFrom, full by HoldTo) and
        // the cut (s), the lead-in rustle and the tail (s, level).
        private const float Speed = 0.85f;
        private const float TiltFrom = 0.08f;
        private const float TiltTo = 0.25f;
        private const float BassGain = 0.9f;
        private const float TopCut = 0.55f;
        private const float Roughness = 0.45f;
        private const float HoldFrom = 0.16f;
        private const float HoldTo = 0.39f;
        private const float HoldGain = 1.3f;
        private const float CutAt = 0.44f;
        private const float CutLength = 0.05f;
        private const float LeadSeconds = 0.12f;
        private const float LeadLevel = 0.09f;
        private const float TailSeconds = 0.18f;
        private const float TailLevel = 0.035f;
        // The stock clip on top: from this long after the start (s), at this level.
        private const float StockDelay = 0.25f;
        private const float StockLevel = 0.6f;
        // The flapping in a stall (see the summary).
        private const float FlapLoopSeconds = 4f;
        private const float FlapGrainMin = 0.035f;
        private const float FlapGrainMax = 0.08f;
        private const float FlapGapMin = 0.04f;
        private const float FlapGapMax = 0.1f;
        private const float FlapGainMin = 0.35f;
        private const float FlapSwellSeconds = 0.25f;
        private const float FlapSwellMin = 0.5f;
        private const float FlapFadeSeconds = 0.3f;
        private const float StallFlapPitch = 0.1f;
        private const float StallFlapFrom = 0.2f;
        private const float StallFlapLevel = 0.6f;
        // The game mixer's sound group level (see DroodWindSound).
        private const float SoundMixerScale = 0.15f;

        private static AudioClip _clip;
        private static AudioClip _flapClip;
        private static bool _built;

        private EvaScript _eva;
        private AudioSource _outside;
        private AudioSource _helmet;
        private AudioSource _flapOutside;
        private AudioSource _flapHelmet;
        private DroodParachute _parachute;
        private float _flapVolume;
        private bool _paused;
        private bool _gamePaused;
        private bool _focused = true;

        private void Awake()
        {
            _eva = GetComponent<EvaScript>();
            _outside = CreateSource("DroodDeploySoundOutside", Assets.Scripts.Game.Instance?.AudioPlayer?.GetGameMixerGroup());
            _helmet = CreateSource("DroodDeploySoundHelmet", null);
            _flapOutside = CreateSource("DroodStallFlapOutside", _outside.outputAudioMixerGroup);
            _flapHelmet = CreateSource("DroodStallFlapHelmet", null);

            // Built now (once per session), not at line stretch in the middle of a jump.
            GetClip();
        }

        /// <summary>
        /// Each frame from Drood 2.0's game loop (<see cref="Drood2FlightBehaviours"/>), also
        /// while the game is paused (<paramref name="gamePaused"/>).
        /// </summary>
        internal void Step(bool gamePaused)
        {
            _gamePaused = gamePaused;
            SetPaused(!_focused || gamePaused);
            if (!_paused)
            {
                StepFlapping();
            }
        }

        /// <summary>The flapping of a stalled canopy: the flap loop, as loud as the stall.</summary>
        private void StepFlapping()
        {
            _parachute = _parachute != null ? _parachute : GetComponent<DroodParachute>();
            var stall = _parachute != null ? _parachute.StallAmount : 0f;
            var target = stall > StallFlapFrom && _eva != null && _eva.IsPlayerCraft
                ? StallFlapLevel * Mathf.InverseLerp(StallFlapFrom, 1f, stall)
                : 0f;
            _flapVolume = Mathf.MoveTowards(_flapVolume, target, Time.unscaledDeltaTime / FlapFadeSeconds);
            if (_flapVolume <= 0f)
            {
                StopFlap(_flapOutside);
                StopFlap(_flapHelmet);
                return;
            }

            var clip = GetFlapClip();
            var source = PrepareSource(_flapOutside, _flapHelmet);
            if (clip == null || source == null)
            {
                return;
            }

            StopFlap(source == _flapOutside ? _flapHelmet : _flapOutside);
            source.volume = _flapVolume * (source == _flapHelmet ? source.volume : 1f);
            source.pitch = 1f + StallFlapPitch * (Mathf.PerlinNoise(Time.time * 0.7f, 3.1f) * 2f - 1f);
            if (!source.isPlaying)
            {
                source.clip = clip;
                source.loop = true;
                source.time = Random.Range(0f, clip.length * 0.9f);
                source.Play();
            }
        }

        private static void StopFlap(AudioSource source)
        {
            if (source != null && source.isPlaying)
            {
                source.Stop();
            }
        }

        /// <summary>
        /// The source to play on: the outside one through the game's mixer, or in first person the
        /// helmet one past it at the mixer's level. Null if it can't play.
        /// </summary>
        private AudioSource PrepareSource(AudioSource outside, AudioSource helmet)
        {
            if (outside == null || helmet == null)
            {
                return null;
            }

            var firstPerson = _eva.IsFpsActive && outside.outputAudioMixerGroup != null;
            var source = firstPerson ? helmet : outside;
            if (firstPerson)
            {
                var audio = Assets.Scripts.Game.Instance?.Settings?.Game?.Audio;
                var master = audio != null ? audio.MasterVolume.Value : 1f;
                var sound = audio != null ? audio.SoundVolume.Value : 1f;
                source.volume = Mathf.Clamp01(master * sound * SoundMixerScale);
            }

            return source.isActiveAndEnabled ? source : null;
        }

        /// <summary>Paused too while the game window is not in focus (see DroodWindSound).</summary>
        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            SetPaused(!focused || _gamePaused);
        }

        private void SetPaused(bool paused)
        {
            if (paused == _paused || _outside == null || _helmet == null)
            {
                return;
            }

            _paused = paused;
            foreach (var source in new[] { _outside, _helmet, _flapOutside, _flapHelmet })
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
        }

        /// <summary>The canopy starts to open: plays the sound if this is the drood you are flying.</summary>
        internal void PlayOpening()
        {
            if (_eva == null || !_eva.IsPlayerCraft || Assets.Scripts.Game.Instance?.FlightScene?.TimeManager?.Paused == true)
            {
                return;
            }

            var clip = GetClip();
            if (clip == null)
            {
                return;
            }

            var source = PrepareSource(_outside, _helmet);
            if (source == null)
            {
                return;
            }

            source.PlayOneShot(clip);
            DroodLog.Debug($"Opening sound ({(source == _helmet ? "first person" : "outside")}).");
        }

        private AudioSource CreateSource(string name, AudioMixerGroup mixer)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 64;
            source.outputAudioMixerGroup = mixer;
            return source;
        }

        private void OnDestroy()
        {
            if (_outside != null)
            {
                Destroy(_outside.gameObject);
            }

            if (_helmet != null)
            {
                Destroy(_helmet.gameObject);
            }

            if (_flapOutside != null)
            {
                Destroy(_flapOutside.gameObject);
            }

            if (_flapHelmet != null)
            {
                Destroy(_flapHelmet.gameObject);
            }
        }

        /// <summary>
        /// The sound, built by the first Drood 2.0 in flight and kept for the session (null if
        /// the game's clip can't be read).
        /// </summary>
        /// <summary>The stall's flap loop (built with the opening sound; null if that failed).</summary>
        private static AudioClip GetFlapClip()
        {
            GetClip();
            return _flapClip;
        }

        private static AudioClip GetClip()
        {
            // Kept for the session; rebuilt if Unity unloaded it. Not retried after a failure.
            if (_clip != null || _built)
            {
                return _clip;
            }

            _built = true;
            AudioClip stock = null;
            try
            {
                stock = ModApi.Audio.AudioLibrary.Flight.ParachuteDeployed?.AudioClip;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Drood 2.0] Couldn't load the game's parachute sound: {e.Message}");
            }

            if (stock == null)
            {
                stock = Resources.Load<AudioClip>("Audio/Sounds/Parachute");
            }

            if (stock == null)
            {
                Debug.LogWarning("[Drood 2.0] The game's parachute sound wasn't found; no opening sound.");
                return null;
            }

            var raw = new float[stock.samples * stock.channels];
            if (!stock.GetData(raw, 0))
            {
                Debug.LogWarning("[Drood 2.0] The game's parachute sound can't be read; no opening sound.");
                return null;
            }

            var mono = new float[stock.samples];
            for (var i = 0; i < mono.Length; i++)
            {
                var sum = 0f;
                for (var c = 0; c < stock.channels; c++)
                {
                    sum += raw[i * stock.channels + c];
                }

                mono[i] = sum / stock.channels;
            }

            var samples = Build(mono, stock.frequency);
            _built = false;
            _clip = AudioClip.Create("DroodCanopyOpening", samples.Length, 1, stock.frequency, false);
            _clip.SetData(samples, 0);
            var flap = BuildFlap(mono, stock.frequency);
            _flapClip = AudioClip.Create("DroodStallFlap", flap.Length, 1, stock.frequency, false);
            _flapClip.SetData(flap, 0);
            DroodLog.Debug($"Opening sound built: {samples.Length / (float)stock.frequency:F2} s from the game's parachute clip.");
            return _clip;
        }

        /// <summary>The two layers from the stock clip (mono samples at <paramref name="rate"/> Hz).</summary>
        private static float[] Build(float[] stock, int rate)
        {
            var random = new System.Random(7);
            float White() => (float)random.NextDouble() * 2f - 1f;

            // 1. Slower and lower.
            var body = new float[(int)(stock.Length / Speed)];
            for (var i = 0; i < body.Length; i++)
            {
                var p = i * Speed;
                var k = (int)p;
                var f = p - k;
                body[i] = stock[k] * (1f - f) + (k + 1 < stock.Length ? stock[k + 1] : 0f) * f;
            }

            // 2. Bright at the start, deep after.
            var low = LowPass(LowPass(body, 300f, rate), 300f, rate);
            for (var i = 0; i < body.Length; i++)
            {
                var w = Smooth(TiltFrom, TiltTo, i / (float)rate);
                body[i] = low[i] * (1f + BassGain * w) + (body[i] - low[i]) * (1f - TopCut * w);
            }

            // 3. Less buzz: a rough low noise layer following the body's level, its flutter irregular.
            var level = new float[body.Length];
            for (var i = 0; i < body.Length; i++)
            {
                level[i] = Mathf.Abs(body[i]);
            }

            level = LowPass(level, 30f, rate);
            var noise = new float[body.Length];
            var wobble = new float[body.Length];
            for (var i = 0; i < body.Length; i++)
            {
                noise[i] = White();
                wobble[i] = White();
            }

            noise = LowPass(LowPass(noise, 350f, rate), 350f, rate);
            wobble = LowPass(wobble, 35f, rate);
            var noiseRms = Mathf.Max(Rms(noise, 0, noise.Length), 1e-9f);
            var wobbleMax = 1e-9f;
            foreach (var v in wobble)
            {
                wobbleMax = Mathf.Max(wobbleMax, Mathf.Abs(v));
            }

            for (var i = 0; i < body.Length; i++)
            {
                body[i] += Roughness * level[i] * 1.5f * (noise[i] / noiseRms) * (0.6f + 0.4f * wobble[i] / wobbleMax);
            }

            // 4. Held, then cut short.
            for (var i = 0; i < body.Length; i++)
            {
                var t = i / (float)rate;
                body[i] *= (1f + HoldGain * Smooth(HoldFrom, HoldTo, t)) * (1f - Smooth(CutAt, CutAt + CutLength, t));
            }

            // 5. The lead-in rustle (crackling, high), the body, and a soft tail.
            var lead = (int)(LeadSeconds * rate);
            var cut = Mathf.Min((int)((CutAt + CutLength) * rate), body.Length);
            var tail = (int)(TailSeconds * rate);
            var overlap = (int)(0.03f * rate);
            var reshaped = new float[lead + cut + tail - overlap];
            var high = 0f;
            var grain = 0f;
            for (var i = 0; i < lead; i++)
            {
                var v = White();
                high += (v - high) * 0.15f;
                v -= high;
                if (random.NextDouble() < 0.004)
                {
                    grain = 1f;
                }

                grain *= 0.995f;
                var t = i / (float)lead;
                reshaped[i] = v * LeadLevel * t * t * (0.5f + grain);
            }

            System.Array.Copy(body, 0, reshaped, lead, cut);
            var tailNoise = new float[tail];
            for (var i = 0; i < tail; i++)
            {
                tailNoise[i] = White();
            }

            tailNoise = LowPass(tailNoise, 900f, rate);
            var tailRms = Mathf.Max(Rms(tailNoise, 0, tail), 1e-9f);
            for (var i = 0; i < tail; i++)
            {
                var fade = 1f - i / (float)tail;
                reshaped[lead + cut - overlap + i] += tailNoise[i] / tailRms * TailLevel * fade * fade;
            }

            // The stock clip on top, softer, both layers at the same loudest level first.
            Scale(reshaped, 1f / Mathf.Max(PeakRms(reshaped, rate), 1e-9f));
            var stockScale = StockLevel / Mathf.Max(PeakRms(stock, rate), 1e-9f);
            var delay = (int)(StockDelay * rate);
            var result = new float[Mathf.Max(reshaped.Length, delay + stock.Length)];
            System.Array.Copy(reshaped, result, reshaped.Length);
            for (var i = 0; i < stock.Length; i++)
            {
                result[delay + i] += stock[i] * stockScale;
            }

            // Its loudest moment at DeployLevel; never clipping.
            Scale(result, DeployLevel / Mathf.Max(PeakRms(result, rate), 1e-9f));
            var peak = 0f;
            foreach (var v in result)
            {
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }

            if (peak > 0.95f)
            {
                Scale(result, 0.95f / peak);
            }

            return result;
        }

        /// <summary>
        /// The stall's flap loop from the stock clip (mono samples at <paramref name="rate"/> Hz):
        /// grains of its loudest part, overlapping, wrapping round the end so the loop has no seam.
        /// </summary>
        private static float[] BuildFlap(float[] stock, int rate)
        {
            var random = new System.Random(11);
            float Between(float a, float b) => a + (float)random.NextDouble() * (b - a);

            // Where the clip is loud (10 ms windows above 40% of the loudest).
            var window = Mathf.Max(rate / 100, 1);
            var loudest = 0f;
            for (var start = 0; start + window <= stock.Length; start += window)
            {
                loudest = Mathf.Max(loudest, Rms(stock, start, window));
            }

            var loud = new System.Collections.Generic.List<int>();
            var longest = (int)(FlapGrainMax * rate) + 1;
            for (var start = 0; start + window <= stock.Length - longest; start += window)
            {
                if (Rms(stock, start, window) > 0.4f * loudest)
                {
                    loud.Add(start);
                }
            }

            if (loud.Count == 0)
            {
                loud.Add(0);
            }

            // The swell: a strength per FlapSwellSeconds, read between them, round the loop.
            var length = (int)(FlapLoopSeconds * rate);
            var swells = Mathf.Max((int)(FlapLoopSeconds / FlapSwellSeconds), 2);
            var swell = new float[swells];
            for (var i = 0; i < swells; i++)
            {
                swell[i] = Between(FlapSwellMin, 1f);
            }

            var result = new float[length];
            var t = 0;
            while (t < length)
            {
                var grain = Mathf.Min((int)(Between(FlapGrainMin, FlapGrainMax) * rate), longest);
                var from = loud[random.Next(loud.Count)];
                var at = t / (float)length * swells;
                var k = (int)at;
                var gain = Between(FlapGainMin, 1f) * Mathf.Lerp(swell[k % swells], swell[(k + 1) % swells], Mathf.SmoothStep(0f, 1f, at - k));
                for (var i = 0; i < grain && from + i < stock.Length; i++)
                {
                    var shape = Mathf.Sin(Mathf.PI * i / grain);
                    result[(t + i) % length] += stock[from + i] * shape * shape * gain;
                }

                t += (int)(Between(FlapGapMin, FlapGapMax) * rate);
            }

            // As loud as the opening at its loudest; never clipping.
            Scale(result, DeployLevel / Mathf.Max(PeakRms(result, rate), 1e-9f));
            var peak = 0f;
            foreach (var v in result)
            {
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }

            if (peak > 0.95f)
            {
                Scale(result, 0.95f / peak);
            }

            return result;
        }

        private static float[] LowPass(float[] input, float cutoff, int rate)
        {
            var a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / rate);
            var output = new float[input.Length];
            var y = 0f;
            for (var i = 0; i < input.Length; i++)
            {
                y += (input[i] - y) * a;
                output[i] = y;
            }

            return output;
        }

        private static float Smooth(float from, float to, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

        private static float Rms(float[] samples, int start, int count)
        {
            var sum = 0.0;
            for (var i = start; i < start + count && i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            return (float)System.Math.Sqrt(sum / Mathf.Max(count, 1));
        }

        /// <summary>The loudest 50 ms (RMS), in steps of 25 ms.</summary>
        private static float PeakRms(float[] samples, int rate)
        {
            var window = Mathf.Max(rate / 20, 1);
            var peak = 0f;
            for (var start = 0; start + window <= samples.Length; start += window / 2)
            {
                peak = Mathf.Max(peak, Rms(samples, start, window));
            }

            return peak;
        }

        private static void Scale(float[] samples, float factor)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] *= factor;
            }
        }
    }
}
