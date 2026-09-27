using System;
using UnityEngine;
using Biquad = RallyX.EditorTools.AudioSynth.Biquad;

namespace RallyX.EditorTools
{
    /// <summary>A generated sound: samples, their rate and whether it is meant to loop.</summary>
    internal readonly struct GeneratedSound
    {
        public GeneratedSound(float[] data, int rate, bool loop)
        {
            Data = data;
            Rate = rate;
            Loop = loop;
        }

        public float[] Data { get; }
        public int Rate { get; }
        public bool Loop { get; }
    }

    /// <summary>
    /// Recipes for the placeholder sounds, synthesised from scratch until licensed audio replaces them: engines built from
    /// firing pulses, tyre noise for each surface from shaped noise and grains, wind, scraping, impacts, landings, gear
    /// changes, backfires, menu sounds, race announcements and two short music loops. Loops join seamlessly: periodic
    /// sounds are whole numbers of periods rendered round the loop, noisy ones are crossfaded end to start.
    /// </summary>
    internal static class PlaceholderSounds
    {
        public const int EffectRate = 44100;
        public const int MusicRate = 32000;

        // ---------------------------------------------------------------- engine

        /// <summary>
        /// A four-cylinder engine at fixed revs, 1.5 s: each firing is a pair of damped exhaust resonances with a click of
        /// combustion noise, the four cylinders slightly unequal, pulse timing a touch loose. Recorded at 2000 rpm (low) or
        /// 5000 rpm (high); the car pitches it to its revs.
        /// </summary>
        public static GeneratedSound Engine(bool high)
        {
            var synth = new AudioSynth(EffectRate, high ? 21 : 11);
            float rpm = high ? 5000f : 2000f;
            float firing = rpm / 60f * 2f;
            int periods = high ? 250 : 100;
            float period = 1f / firing;
            var loop = new float[synth.Samples(periods * period)];
            float[] cylinders = { 1f, 0.88f, 1.08f, 0.94f };

            for (int k = 0; k < periods; k++)
            {
                float jitter = synth.Range(-0.02f, 0.02f) * period;
                int start = Mathf.RoundToInt((k * period + jitter) * synth.Rate);
                float gain = cylinders[k % cylinders.Length] * synth.Range(0.92f, 1.08f);
                AudioSynth.AddLooped(loop, start, FiringPulse(synth, high), gain);
            }

            // The firing frequency itself, steady, to give the note a core.
            for (int i = 0; i < loop.Length; i++)
            {
                float t = i / (float)synth.Rate;
                loop[i] += 0.22f * Mathf.Sin(2f * Mathf.PI * firing * t) + 0.08f * Mathf.Sin(4f * Mathf.PI * firing * t);
            }

            AudioSynth.FilterLoop(loop, Biquad.Peak(synth.Rate, high ? 320f : 120f, 1.2f, 4f));
            AudioSynth.FilterLoop(loop, Biquad.LowPass(synth.Rate, high ? 5200f : 2300f, 0.6f));

            // Levelled before the saturation, so it only adds a little grit however much the pulses build up.
            AudioSynth.Normalize(loop, 1f);
            AudioSynth.Saturate(loop, high ? 1.15f : 1.25f);
            AudioSynth.Normalize(loop, 0.85f);
            return new GeneratedSound(loop, synth.Rate, loop: true);
        }

        private static float[] FiringPulse(AudioSynth synth, bool high)
        {
            float seconds = high ? 0.06f : 0.12f;
            float detune = synth.Range(0.97f, 1.03f);
            var pulse = high
                ? Sum(synth.Ring(170f * detune, 0.014f, seconds), 0.9f, synth.Ring(520f * detune, 0.007f, seconds), 0.6f,
                    synth.Ring(1400f * detune, 0.003f, seconds), 0.35f)
                : Sum(synth.Ring(95f * detune, 0.03f, seconds), 1f, synth.Ring(240f * detune, 0.012f, seconds), 0.55f,
                    synth.Ring(620f * detune, 0.005f, seconds), 0.25f);

            // Combustion: a short burst of noise at the start of each firing.
            var noise = synth.FilteredNoise(high ? 0.006f : 0.004f, Biquad.BandPass(synth.Rate, high ? 2200f : 1600f, 0.8f));
            for (int i = 0; i < noise.Length; i++)
                noise[i] *= AudioSynth.Envelope(i / (float)synth.Rate, 0f, high ? 0.0015f : 0.001f);
            AudioSynth.Add(pulse, 0, noise, high ? 0.5f : 0.35f);
            return pulse;
        }

