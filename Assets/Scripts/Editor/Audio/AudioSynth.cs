using System;
using System.IO;
using UnityEngine;

namespace RallyX.EditorTools
{
    /// <summary>
    /// A small offline synthesiser for the placeholder sounds: noise and tones, filters, envelopes, loops that join
    /// seamlessly and 16-bit mono WAV output. Seeded, so the same recipe always gives the same sound.
    /// </summary>
    internal sealed class AudioSynth
    {
        private readonly System.Random random;

        public AudioSynth(int rate, int seed)
        {
            Rate = rate;
            random = new System.Random(seed);
        }

        /// <summary>Samples per second.</summary>
        public int Rate { get; }

        public int Samples(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));

        public float[] Buffer(float seconds) => new float[Samples(seconds)];

        /// <summary>White noise, -1..1.</summary>
        public float Noise() => (float)(random.NextDouble() * 2.0 - 1.0);

        /// <summary>A random number from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        public float Chance() => (float)random.NextDouble();

        // ---------------------------------------------------------------- filters

        /// <summary>A two-pole filter (RBJ cookbook), transposed direct form II. Keeps its state between samples.</summary>
        public struct Biquad
        {
            private float b0, b1, b2, a1, a2;
            private float z1, z2;

            public float Process(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }

            /// <summary>New coefficients, keeping the state (for filters that sweep).</summary>
            public void Retune(Biquad other)
            {
                b0 = other.b0;
                b1 = other.b1;
                b2 = other.b2;
                a1 = other.a1;
                a2 = other.a2;
            }

            public static Biquad LowPass(int rate, float frequency, float q = 0.707f) =>
                Make(rate, frequency, q, (cos, alpha) => ((1f - cos) / 2f, 1f - cos, (1f - cos) / 2f, 1f + alpha, -2f * cos, 1f - alpha));

            public static Biquad HighPass(int rate, float frequency, float q = 0.707f) =>
                Make(rate, frequency, q, (cos, alpha) => ((1f + cos) / 2f, -(1f + cos), (1f + cos) / 2f, 1f + alpha, -2f * cos, 1f - alpha));

            /// <summary>Band-pass with 0 dB at the centre.</summary>
            public static Biquad BandPass(int rate, float frequency, float q) =>
                Make(rate, frequency, q, (cos, alpha) => (alpha, 0f, -alpha, 1f + alpha, -2f * cos, 1f - alpha));

            public static Biquad Peak(int rate, float frequency, float q, float gainDb)
            {
                float a = Mathf.Pow(10f, gainDb / 40f);
                return Make(rate, frequency, q, (cos, alpha) => (1f + alpha * a, -2f * cos, 1f - alpha * a, 1f + alpha / a, -2f * cos, 1f - alpha / a));
            }

            private static Biquad Make(int rate, float frequency, float q,
                Func<float, float, (float b0, float b1, float b2, float a0, float a1, float a2)> design)
            {
                frequency = Mathf.Clamp(frequency, 10f, rate * 0.45f);
                float w0 = 2f * Mathf.PI * frequency / rate;
                var (b0, b1, b2, a0, a1, a2) = design(Mathf.Cos(w0), Mathf.Sin(w0) / (2f * q));
                return new Biquad { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
            }
        }

