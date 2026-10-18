using System;
using System.IO;
using UnityEngine;
using Random = System.Random;

namespace RunwayPanic.AudioTools
{
    /// <summary>Oscillator shapes, built from harmonics (band-limited, so no aliasing buzz).</summary>
    internal enum Wave { Sine, Saw, Square, Triangle }

    /// <summary>
    /// A small sound synthesiser for the game's generated audio: a mono buffer plus the building blocks every recipe
    /// uses — tones with pitch curves, noise (filtered or band-passed with a moving centre), a formant "voice" for
    /// grunts and bird calls, echo, low-pass, seamless loops and WAV output. Times are in seconds, levels 0..1.
    /// </summary>
    internal sealed class Signal
    {
        public readonly float[] S;
        public readonly int Rate;

        public Signal(float seconds, int rate = 44100)
        {
            Rate = rate;
            S = new float[Mathf.Max(1, Mathf.CeilToInt(seconds * rate))];
        }

        // Exact sample count: loops must not gain a stray empty sample from rounding seconds back to samples.
        private Signal(int samples, int rate)
        {
            Rate = rate;
            S = new float[Mathf.Max(1, samples)];
        }

        public float Seconds => S.Length / (float)Rate;

        // ---------- Envelopes and curves (t = seconds since the event started) ----------

        /// <summary>Fast attack, exponential decay.</summary>
        public static Func<float, float> Perc(float attack, float decay) =>
            t => t < attack ? t / Mathf.Max(attack, 1e-5f) : Mathf.Exp(-(t - attack) / Mathf.Max(decay, 1e-5f));

        /// <summary>Linear attack, hold, linear release over <paramref name="duration"/>.</summary>
        public static Func<float, float> Swell(float attack, float release, float duration) =>
            t => Mathf.Clamp01(Mathf.Min(t / Mathf.Max(attack, 1e-5f), (duration - t) / Mathf.Max(release, 1e-5f)));