        // ---------------------------------------------------------------- tyres

        public static GeneratedSound AsphaltRoll()
        {
            var synth = new AudioSynth(EffectRate, 31);
            var data = synth.BrownNoise(2.3f);
            AudioSynth.Filter(data, Biquad.LowPass(synth.Rate, 350f));
            var hum = synth.FilteredNoise(2.3f, Biquad.BandPass(synth.Rate, 120f, 1.5f));
            Mix(data, hum, 0.5f);
            return Loop(synth, data, 0.3f, 0.6f);
        }

        /// <summary>Tyre squeal: a wavering rubbery tone around 820 Hz over bright scrubbing noise.</summary>
        public static GeneratedSound AsphaltSlide()
        {
            var synth = new AudioSynth(EffectRate, 32);
            var data = synth.Buffer(2.6f);
            var wobble = synth.Wander(data.Length, 3f);
            var level = synth.Wander(data.Length, 4f);
            double phase = 0.0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)synth.Rate;
                float frequency = 820f * (1f + 0.04f * (wobble[i] - 0.5f) + 0.006f * Mathf.Sin(2f * Mathf.PI * 7f * t));
                phase += 2.0 * Math.PI * frequency / synth.Rate;
                float tone = (float)(Math.Sin(phase) + 0.35 * Math.Sin(phase * 2.0) + 0.15 * Math.Sin(phase * 2.9));
                data[i] = tone * (0.65f + 0.35f * level[i]);
            }