        /// <summary>Runs a filter over a whole buffer.</summary>
        public static void Filter(float[] data, Biquad filter)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] = filter.Process(data[i]);
        }

        /// <summary>
        /// Runs a filter over a buffer that is one period of a loop, in its steady state: it is run over the loop once to
        /// settle, then again for the result, so the filtered loop still joins without a click.
        /// </summary>
        public static void FilterLoop(float[] data, Biquad filter)
        {
            for (int i = 0; i < data.Length; i++)
                filter.Process(data[i]);
            for (int i = 0; i < data.Length; i++)
                data[i] = filter.Process(data[i]);
        }

        /// <summary>One-pole low-pass, in place, a cheap gentle smoothing.</summary>
        public void Smooth(float[] data, float frequency)
        {
            float k = 1f - Mathf.Exp(-2f * Mathf.PI * frequency / Rate);
            float y = 0f;
            for (int i = 0; i < data.Length; i++)
                data[i] = y += (data[i] - y) * k;
        }

        // ---------------------------------------------------------------- building blocks

        /// <summary>Filtered noise: white noise through a chain of filters.</summary>
        public float[] FilteredNoise(float seconds, params Biquad[] filters)
        {
            var data = Buffer(seconds);
            for (int i = 0; i < data.Length; i++)
                data[i] = Noise();
            foreach (var filter in filters)
                Filter(data, filter);
            return data;
        }

        /// <summary>Brown noise (integrated white noise with a slight leak), a soft low rumble.</summary>
        public float[] BrownNoise(float seconds)
        {
            var data = Buffer(seconds);
            float y = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                y = y * 0.996f + Noise() * 0.06f;
                data[i] = y;
            }

            return data;
        }

        /// <summary>A smoothly wandering value from 0 to 1 over time: slow random modulation.</summary>
        public float[] Wander(int length, float changesPerSecond)
        {
            var data = new float[length];
            int step = Mathf.Max(1, Mathf.RoundToInt(Rate / Mathf.Max(changesPerSecond, 0.01f)));
            float from = Chance();
            float to = Chance();
            for (int i = 0; i < length; i++)
            {
                int phase = i % step;
                if (phase == 0 && i > 0)
                {
                    from = to;
                    to = Chance();
                }

                float t = phase / (float)step;
                data[i] = Mathf.Lerp(from, to, t * t * (3f - 2f * t));
            }

            return data;
        }

        /// <summary>A tone with harmonics, a pitch glide and an attack/exponential decay envelope.</summary>
        public float[] Tone(float seconds, float startHz, float endHz, float attack, float decay, params float[] harmonics)
        {
            var data = Buffer(seconds);
            if (harmonics.Length == 0)
                harmonics = new[] { 1f };
            double phase = 0.0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float frequency = Mathf.Lerp(startHz, endHz, Mathf.Clamp01(t / seconds));
                phase += 2.0 * Math.PI * frequency / Rate;
                float value = 0f;
                for (int h = 0; h < harmonics.Length; h++)
                    value += harmonics[h] * (float)Math.Sin(phase * (h + 1));
                data[i] = value * Envelope(t, attack, decay);
            }

            return data;
        }

        /// <summary>A short burst: rises over <paramref name="attack"/> seconds, then decays exponentially with the given time constant.</summary>
        public static float Envelope(float t, float attack, float decay)
        {
            if (t < attack)
                return attack > 0f ? t / attack : 1f;
            return Mathf.Exp(-(t - attack) / Mathf.Max(decay, 0.0001f));
        }

        /// <summary>A damped resonance: a sine at <paramref name="frequency"/> dying away with the given time constant.</summary>
        public float[] Ring(float frequency, float decay, float seconds)
        {
            var data = Buffer(seconds);
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-t / decay);
            }

            return data;
        }

        // ---------------------------------------------------------------- mixing

        /// <summary>Adds a sound into a buffer from a sample onwards, wrapping past the end (for loops).</summary>
        public static void AddLooped(float[] buffer, int start, float[] sound, float gain)
        {
            int length = buffer.Length;
            for (int i = 0; i < sound.Length; i++)
                buffer[((start + i) % length + length) % length] += sound[i] * gain;
        }

        /// <summary>Adds a sound into a buffer from a sample onwards, cutting it at the end.</summary>
        public static void Add(float[] buffer, int start, float[] sound, float gain)
        {
            for (int i = 0; i < sound.Length && start + i < buffer.Length; i++)
            {
                if (start + i >= 0)
                    buffer[start + i] += sound[i] * gain;
            }
        }

        public static void Multiply(float[] data, float[] by)
        {
            for (int i = 0; i < data.Length && i < by.Length; i++)
                data[i] *= by[i];
        }

        /// <summary>
        /// Makes a buffer loop seamlessly: its last <paramref name="overlapSeconds"/> are crossfaded (equal power) into its
        /// start and cut off. For noisy sounds with no period of their own.
        /// </summary>
        public float[] Seamless(float[] data, float overlapSeconds)
        {
            int overlap = Mathf.Clamp(Samples(overlapSeconds), 1, data.Length / 2);
            var loop = new float[data.Length - overlap];
            Array.Copy(data, loop, loop.Length);
            for (int i = 0; i < overlap; i++)
            {
                float t = i / (float)overlap;
                loop[i] = data[i] * Mathf.Sin(t * Mathf.PI * 0.5f) + data[loop.Length + i] * Mathf.Cos(t * Mathf.PI * 0.5f);
            }

            return loop;
        }

        /// <summary>Short fades at both ends of a one-shot, so it starts and stops without a click.</summary>
        public void FadeEnds(float[] data, float fadeIn = 0.002f, float fadeOut = 0.02f)
        {
            int inSamples = Mathf.Min(Samples(fadeIn), data.Length);
            int outSamples = Mathf.Min(Samples(fadeOut), data.Length);
            for (int i = 0; i < inSamples; i++)
                data[i] *= i / (float)inSamples;
            for (int i = 0; i < outSamples; i++)
                data[data.Length - 1 - i] *= i / (float)outSamples;
        }

        /// <summary>Scales a buffer so its loudest sample is <paramref name="peak"/>, after removing any DC offset.</summary>
        public static void Normalize(float[] data, float peak)
        {
            double mean = 0.0;
            foreach (float value in data)
                mean += value;
            mean /= Math.Max(1, data.Length);

            float loudest = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] -= (float)mean;
                loudest = Mathf.Max(loudest, Mathf.Abs(data[i]));
            }

            if (loudest <= 0f)
                return;
            float scale = peak / loudest;
            for (int i = 0; i < data.Length; i++)
                data[i] *= scale;
        }

        /// <summary>Gentle saturation: keeps peaks under 1 and adds a little grit.</summary>
        public static void Saturate(float[] data, float drive)
        {
            float norm = (float)Math.Tanh(drive);
            for (int i = 0; i < data.Length; i++)
                data[i] = (float)Math.Tanh(data[i] * drive) / norm;
        }

        // ---------------------------------------------------------------- output

        /// <summary>A 16-bit mono PCM WAV file.</summary>
        public static byte[] Wav(float[] data, int rate)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            int bytes = data.Length * 2;
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + bytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(bytes);
            foreach (float value in data)
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * 32767f));
            writer.Flush();
            return stream.ToArray();
        }
    }
}
