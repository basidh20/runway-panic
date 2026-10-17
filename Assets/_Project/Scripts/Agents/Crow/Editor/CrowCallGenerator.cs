using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace RunwayPanic.Agents.Crow.EditorTools
{
    /// <summary>
    /// Tools > IS > Crow > Generate Crow Calls: synthesises the crow's three tell sounds as .wav files, so the tells
    /// need no downloaded audio. Replace them with recorded calls any time (keep the file names).
    /// <para>
    /// A crow's "caw" is harsh and nasal: a buzzy voice (many harmonics) shaped by two resonances, a pitch that rises
    /// then falls, rasp (fast flutter of the loudness) and breath noise. Each call is built from those parts:
    /// </para>
    /// <list type="bullet">
    /// <item><c>SFX_Crow_Caw</c> — one caw; the funeral plays it in a slow repeating pattern.</item>
    /// <item><c>SFX_Crow_Call</c> — a quick, higher "ca-caw": the thief grabbing a pickup.</item>
    /// <item><c>SFX_Crow_Rage</c> — a long, low, very rough caw: the crow becoming enraged.</item>
    /// </list>
    /// Deterministic (fixed seed): running it again produces identical files.
    /// </summary>
    public static class CrowCallGenerator
    {
        private const string Folder = "Assets/_Project/Audio/SFX/Crow";
        private const int SampleRate = 44100;
        private const int Harmonics = 14;
        private const float PeakLevel = 0.89f; // −1 dBFS

        /// <summary>One syllable of a call.</summary>
        private struct Syllable
        {
            public float Start;      // seconds from the start of the clip
            public float Length;     // seconds
            public float PitchStart; // Hz
            public float PitchPeak;  // Hz, reached at 30% of the syllable
            public float PitchEnd;   // Hz
            public float Rasp;       // 0..1 depth of the loudness flutter
            public float Breath;     // 0..1 noise mix
            public float Gain;       // relative loudness
        }

        [MenuItem("Tools/IS/Crow/Generate Crow Calls")]
        public static void Generate()
        {
            Directory.CreateDirectory(Folder);

            Write("SFX_Crow_Caw", 0.36f, new[]
            {
                new Syllable { Start = 0.01f, Length = 0.32f, PitchStart = 560f, PitchPeak = 690f, PitchEnd = 500f, Rasp = 0.45f, Breath = 0.18f, Gain = 1f },
            });

            Write("SFX_Crow_Call", 0.42f, new[]
            {
                new Syllable { Start = 0.01f, Length = 0.13f, PitchStart = 700f, PitchPeak = 800f, PitchEnd = 680f, Rasp = 0.35f, Breath = 0.15f, Gain = 0.85f },
                new Syllable { Start = 0.19f, Length = 0.21f, PitchStart = 680f, PitchPeak = 820f, PitchEnd = 600f, Rasp = 0.4f, Breath = 0.15f, Gain = 1f },
            });

            Write("SFX_Crow_Rage", 0.58f, new[]
            {
                new Syllable { Start = 0.01f, Length = 0.55f, PitchStart = 430f, PitchPeak = 560f, PitchEnd = 360f, Rasp = 0.75f, Breath = 0.32f, Gain = 1f },
            });

            AssetDatabase.Refresh();
            Debug.Log($"[Crow] Generated SFX_Crow_Caw, SFX_Crow_Call and SFX_Crow_Rage in {Folder}.");
        }

        private static void Write(string name, float duration, Syllable[] syllables)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var samples = new float[count];
            int seed = 17;
            foreach (char c in name)
                seed = seed * 31 + c; // stable across runs, unlike string.GetHashCode
            var random = new Random(seed);

            foreach (Syllable s in syllables)
                AddSyllable(samples, s, random);

            Normalise(samples);
            File.WriteAllBytes($"{Folder}/{name}.wav", ToWav(samples));
        }

        private static void AddSyllable(float[] samples, Syllable s, Random random)
        {
            int first = Mathf.Max(0, Mathf.RoundToInt(s.Start * SampleRate));
            int length = Mathf.RoundToInt(s.Length * SampleRate);
            double phase = 0;
            float noiseLow = 0f;

            for (int i = 0; i < length && first + i < samples.Length; i++)
            {
                float t = i / (float)length; // 0..1 through the syllable

                // Pitch: up to the peak at 30%, then down.
                float pitch = t < 0.3f
                    ? Mathf.Lerp(s.PitchStart, s.PitchPeak, t / 0.3f)
                    : Mathf.Lerp(s.PitchPeak, s.PitchEnd, (t - 0.3f) / 0.7f);
                phase += 2.0 * Math.PI * pitch / SampleRate;

                // Buzzy voice: harmonics weighted by two nasal resonances (around 1.3 and 2.6 kHz).
                float voice = 0f;
                for (int k = 1; k <= Harmonics; k++)
                {
                    float frequency = pitch * k;
                    float shape = 0.35f + Resonance(frequency, 1300f, 450f) + 0.7f * Resonance(frequency, 2600f, 700f);
                    voice += Mathf.Sin((float)(phase * k)) * shape / k;
                }

                // Rasp: fast flutter of loudness, like air through a rough throat.
                float rasp = 1f - s.Rasp * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * 58f * i / SampleRate));

                // Breath: noise, softened a little.
                noiseLow = Mathf.Lerp(noiseLow, (float)(random.NextDouble() * 2.0 - 1.0), 0.35f);

                // Loudness: 12 ms attack, hold, then fade over the last 40%.
                float attack = Mathf.Clamp01(i / (0.012f * SampleRate));
                float release = t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f;
                float envelope = attack * release * release;

                samples[first + i] += s.Gain * envelope * (voice * rasp * (1f - s.Breath) + noiseLow * s.Breath);
            }
        }

        private static float Resonance(float frequency, float centre, float width)
        {
            float x = (frequency - centre) / width;
            return Mathf.Exp(-0.5f * x * x);
        }

        private static void Normalise(float[] samples)
        {
            float peak = 0f;
            foreach (float s in samples)
                peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak <= 0f)
                return;
            float scale = PeakLevel / peak;
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= scale;
        }

        // 16-bit mono PCM .wav.
        private static byte[] ToWav(float[] samples)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                int dataSize = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);                 // fmt chunk size
                writer.Write((short)1);           // PCM
                writer.Write((short)1);           // mono
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);     // byte rate
                writer.Write((short)2);           // block align
                writer.Write((short)16);          // bits per sample
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);
                foreach (float s in samples)
                    writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * short.MaxValue), short.MinValue, short.MaxValue));
                return stream.ToArray();
            }
        }
    }
}