            var scrub = synth.FilteredNoise(2.6f, Biquad.BandPass(synth.Rate, 2500f, 0.7f));
            Mix(data, scrub, 0.2f);
            return Loop(synth, data, 0.3f, 0.7f);
        }

        public static GeneratedSound GravelRoll()
        {
            var synth = new AudioSynth(EffectRate, 33);
            var data = Rumble(synth, 2.3f, 250f, 0.6f);
            AddGrains(synth, data, 260f, 1500f, 5000f, 0.0015f, 0.005f, 0.6f);
            return Loop(synth, data, 0.2f, 0.6f);
        }

        /// <summary>Gravel sliding: a dense crunch of stones over a low rumble.</summary>
        public static GeneratedSound GravelSlide()
        {
            var synth = new AudioSynth(EffectRate, 34);
            var data = Rumble(synth, 2.3f, 300f, 0.35f);
            AddGrains(synth, data, 700f, 1200f, 6000f, 0.002f, 0.008f, 1f);
            AddGrains(synth, data, 150f, 300f, 900f, 0.006f, 0.015f, 0.7f);
            return Loop(synth, data, 0.2f, 0.75f);
        }

        public static GeneratedSound DirtRoll()
        {
            var synth = new AudioSynth(EffectRate, 35);
            var data = Rumble(synth, 2.3f, 400f, 1f);
            AddGrains(synth, data, 70f, 400f, 1200f, 0.004f, 0.01f, 0.3f);
            return Loop(synth, data, 0.3f, 0.55f);
        }

        /// <summary>Dirt sliding: a dry, surging "shhh" with a few clods.</summary>
        public static GeneratedSound DirtSlide()
        {
            var synth = new AudioSynth(EffectRate, 36);
            var data = synth.FilteredNoise(2.4f, Biquad.BandPass(synth.Rate, 900f, 0.6f));
            Surge(synth, data, 6f, 0.5f);
            AddGrains(synth, data, 150f, 500f, 1800f, 0.004f, 0.01f, 0.6f);
            Mix(data, Rumble(synth, 2.4f, 300f, 1f), 0.3f);
            return Loop(synth, data, 0.3f, 0.7f);
        }

        public static GeneratedSound MudRoll()
        {
            var synth = new AudioSynth(EffectRate, 37);
            var data = Rumble(synth, 2.4f, 300f, 1f);
            AddSquelches(synth, data, 3f, 0.3f);
            return Loop(synth, data, 0.3f, 0.55f);
        }

        /// <summary>Mud sliding: a thick, surging wet churn with squelches and splats.</summary>
        public static GeneratedSound MudSlide()
        {
            var synth = new AudioSynth(EffectRate, 38);
            var data = synth.FilteredNoise(2.4f, Biquad.LowPass(synth.Rate, 700f, 0.9f));
            Surge(synth, data, 5f, 0.2f);
            AddSquelches(synth, data, 10f, 0.8f);
            int splats = Mathf.RoundToInt(6f * 2.4f);
            for (int s = 0; s < splats; s++)
            {
                var splat = synth.FilteredNoise(0.02f, Biquad.LowPass(synth.Rate, 1200f));
                Decay(synth, splat, 0.006f);
                AudioSynth.AddLooped(data, Mathf.RoundToInt(synth.Chance() * data.Length), splat, synth.Range(0.3f, 0.7f));
            }

            return Loop(synth, data, 0.3f, 0.7f);
        }

        public static GeneratedSound GrassRoll()
        {
            var synth = new AudioSynth(EffectRate, 39);
            var data = synth.FilteredNoise(2.4f, Biquad.BandPass(synth.Rate, 1800f, 0.7f), Biquad.LowPass(synth.Rate, 4500f));
            Surge(synth, data, 10f, 0.5f);
            Mix(data, Rumble(synth, 2.4f, 300f, 1f), 0.25f);
            return Loop(synth, data, 0.3f, 0.45f);
        }

        /// <summary>Grass sliding: a fluttering swish of blades with a soft thud underneath.</summary>
        public static GeneratedSound GrassSlide()
        {
            var synth = new AudioSynth(EffectRate, 40);
            var data = synth.FilteredNoise(2.4f, Biquad.BandPass(synth.Rate, 2400f, 0.6f), Biquad.LowPass(synth.Rate, 5500f));
            Surge(synth, data, 18f, 0.35f);
            Mix(data, Rumble(synth, 2.4f, 250f, 1f), 0.15f);
            return Loop(synth, data, 0.3f, 0.6f);
        }

        // ---------------------------------------------------------------- wind and scraping

        /// <summary>Wind rushing past: noise through a slowly wandering band, in slow gusts.</summary>
        public static GeneratedSound Wind()
        {
            var synth = new AudioSynth(EffectRate, 41);
            var data = synth.Buffer(7f);
            var centre = synth.Wander(data.Length, 0.4f);
            var gusts = synth.Wander(data.Length, 0.3f);
            var band = Biquad.BandPass(synth.Rate, 600f, 0.9f);
            for (int i = 0; i < data.Length; i++)
            {
                if (i % 64 == 0)
                    band.Retune(Biquad.BandPass(synth.Rate, 350f + 500f * centre[i], 0.9f));
                data[i] = band.Process(synth.Noise()) * (0.55f + 0.45f * gusts[i]);
            }

            Mix(data, Rumble(synth, 7f, 200f, 1f), 0.4f);
            return Loop(synth, data, 1f, 0.6f);
        }

        /// <summary>Metal scraping: grinding noise ringing at a handful of inharmonic metal resonances.</summary>
        public static GeneratedSound Scrape()
        {
            var synth = new AudioSynth(EffectRate, 42);
            var grind = synth.Buffer(1.8f);
            var level = synth.Wander(grind.Length, 35f);
            for (int i = 0; i < grind.Length; i++)
                grind[i] = synth.Noise() * (0.4f + 0.6f * level[i]);

            var data = new float[grind.Length];
            (float hz, float q, float gain)[] partials = { (1150f, 18f, 1f), (2330f, 22f, 0.8f), (3720f, 25f, 0.6f), (5100f, 20f, 0.4f) };
            foreach (var (hz, q, gain) in partials)
            {
                var ring = (float[])grind.Clone();
                AudioSynth.Filter(ring, Biquad.BandPass(synth.Rate, hz, q));
                Mix(data, ring, gain * 4f);
            }

            var broad = (float[])grind.Clone();
            AudioSynth.Filter(broad, Biquad.BandPass(synth.Rate, 3000f, 0.8f));
            Mix(data, broad, 0.3f);
            return Loop(synth, data, 0.3f, 0.7f);
        }

        // ---------------------------------------------------------------- one-shots

        public static GeneratedSound LightImpact()
        {
            var synth = new AudioSynth(EffectRate, 51);
            var data = synth.Tone(0.4f, 120f, 70f, 0.001f, 0.06f, 1f, 0.3f);
            var crack = synth.FilteredNoise(0.05f, Biquad.LowPass(synth.Rate, 2500f));
            Decay(synth, crack, 0.012f);
            AudioSynth.Add(data, 0, crack, 0.7f);
            AudioSynth.Add(data, 0, synth.Ring(1900f, 0.05f, 0.3f), 0.15f);
            AudioSynth.Add(data, 0, synth.Ring(2750f, 0.03f, 0.3f), 0.1f);
            return OneShot(synth, data, 0.85f);
        }

        /// <summary>A heavy crash: a deep boom, crunching panels, ringing metal and a second, smaller knock.</summary>
        public static GeneratedSound HeavyImpact()
        {
            var synth = new AudioSynth(EffectRate, 52);
            var data = synth.Tone(0.9f, 80f, 42f, 0.001f, 0.18f, 1f, 0.5f, 0.2f);
            var crunch = synth.FilteredNoise(0.5f, Biquad.BandPass(synth.Rate, 1200f, 0.6f));
            Decay(synth, crunch, 0.09f);
            AudioSynth.Add(data, 0, crunch, 0.8f);
            var debris = new float[synth.Samples(0.3f)];
            AddGrains(synth, debris, 200f, 800f, 4000f, 0.003f, 0.01f, 0.5f);
            AudioSynth.Add(data, 0, debris, 1f);
            AudioSynth.Add(data, 0, synth.Ring(870f, 0.25f, 0.9f), 0.25f);
            AudioSynth.Add(data, 0, synth.Ring(1430f, 0.2f, 0.9f), 0.2f);
            AudioSynth.Add(data, 0, synth.Ring(2600f, 0.12f, 0.9f), 0.12f);
            AudioSynth.Add(data, synth.Samples(0.07f), synth.Tone(0.4f, 95f, 60f, 0.001f, 0.07f, 1f, 0.3f), 0.5f);
            AudioSynth.Saturate(data, 1.4f);
            return OneShot(synth, data, 0.9f);
        }

        /// <summary>Landing: a low thump, the suspension's clunk and a spray of grit.</summary>
        public static GeneratedSound Landing()
        {
            var synth = new AudioSynth(EffectRate, 53);
            var data = synth.Tone(0.6f, 75f, 42f, 0.002f, 0.12f, 1f, 0.35f);
            int clunk = synth.Samples(0.015f);
            AudioSynth.Add(data, clunk, synth.Ring(330f, 0.035f, 0.2f), 0.45f);
            var click = synth.FilteredNoise(0.005f, Biquad.BandPass(synth.Rate, 1500f, 1f));
            AudioSynth.Add(data, clunk, click, 0.3f);
            var grit = synth.FilteredNoise(0.45f, Biquad.BandPass(synth.Rate, 2000f, 0.7f));
            for (int i = 0; i < grit.Length; i++)
                grit[i] *= AudioSynth.Envelope(i / (float)synth.Rate, 0.01f, 0.12f);
            AudioSynth.Add(data, 0, grit, 0.25f);
            return OneShot(synth, data, 0.85f);
        }

        /// <summary>A gear change: two mechanical clicks and a small thump.</summary>
        public static GeneratedSound GearShift()
        {
            var synth = new AudioSynth(EffectRate, 54);
            var data = synth.Tone(0.16f, 160f, 140f, 0.001f, 0.03f, 1f);
            for (int n = 0; n < 2; n++)
            {
                var click = synth.FilteredNoise(0.012f, Biquad.BandPass(synth.Rate, 2800f, 1.5f));
                Decay(synth, click, 0.004f);
                AudioSynth.Add(data, synth.Samples(0.045f * n), click, n == 0 ? 1f : 0.7f);
            }

            return OneShot(synth, data, 0.7f);
        }

        /// <summary>A backfire: a sharp pop, a low boom and a few crackles.</summary>
        public static GeneratedSound Backfire()
        {
            var synth = new AudioSynth(EffectRate, 55);
            var data = synth.Tone(0.35f, 95f, 55f, 0.001f, 0.07f, 1f, 0.4f);
            var pop = synth.FilteredNoise(0.12f, Biquad.LowPass(synth.Rate, 3500f));
            for (int i = 0; i < pop.Length; i++)
                pop[i] *= AudioSynth.Envelope(i / (float)synth.Rate, 0.0005f, 0.018f);
            AudioSynth.Add(data, 0, pop, 1.2f);
            for (int n = 0; n < 4; n++)
            {
                var crackle = synth.FilteredNoise(0.004f, Biquad.BandPass(synth.Rate, 2000f, 1f));
                AudioSynth.Add(data, synth.Samples(synth.Range(0.03f, 0.2f)), crackle, 0.3f);
            }

            AudioSynth.Saturate(data, 2f);
            return OneShot(synth, data, 0.9f);
        }

        // ---------------------------------------------------------------- menus

        public static GeneratedSound Navigate()
        {
            var synth = new AudioSynth(EffectRate, 61);
            return OneShot(synth, synth.Tone(0.07f, 1760f, 1760f, 0.001f, 0.018f, 1f, 0.15f), 0.5f);
        }

        public static GeneratedSound Confirm()
        {
            var synth = new AudioSynth(EffectRate, 62);
            var data = synth.Tone(0.2f, 880f, 880f, 0.002f, 0.04f, 1f, 0.25f, 0.1f);
            AudioSynth.Add(data, synth.Samples(0.05f), synth.Tone(0.15f, 1320f, 1320f, 0.002f, 0.06f, 1f, 0.25f, 0.1f), 0.9f);
            return OneShot(synth, data, 0.6f);
        }

        public static GeneratedSound Back()
        {
            var synth = new AudioSynth(EffectRate, 63);
            var data = synth.Tone(0.19f, 700f, 700f, 0.002f, 0.04f, 1f, 0.25f);
            AudioSynth.Add(data, synth.Samples(0.05f), synth.Tone(0.14f, 467f, 467f, 0.002f, 0.05f, 1f, 0.25f), 0.9f);
            return OneShot(synth, data, 0.55f);
        }

        public static GeneratedSound Error()
        {
            var synth = new AudioSynth(EffectRate, 64);
            var data = synth.Buffer(0.2f);
            for (int n = 0; n < 2; n++)
                AudioSynth.Add(data, synth.Samples(0.1f * n), synth.Tone(0.08f, 155f, 150f, 0.002f, 0.05f, 1f, 0f, 0.33f, 0f, 0.2f), 1f);
            return OneShot(synth, data, 0.55f);
        }

        /// <summary>A purchase: three bell notes going up, with a shimmer.</summary>
        public static GeneratedSound Purchase()
        {
            var synth = new AudioSynth(EffectRate, 65);
            var data = synth.Buffer(0.8f);
            float[] notes = { 1046.5f, 1318.5f, 1568f };
            for (int n = 0; n < notes.Length; n++)
                AudioSynth.Add(data, synth.Samples(0.07f * n), Bell(synth, notes[n], 0.6f), 1f);
            var shimmer = synth.FilteredNoise(0.6f, Biquad.HighPass(synth.Rate, 6000f));
            Decay(synth, shimmer, 0.15f);
            AudioSynth.Add(data, 0, shimmer, 0.08f);
            return OneShot(synth, data, 0.65f);
        }

        /// <summary>A whoosh: noise through a band sweeping upwards, swelling and fading.</summary>
        public static GeneratedSound Switch()
        {
            var synth = new AudioSynth(EffectRate, 66);
            var data = synth.Buffer(0.3f);
            var band = Biquad.BandPass(synth.Rate, 500f, 1.2f);
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)data.Length;
                if (i % 64 == 0)
                    band.Retune(Biquad.BandPass(synth.Rate, Mathf.Lerp(500f, 2600f, t), 1.2f));
                float swell = Mathf.Sin(Mathf.PI * t);
                data[i] = band.Process(synth.Noise()) * swell * swell;
            }

            return OneShot(synth, data, 0.5f);
        }

        // ---------------------------------------------------------------- race

        public static GeneratedSound CountdownTick()
        {
            var synth = new AudioSynth(EffectRate, 71);
            return OneShot(synth, synth.Tone(0.3f, 587.33f, 587.33f, 0.003f, 0.14f, 1f, 0.3f, 0.12f), 0.7f);
        }

        public static GeneratedSound Go()
        {
            var synth = new AudioSynth(EffectRate, 72);
            var data = synth.Tone(0.9f, 1174.66f, 1174.66f, 0.003f, 0.45f, 1f, 0.3f, 0.12f);
            AudioSynth.Add(data, 0, synth.Tone(0.9f, 587.33f, 587.33f, 0.003f, 0.45f, 1f, 0.3f), 0.5f);
            return OneShot(synth, data, 0.8f);
        }

        public static GeneratedSound Lap()
        {
            var synth = new AudioSynth(EffectRate, 73);
            var data = synth.Buffer(0.7f);
            AudioSynth.Add(data, 0, Bell(synth, 784f, 0.6f), 1f);
            AudioSynth.Add(data, synth.Samples(0.1f), Bell(synth, 1174.66f, 0.6f), 0.9f);
            return OneShot(synth, data, 0.6f);
        }

        public static GeneratedSound FinalLap()
        {
            var synth = new AudioSynth(EffectRate, 74);
            var data = synth.Buffer(1f);
            float[] notes = { 784f, 987.77f, 1174.66f };
            for (int n = 0; n < notes.Length; n++)
                AudioSynth.Add(data, synth.Samples(0.09f * n), Bell(synth, notes[n], 0.8f), 1f);
            return OneShot(synth, data, 0.65f);
        }

        /// <summary>The finish: a bright brass-like chord building note by note, over a cymbal swell.</summary>
        public static GeneratedSound Finish()
        {
            var synth = new AudioSynth(EffectRate, 75);
            var data = synth.Buffer(2.4f);
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int n = 0; n < notes.Length; n++)
            {
                var note = synth.Tone(2.2f, notes[n], notes[n], 0.02f, 1.2f, 1f, 0.5f, 0.33f, 0.25f, 0.2f, 0.16f);
                AudioSynth.Add(data, synth.Samples(0.08f * n), note, 1f);
            }

            AudioSynth.Filter(data, Biquad.LowPass(synth.Rate, 2500f, 0.8f));
            var cymbal = synth.FilteredNoise(2.4f, Biquad.HighPass(synth.Rate, 5000f));
            for (int i = 0; i < cymbal.Length; i++)
                cymbal[i] *= AudioSynth.Envelope(i / (float)synth.Rate, 0.05f, 0.8f);
            Mix(data, cymbal, 0.05f);
            return OneShot(synth, data, 0.8f, fadeOut: 0.3f);
        }

        /// <summary>The results opening: a soft whoosh into a gentle chord.</summary>
        public static GeneratedSound Results()
        {
            var synth = new AudioSynth(EffectRate, 76);
            var data = synth.Buffer(1.4f);
            foreach (float note in new[] { 261.63f, 392f, 659.25f })
                AudioSynth.Add(data, 0, synth.Tone(1.4f, note, note, 0.15f, 0.8f, 1f, 0.2f), 1f);
            AudioSynth.Add(data, 0, Switch().Data, 0.5f);
            return OneShot(synth, data, 0.5f, fadeOut: 0.2f);
        }

        // ---------------------------------------------------------------- music

        /// <summary>The menu loop: 96 bpm, eight bars of A minor, F, C, G with pads, a soft bass, drums and a quiet arpeggio.</summary>
        public static GeneratedSound MenuMusic()
        {
            var synth = new AudioSynth(MusicRate, 81);
            const float beat = 60f / 96f;
            var data = new float[synth.Samples(beat * 32f)];
            int[][] chords = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 60, 64, 67 }, new[] { 55, 59, 62 } };
            for (int bar = 0; bar < 8; bar++)
            {
                var chord = chords[bar / 2];
                float barStart = bar * 4f * beat;
                if (bar % 2 == 0)
                {
                    foreach (int note in chord)
                        AudioSynth.AddLooped(data, synth.Samples(barStart), Pad(synth, Frequency(note), beat * 8f), 0.2f);
                }

                for (int b = 0; b < 4; b++)
                {
                    float at = barStart + b * beat;
                    AudioSynth.AddLooped(data, synth.Samples(at), Bass(synth, Frequency(chord[0] - 12), beat * 0.9f, 500f), 0.45f);
                    if (b % 2 == 0)
                        AudioSynth.AddLooped(data, synth.Samples(at), Kick(synth), 0.7f);
                    else
                        AudioSynth.AddLooped(data, synth.Samples(at), Snare(synth), 0.3f);
                    for (int h = 0; h < 2; h++)
                        AudioSynth.AddLooped(data, synth.Samples(at + h * beat * 0.5f), Hat(synth), h == 0 ? 0.12f : 0.07f);
                    for (int a = 0; a < 2; a++)
                        AudioSynth.AddLooped(data, synth.Samples(at + a * beat * 0.5f),
                            Pluck(synth, Frequency(chord[(b * 2 + a) % 3] + 12), 0.3f), 0.1f);
                }
            }

            return Music(data, synth.Rate);
        }

        /// <summary>The race loop: 128 bpm, eight bars of E minor, C, D, B minor with a driving bass, four-to-the-floor drums, stabs and an arpeggio.</summary>
        public static GeneratedSound RaceMusic()
        {
            var synth = new AudioSynth(MusicRate, 82);
            const float beat = 60f / 128f;
            var data = new float[synth.Samples(beat * 32f)];
            int[][] chords = { new[] { 52, 55, 59 }, new[] { 48, 52, 55 }, new[] { 50, 54, 57 }, new[] { 47, 50, 54 } };
            for (int bar = 0; bar < 8; bar++)
            {
                var chord = chords[bar / 2];
                float barStart = bar * 4f * beat;
                foreach (int note in chord)
                    AudioSynth.AddLooped(data, synth.Samples(barStart), Pluck(synth, Frequency(note + 12), 0.5f), 0.12f);
                if (bar % 2 == 0)
                {
                    foreach (int note in chord)
                        AudioSynth.AddLooped(data, synth.Samples(barStart), Pad(synth, Frequency(note), beat * 8f), 0.14f);
                }

                for (int b = 0; b < 4; b++)
                {
                    float at = barStart + b * beat;
                    AudioSynth.AddLooped(data, synth.Samples(at), Kick(synth), 0.8f);
                    if (b % 2 == 1)
                        AudioSynth.AddLooped(data, synth.Samples(at), Snare(synth), 0.45f);
                    AudioSynth.AddLooped(data, synth.Samples(at + beat * 0.5f), Hat(synth), 0.16f);
                    for (int s = 0; s < 4; s++)
                    {
                        float sixteenth = at + s * beat * 0.25f;
                        int bassNote = chord[0] - 24 + (s == 2 ? 12 : 0);
                        AudioSynth.AddLooped(data, synth.Samples(sixteenth), Bass(synth, Frequency(bassNote), beat * 0.22f, 700f), 0.4f);
                        AudioSynth.AddLooped(data, synth.Samples(sixteenth), Pluck(synth, Frequency(chord[(b * 4 + s) % 3] + 24), 0.15f), 0.06f);
                    }
                }
            }

            return Music(data, synth.Rate);
        }

        // ---------------------------------------------------------------- instruments and helpers

        private static float[] Kick(AudioSynth synth)
        {
            var kick = synth.Tone(0.35f, 110f, 45f, 0.001f, 0.09f, 1f);
            var click = synth.FilteredNoise(0.004f, Biquad.HighPass(synth.Rate, 2000f));
            AudioSynth.Add(kick, 0, click, 0.2f);
            return kick;
        }

        private static float[] Snare(AudioSynth synth)
        {
            var snare = synth.FilteredNoise(0.25f, Biquad.BandPass(synth.Rate, 1800f, 0.7f));
            Decay(synth, snare, 0.07f);
            AudioSynth.Add(snare, 0, synth.Tone(0.25f, 190f, 170f, 0.001f, 0.05f, 1f), 0.5f);
            return snare;
        }

        private static float[] Hat(AudioSynth synth)
        {
            var hat = synth.FilteredNoise(0.06f, Biquad.HighPass(synth.Rate, 7000f));
            Decay(synth, hat, 0.015f);
            return hat;
        }

        private static float[] Bass(AudioSynth synth, float frequency, float seconds, float brightness)
        {
            var bass = synth.Tone(seconds + 0.05f, frequency, frequency, 0.005f, seconds * 0.8f, 1f, 0.5f, 0.33f, 0.25f, 0.2f, 0.16f, 0.14f, 0.12f);
            AudioSynth.Filter(bass, Biquad.LowPass(synth.Rate, brightness, 0.9f));
            synth.FadeEnds(bass, 0.002f, 0.03f);
            return bass;
        }

        private static float[] Pad(AudioSynth synth, float frequency, float seconds)
        {
            var pad = synth.Tone(seconds, frequency * 0.996f, frequency * 0.996f, seconds * 0.15f, seconds * 3f, 1f, 0.5f, 0.33f, 0.25f, 0.2f, 0.16f);
            AudioSynth.Add(pad, 0, synth.Tone(seconds, frequency * 1.004f, frequency * 1.004f, seconds * 0.15f, seconds * 3f, 1f, 0.5f, 0.33f, 0.25f), 1f);
            AudioSynth.Filter(pad, Biquad.LowPass(synth.Rate, 1200f, 0.7f));
            synth.FadeEnds(pad, 0.01f, 0.3f);
            return pad;
        }

        private static float[] Pluck(AudioSynth synth, float frequency, float seconds)
        {
            var pluck = synth.Tone(seconds, frequency, frequency, 0.002f, seconds * 0.35f, 1f, 0.5f, 0.25f, 0.12f);
            synth.FadeEnds(pluck, 0.002f, 0.02f);
            return pluck;
        }

        private static float[] Bell(AudioSynth synth, float frequency, float seconds)
        {
            var bell = synth.Tone(seconds, frequency, frequency, 0.001f, seconds * 0.4f, 1f, 0f, 0.35f);
            AudioSynth.Add(bell, 0, synth.Tone(seconds, frequency * 2.76f, frequency * 2.76f, 0.001f, seconds * 0.13f, 1f), 0.25f);
            return bell;
        }

        private static float Frequency(int midiNote) => 440f * Mathf.Pow(2f, (midiNote - 69) / 12f);

        /// <summary>Soft low rumble: brown noise, low-passed.</summary>
        private static float[] Rumble(AudioSynth synth, float seconds, float cutoff, float gain)
        {
            var rumble = synth.BrownNoise(seconds);
            AudioSynth.Filter(rumble, Biquad.LowPass(synth.Rate, cutoff));
            AudioSynth.Normalize(rumble, gain);
            return rumble;
        }

        /// <summary>Scatters short bursts of band-passed noise (stones, grit) through a buffer, wrapping round its end.</summary>
        private static void AddGrains(AudioSynth synth, float[] buffer, float perSecond, float minHz, float maxHz, float minDecay,
            float maxDecay, float gain)
        {
            int count = Mathf.RoundToInt(perSecond * buffer.Length / synth.Rate);
            for (int n = 0; n < count; n++)
            {
                float hz = Mathf.Exp(Mathf.Lerp(Mathf.Log(minHz), Mathf.Log(maxHz), synth.Chance()));
                float decay = synth.Range(minDecay, maxDecay);
                var grain = synth.FilteredNoise(decay * 5f, Biquad.BandPass(synth.Rate, hz, 2.5f));
                Decay(synth, grain, decay);
                float level = synth.Range(0.3f, 1f);
                AudioSynth.AddLooped(buffer, Mathf.RoundToInt(synth.Chance() * buffer.Length), grain, gain * level * level * 3f);
            }
        }

        /// <summary>Wet squelches: short falling low tones.</summary>
        private static void AddSquelches(AudioSynth synth, float[] buffer, float perSecond, float gain)
        {
            int count = Mathf.RoundToInt(perSecond * buffer.Length / synth.Rate);
            for (int n = 0; n < count; n++)
            {
                var squelch = synth.Tone(synth.Range(0.05f, 0.09f), synth.Range(180f, 260f), synth.Range(60f, 90f), 0.005f, 0.025f, 1f, 0.4f);
                AudioSynth.AddLooped(buffer, Mathf.RoundToInt(synth.Chance() * buffer.Length), squelch, gain * synth.Range(0.3f, 1f));
            }
        }

        /// <summary>Slow, irregular swelling of the level between <paramref name="floor"/> and 1.</summary>
        private static void Surge(AudioSynth synth, float[] data, float changesPerSecond, float floor)
        {
            var level = synth.Wander(data.Length, changesPerSecond);
            for (int i = 0; i < data.Length; i++)
                data[i] *= Mathf.Lerp(floor, 1f, level[i]);
        }

        private static void Decay(AudioSynth synth, float[] data, float decay)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] *= Mathf.Exp(-i / (float)synth.Rate / decay);
        }

        private static float[] Sum(float[] a, float gainA, float[] b, float gainB, float[] c, float gainC)
        {
            var sum = new float[a.Length];
            for (int i = 0; i < sum.Length; i++)
                sum[i] = a[i] * gainA + b[i] * gainB + c[i] * gainC;
            return sum;
        }

        private static void Mix(float[] into, float[] other, float gain)
        {
            for (int i = 0; i < into.Length && i < other.Length; i++)
                into[i] += other[i] * gain;
        }

        private static GeneratedSound Loop(AudioSynth synth, float[] data, float overlap, float peak)
        {
            var loop = synth.Seamless(data, overlap);
            AudioSynth.Normalize(loop, peak);
            return new GeneratedSound(loop, synth.Rate, loop: true);
        }

        private static GeneratedSound OneShot(AudioSynth synth, float[] data, float peak, float fadeOut = 0.02f)
        {
            synth.FadeEnds(data, 0.001f, fadeOut);
            AudioSynth.Normalize(data, peak);
            synth.FadeEnds(data, 0.001f, 0.005f);
            return new GeneratedSound(data, synth.Rate, loop: false);
        }

        private static GeneratedSound Music(float[] data, int rate)
        {
            AudioSynth.Saturate(data, 1.1f);
            AudioSynth.Normalize(data, 0.8f);
            return new GeneratedSound(data, rate, loop: true);
        }
    }
}