        /// <summary>Glide from <paramref name="from"/> to <paramref name="to"/> over <paramref name="duration"/>, eased.</summary>
        public static Func<float, float> Glide(float from, float to, float duration) =>
            t => Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(duration, 1e-5f)));

        /// <summary>Constant value.</summary>
        public static Func<float, float> Const(float value) => t => value;

        public static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        // ---------- Sources ----------

        /// <summary>Adds a tone: pitch and loudness follow the curves; vibrato in Hz/depth (fraction).</summary>
        public Signal Tone(float start, float duration, Func<float, float> pitch, Func<float, float> envelope, float amp,
            Wave wave = Wave.Sine, int harmonics = 12, float vibratoHz = 0f, float vibratoDepth = 0f)
        {
            int first = Mathf.RoundToInt(start * Rate), count = Mathf.RoundToInt(duration * Rate);
            double phase = 0;
            for (int i = 0; i < count && first + i < S.Length; i++)
            {
                if (first + i < 0) continue;
                float t = i / (float)Rate;
                float f = pitch(t) * (1f + vibratoDepth * Mathf.Sin(2f * Mathf.PI * vibratoHz * t));
                phase += 2.0 * Math.PI * f / Rate;
                S[first + i] += amp * envelope(t) * Osc(phase, f, wave, harmonics);
            }

            return this;
        }

        private float Osc(double phase, float frequency, Wave wave, int harmonics)
        {
            if (wave == Wave.Sine)
                return Mathf.Sin((float)phase);

            float nyquist = Rate * 0.45f, sum = 0f;
            for (int k = 1; k <= harmonics; k++)
            {
                if (frequency * k > nyquist) break;
                switch (wave)
                {
                    case Wave.Saw: sum += Mathf.Sin((float)(phase * k)) / k; break;
                    case Wave.Square: if ((k & 1) == 1) sum += Mathf.Sin((float)(phase * k)) / k; break;
                    case Wave.Triangle: if ((k & 1) == 1) sum += ((k & 2) == 0 ? 1f : -1f) * Mathf.Sin((float)(phase * k)) / (k * k); break;
                }
            }

            return wave == Wave.Triangle ? sum * 0.8f : sum * 0.6f;
        }

        /// <summary>Adds noise, optionally low-passed and/or high-passed (one-pole, cut-offs in Hz; 0 = off).</summary>
        public Signal Noise(float start, float duration, Func<float, float> envelope, float amp, int seed,
            float lowpass = 0f, float highpass = 0f)
        {
            var random = new Random(seed);
            int first = Mathf.RoundToInt(start * Rate), count = Mathf.RoundToInt(duration * Rate);
            float lp = 0f, hpIn = 0f, hpOut = 0f;
            float aLow = lowpass > 0f ? OnePole(lowpass) : 1f;
            float aHigh = highpass > 0f ? Mathf.Exp(-2f * Mathf.PI * highpass / Rate) : 0f;
            for (int i = 0; i < count && first + i < S.Length; i++)
            {
                if (first + i < 0) continue;
                float x = (float)(random.NextDouble() * 2.0 - 1.0);
                lp += aLow * (x - lp);
                float y = lp;
                if (highpass > 0f)
                {
                    hpOut = aHigh * (hpOut + y - hpIn);
                    hpIn = y;
                    y = hpOut;
                }

                S[first + i] += amp * envelope(i / (float)Rate) * y;
            }

            return this;
        }

        /// <summary>Adds band-passed noise whose centre (Hz) follows a curve — whooshes, scrapes, wind.</summary>
        public Signal Band(float start, float duration, Func<float, float> centre, float q, Func<float, float> envelope,
            float amp, int seed)
        {
            var random = new Random(seed);
            int first = Mathf.RoundToInt(start * Rate), count = Mathf.RoundToInt(duration * Rate);
            float x1 = 0, x2 = 0, y1 = 0, y2 = 0, b0 = 0, b2 = 0, a1 = 0, a2 = 0;
            for (int i = 0; i < count && first + i < S.Length; i++)
            {
                if ((i & 31) == 0) // recompute the filter every 32 samples
                {
                    float w = 2f * Mathf.PI * Mathf.Clamp(centre(i / (float)Rate), 20f, Rate * 0.45f) / Rate;
                    float alpha = Mathf.Sin(w) / (2f * Mathf.Max(q, 0.1f));
                    float a0 = 1f + alpha;
                    b0 = alpha / a0; b2 = -alpha / a0; a1 = -2f * Mathf.Cos(w) / a0; a2 = (1f - alpha) / a0;
                }

                float x = (float)(random.NextDouble() * 2.0 - 1.0);
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                if (first + i >= 0)
                    S[first + i] += amp * envelope(i / (float)Rate) * y * 3f;
            }

            return this;
        }

        /// <summary>
        /// Adds a voice-like sound: a buzzy pitched source shaped by resonances (formants), with rasp (a fast flutter
        /// of loudness) and breath noise. Formants are (centre Hz, width Hz, gain).
        /// </summary>
        public Signal Voice(float start, float duration, Func<float, float> pitch, Func<float, float> envelope, float amp,
            (float centre, float width, float gain)[] formants, float rasp, float breath, int seed, int harmonics = 18)
        {
            var random = new Random(seed);
            int first = Mathf.RoundToInt(start * Rate), count = Mathf.RoundToInt(duration * Rate);
            double phase = 0;
            float noise = 0f;
            for (int i = 0; i < count && first + i < S.Length; i++)
            {
                if (first + i < 0) continue;
                float t = i / (float)Rate;
                float f = pitch(t);
                phase += 2.0 * Math.PI * f / Rate;

                float voiced = 0f;
                for (int k = 1; k <= harmonics; k++)
                {
                    float fk = f * k;
                    if (fk > Rate * 0.45f) break;
                    float shape = 0.15f;
                    foreach ((float c, float w, float g) in formants)
                    {
                        float d = (fk - c) / w;
                        shape += g * Mathf.Exp(-0.5f * d * d);
                    }

                    voiced += Mathf.Sin((float)(phase * k)) * shape / Mathf.Sqrt(k);
                }

                float flutter = 1f - rasp * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * 63f * t));
                noise = Mathf.Lerp(noise, (float)(random.NextDouble() * 2.0 - 1.0), 0.3f);
                S[first + i] += amp * envelope(t) * (voiced * 0.25f * flutter * (1f - breath) + noise * breath);
            }

            return this;
        }

        /// <summary>A short metallic click: inharmonic partials with fast decay (gun mechanics, latches).</summary>
        public Signal Metal(float start, float[] partials, float decay, float amp)
        {
            foreach (float p in partials)
                Tone(start, decay * 6f, Const(p), Perc(0.0005f, decay), amp / partials.Length);
            return Noise(start, 0.01f, Perc(0.0002f, 0.002f), amp * 0.5f, (int)(start * 1000) + 7, 0f, 2000f);
        }

        // ---------- Processing ----------

        /// <summary>Adds delayed, quieter copies of the whole sound (room or distance echo).</summary>
        public Signal Echo(float delay, float feedback, int taps)
        {
            int d = Mathf.RoundToInt(delay * Rate);
            var dry = (float[])S.Clone();
            float gain = 1f;
            for (int tap = 1; tap <= taps; tap++)
            {
                gain *= feedback;
                int offset = d * tap;
                for (int i = 0; i + offset < S.Length; i++)
                    S[i + offset] += dry[i] * gain;
            }

            return this;
        }

        /// <summary>One-pole low-pass over the whole sound.</summary>
        public Signal LowPass(float cutoff)
        {
            float a = OnePole(cutoff), y = 0f;
            for (int i = 0; i < S.Length; i++)
                S[i] = y += a * (S[i] - y);
            return this;
        }

        /// <summary>Mixes <paramref name="other"/> in at <paramref name="at"/> seconds.</summary>
        public Signal Mix(Signal other, float at = 0f, float gain = 1f)
        {
            int first = Mathf.RoundToInt(at * Rate);
            float step = other.Rate / (float)Rate;
            for (int i = 0; first + i < S.Length; i++)
            {
                int j = Mathf.FloorToInt(i * step);
                if (j >= other.S.Length) break;
                if (first + i >= 0) S[first + i] += other.S[j] * gain;
            }

            return this;
        }

        /// <summary>Fades the last <paramref name="seconds"/> to silence (no click at the end).</summary>
        public Signal FadeOut(float seconds)
        {
            int n = Mathf.Min(S.Length, Mathf.RoundToInt(seconds * Rate));
            for (int i = 0; i < n; i++)
                S[S.Length - 1 - i] *= i / (float)n;
            return this;
        }

        /// <summary>Fades the first <paramref name="seconds"/> in.</summary>
        public Signal FadeIn(float seconds)
        {
            int n = Mathf.Min(S.Length, Mathf.RoundToInt(seconds * Rate));
            for (int i = 0; i < n; i++)
                S[i] *= i / (float)n;
            return this;
        }

        /// <summary>
        /// Makes a seamless loop of the first <paramref name="loopSeconds"/>: whatever rings on past the loop point
        /// (tails, reverb) is folded back onto the start, so the end flows into the beginning.
        /// </summary>
        public Signal Loop(float loopSeconds)
        {
            int n = Mathf.Min(S.Length, Mathf.RoundToInt(loopSeconds * Rate));
            var looped = new Signal(n, Rate);
            for (int i = 0; i < S.Length; i++)
                looped.S[i % n] += S[i];
            return looped;
        }

        /// <summary>
        /// Makes a seamless loop of <paramref name="loopSeconds"/> by crossfading the sound's continuation into its
        /// start over <paramref name="fadeSeconds"/> — for steady sounds (wind, scrape). Render at least
        /// loopSeconds + fadeSeconds first.
        /// </summary>
        public Signal LoopCrossfade(float loopSeconds, float fadeSeconds)
        {
            int n = Mathf.RoundToInt(loopSeconds * Rate), fade = Mathf.RoundToInt(fadeSeconds * Rate);
            var looped = new Signal(n, Rate);
            for (int i = 0; i < n; i++)
            {
                float s = i < S.Length ? S[i] : 0f;
                if (i < fade && i + n < S.Length)
                {
                    float w = i / (float)fade; // equal-power crossfade from the tail into the head
                    s = s * Mathf.Sin(w * Mathf.PI * 0.5f) + S[i + n] * Mathf.Cos(w * Mathf.PI * 0.5f);
                }

                looped.S[i] = s;
            }

            return looped;
        }

        /// <summary>Scales so the loudest sample is <paramref name="peak"/>.</summary>
        public Signal Normalise(float peak = 0.89f)
        {
            float max = 0f;
            foreach (float s in S) max = Mathf.Max(max, Mathf.Abs(s));
            if (max > 0f)
                for (int i = 0; i < S.Length; i++) S[i] *= peak / max;
            return this;
        }

        /// <summary>Gentle saturation: rounds off peaks (adds weight to gunshots and drums).</summary>
        public Signal Saturate(float drive)
        {
            for (int i = 0; i < S.Length; i++)
                S[i] = (float)Math.Tanh(S[i] * drive);
            return this;
        }

        private float OnePole(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Min(cutoff, Rate * 0.45f) / Rate);

        // ---------- Output ----------

        /// <summary>Writes a 16-bit mono PCM .wav.</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                int dataSize = S.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataSize);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataSize);
                foreach (float s in S)
                    w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * short.MaxValue), short.MinValue, short.MaxValue));
                File.WriteAllBytes(path, stream.ToArray());
            }
        }
    }
}
