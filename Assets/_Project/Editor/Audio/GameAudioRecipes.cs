using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunwayPanic.AudioTools
{
    /// <summary>How a generated clip is imported (see GameAudioGenerator.ApplyImportSettings).</summary>
    internal enum ClipKind { Effect, Loop, Ambience, Music }

    /// <summary>
    /// The sound design of every generated clip, as recipes for <see cref="Signal"/>. Each recipe is deterministic
    /// (fixed seeds), so regenerating gives identical files. Effects are 44.1 kHz; environment sounds 22.05 kHz.
    /// The background is the airport itself (wind, distant jets, PA, far gulls) under quiet music that builds with the
    /// waves.
    /// </summary>
    internal static class GameAudioRecipes
    {
        public const string Root = "Assets/_Project/Audio";

        public static IEnumerable<(string path, ClipKind kind, Func<Signal> make)> All()
        {
            // ---------- Weapons ----------
            for (int v = 0; v < 2; v++) { int s = v; yield return (Sfx("Weapons", $"SFX_Shot_Pistol_{v + 1}"), ClipKind.Effect, () => Pistol(s)); }
            for (int v = 0; v < 3; v++) { int s = v; yield return (Sfx("Weapons", $"SFX_Shot_Rifle_{v + 1}"), ClipKind.Effect, () => Rifle(s)); }
            yield return (Sfx("Weapons", "SFX_Shot_Sniper"), ClipKind.Effect, Sniper);
            yield return (Sfx("Weapons", "SFX_Shot_RPG"), ClipKind.Effect, Rpg);
            yield return (Sfx("Weapons", "SFX_Reload_Start"), ClipKind.Effect, ReloadStart);
            yield return (Sfx("Weapons", "SFX_Reload_Finish"), ClipKind.Effect, ReloadFinish);
            yield return (Sfx("Weapons", "SFX_Weapon_Equip"), ClipKind.Effect, Equip);
            for (int v = 0; v < 3; v++) { int s = v; yield return (Sfx("Weapons", $"SFX_Bullet_Impact_{v + 1}"), ClipKind.Effect, () => Impact(s)); }

            // ---------- Player ----------
            for (int v = 0; v < 4; v++) { int s = v; yield return (Sfx("Player", $"SFX_Footstep_Walk_{v + 1}"), ClipKind.Effect, () => Footstep(s, false)); }
            for (int v = 0; v < 4; v++) { int s = v; yield return (Sfx("Player", $"SFX_Footstep_Run_{v + 1}"), ClipKind.Effect, () => Footstep(s, true)); }
            for (int v = 0; v < 3; v++) { int s = v; yield return (Sfx("Player", $"SFX_Jump_{v + 1}"), ClipKind.Effect, () => Jump(s)); }
            yield return (Sfx("Player", "SFX_Land"), ClipKind.Effect, Land);
            for (int v = 0; v < 3; v++) { int s = v; yield return (Sfx("Player", $"SFX_Player_Hurt_{v + 1}"), ClipKind.Effect, () => Hurt(s)); }
            yield return (Sfx("Player", "SFX_Player_Death"), ClipKind.Effect, Death);
            yield return (Sfx("Player", "SFX_Respawn"), ClipKind.Effect, Respawn);
            yield return (Sfx("Player", "SFX_Heartbeat"), ClipKind.Loop, Heartbeat);
            yield return (Sfx("Player", "SFX_Medkit"), ClipKind.Effect, Medkit);
            yield return (Sfx("Player", "SFX_Shield_On"), ClipKind.Effect, ShieldOn);
            yield return (Sfx("Player", "SFX_Shield_Hum"), ClipKind.Loop, ShieldHum);
            yield return (Sfx("Player", "SFX_Shield_Off"), ClipKind.Effect, ShieldOff);
            yield return (Sfx("Player", "SFX_Pickup"), ClipKind.Effect, Pickup);

            // ---------- World ----------
            yield return (Sfx("World", "SFX_Door_Open"), ClipKind.Effect, DoorOpen);
            yield return (Sfx("World", "SFX_Door_Close"), ClipKind.Effect, DoorClose);
            yield return (Sfx("World", "SFX_Barricade_Scrape"), ClipKind.Loop, Scrape);
            yield return (Sfx("World", "SFX_Barricade_Settle"), ClipKind.Effect, Settle);

            // ---------- UI and game ----------
            yield return (Sfx("UI", "SFX_UI_Click"), ClipKind.Effect, Click);
            yield return (Sfx("UI", "SFX_Coin"), ClipKind.Effect, Coin);
            yield return (Sfx("UI", "SFX_Purchase"), ClipKind.Effect, Purchase);
            yield return (Sfx("UI", "SFX_Purchase_Denied"), ClipKind.Effect, Denied);
            yield return (Sfx("UI", "SFX_Wave_Start"), ClipKind.Effect, WaveStart);
            yield return (Sfx("UI", "SFX_Wave_Cleared"), ClipKind.Effect, WaveCleared);
            yield return (Sfx("UI", "SFX_Victory"), ClipKind.Effect, Victory);
            yield return (Sfx("UI", "SFX_Game_Over"), ClipKind.Effect, GameOver);

            // ---------- Birds ----------
            for (int v = 0; v < 3; v++) { int s = v; yield return (Sfx("Birds", $"SFX_Bird_Flap_{v + 1}"), ClipKind.Effect, () => Flap(s)); }
            yield return (Sfx("Birds", "SFX_Bird_Dive"), ClipKind.Effect, Dive);
            for (int v = 0; v < 3; v++) { int s = v; yield return (Sfx("Birds", $"SFX_Bird_Hit_{v + 1}"), ClipKind.Effect, () => BirdHit(s)); }
            yield return (Sfx("Birds", "SFX_Bird_Death"), ClipKind.Effect, BirdDeath);
            for (int v = 0; v < 2; v++) { int s = v; yield return (Sfx("Birds", $"SFX_Gull_Call_{v + 1}"), ClipKind.Effect, () => GullCall(s)); }
            for (int v = 0; v < 2; v++) { int s = v; yield return (Sfx("Birds", $"SFX_Pigeon_Coo_{v + 1}"), ClipKind.Effect, () => PigeonCoo(s)); }
            yield return (Sfx("Birds", "SFX_Hawk_Scream"), ClipKind.Effect, HawkScream);

            // ---------- Environment and wave music ----------
            yield return (Amb("AMB_Airport"), ClipKind.Ambience, AirportBed);
            yield return (Amb("AMB_Wind"), ClipKind.Ambience, Wind);
            yield return ($"{Root}/Music/MUS_Ambient.wav", ClipKind.Music, AmbientPad);
            yield return ($"{Root}/Music/MUS_Tension_1.wav", ClipKind.Music, TensionPulse);
            yield return ($"{Root}/Music/MUS_Tension_2.wav", ClipKind.Music, TensionDrive);
            yield return ($"{Root}/Music/MUS_Tension_3.wav", ClipKind.Music, TensionBattle);
            yield return ($"{Root}/Music/MUS_Tension_4.wav", ClipKind.Music, TensionPanic);
            yield return (Amb("SFX_Amb_Jet_Flyover"), ClipKind.Effect, JetFlyover);
            yield return (Amb("SFX_Amb_PA_Chime"), ClipKind.Effect, PaChime);
            for (int v = 0; v < 2; v++) { int s = v; yield return (Amb($"SFX_Amb_Gull_Far_{v + 1}"), ClipKind.Effect, () => FarGull(s)); }
            yield return (Amb("SFX_Amb_Fence_Rattle"), ClipKind.Effect, FenceRattle);
        }

        private static string Amb(string name) => $"{Root}/Ambience/{name}.wav";

        private static string Sfx(string folder, string name) => $"{Root}/SFX/{folder}/{name}.wav";

        // Shorthands.
        private static Func<float, float> Perc(float a, float d) => Signal.Perc(a, d);
        private static Func<float, float> Glide(float a, float b, float d) => Signal.Glide(a, b, d);
        private static Func<float, float> Const(float v) => Signal.Const(v);
        private static Func<float, float> Swell(float a, float r, float d) => Signal.Swell(a, r, d);
        private static float N(int midi) => Signal.Midi(midi);

        // =====================================================================
        // Weapons: a gunshot = a sharp crack (high noise), a body (low noise), a thump (falling sine) and the
        // environment answering (echo). Bigger guns: longer, lower, more echo.
        // =====================================================================

        private static Signal Pistol(int v) => new Signal(0.6f)
            .Noise(0f, 0.03f, Perc(0.0005f, 0.006f), 1f, 11 + v, 0f, 2500f)
            .Noise(0f, 0.25f, Perc(0.001f, 0.05f), 0.8f, 21 + v, 1800f + v * 200f)
            .Tone(0f, 0.2f, Glide(160f - v * 10f, 55f, 0.08f), Perc(0.001f, 0.06f), 0.9f)
            .Echo(0.11f, 0.22f, 2).Saturate(1.6f).Normalise().FadeOut(0.05f);

        private static Signal Rifle(int v) => new Signal(0.45f)
            .Noise(0f, 0.02f, Perc(0.0004f, 0.004f), 1f, 31 + v, 0f, 2200f)
            .Noise(0f, 0.18f, Perc(0.001f, 0.035f), 0.9f, 41 + v, 2200f - v * 150f)
            .Tone(0f, 0.15f, Glide(140f + v * 8f, 60f, 0.06f), Perc(0.001f, 0.045f), 0.9f)
            .Echo(0.09f, 0.18f, 1).Saturate(2f).Normalise().FadeOut(0.04f);

        private static Signal Sniper() => new Signal(1.8f)
            .Noise(0f, 0.04f, Perc(0.0004f, 0.008f), 1f, 51, 0f, 1500f)
            .Noise(0f, 0.4f, Perc(0.001f, 0.09f), 0.9f, 52, 1500f)
            .Tone(0f, 0.4f, Glide(120f, 40f, 0.12f), Perc(0.001f, 0.12f), 1f)
            .Noise(0f, 1.2f, Perc(0.01f, 0.5f), 0.35f, 53, 250f)
            .Echo(0.28f, 0.35f, 3).Saturate(1.8f).Normalise().FadeOut(0.3f);

        private static Signal Rpg() => new Signal(1.4f)
            .Tone(0f, 0.4f, Glide(90f, 45f, 0.15f), Perc(0.002f, 0.15f), 1f)
            .Band(0f, 1.0f, Glide(2500f, 400f, 0.9f), 1.2f, Swell(0.02f, 0.7f, 1f), 0.8f, 61)
            .Noise(0f, 0.6f, Perc(0.005f, 0.4f), 0.25f, 62, 0f, 4000f)
            .Echo(0.2f, 0.25f, 2).Saturate(1.5f).Normalise().FadeOut(0.2f);

        private static Signal ReloadStart() => new Signal(0.45f)
            .Metal(0f, new[] { 1250f, 2830f, 4400f }, 0.015f, 0.8f)
            .Band(0.08f, 0.25f, Const(2400f), 2f, Swell(0.03f, 0.15f, 0.25f), 0.4f, 71)
            .Metal(0.3f, new[] { 900f, 2100f, 3600f }, 0.02f, 0.6f)
            .Normalise().FadeOut(0.05f);

        private static Signal ReloadFinish() => new Signal(0.55f)
            .Metal(0f, new[] { 700f, 1900f, 3300f }, 0.025f, 1f)
            .Metal(0.22f, new[] { 1500f, 3400f, 5100f }, 0.012f, 0.8f)
            .Metal(0.32f, new[] { 1100f, 2600f, 4200f }, 0.02f, 0.9f)
            .Normalise().FadeOut(0.05f);

        private static Signal Equip() => new Signal(0.4f)
            .Band(0f, 0.25f, Glide(800f, 1600f, 0.25f), 0.9f, Swell(0.05f, 0.15f, 0.25f), 0.6f, 81)
            .Metal(0.22f, new[] { 1600f, 3300f }, 0.012f, 0.7f)
            .Normalise().FadeOut(0.05f);

        private static Signal Impact(int v) => new Signal(0.35f)
            .Noise(0f, 0.15f, Perc(0.001f, 0.03f), 1f, 91 + v, 1200f)
            .Metal(0.002f, new[] { 2200f + v * 300f, 3700f, 5200f }, 0.02f, 0.35f)
            .Band(0.01f, 0.2f, Const(1800f - v * 200f), 1f, Perc(0.005f, 0.06f), 0.3f, 95 + v)
            .Normalise().FadeOut(0.05f);

        // =====================================================================
        // Player
        // =====================================================================

        private static Signal Footstep(int v, bool run)
        {
            var s = new Signal(0.25f)
                .Noise(0f, 0.2f, Perc(0.002f, run ? 0.025f : 0.03f), 1f, 101 + v + (run ? 10 : 0), run ? 1600f : 900f, 60f)
                .Tone(0f, 0.1f, Glide(95f - v * 5f, 60f, 0.05f), Perc(0.001f, 0.03f), run ? 0.6f : 0.5f)
                .Band(0.02f, 0.08f, Const(2500f + v * 250f), 1.5f, Perc(0.005f, 0.02f), run ? 0.35f : 0.25f, 111 + v);
            return s.Normalise().FadeOut(0.03f);
        }

        // A real jump: the shoe pushing off (a soft low thud and a short gritty scuff on the tarmac), clothes moving
        // as the body rises, and a small breath out. Quiet and short, with three takes so repeats don't stand out.
        private static Signal Jump(int v) => new Signal(0.35f)
            .Tone(0f, 0.08f, Glide(90f - v * 6f, 55f, 0.04f), Perc(0.001f, 0.022f), 0.55f)
            .Noise(0f, 0.09f, Swell(0.004f, 0.06f, 0.09f), 0.7f, 121 + v, 3200f, 350f)
            .Band(0.005f, 0.05f, Const(2000f + v * 300f), 2f, Perc(0.002f, 0.015f), 0.3f, 124 + v)
            .Band(0.03f, 0.2f, Glide(1400f + v * 100f, 2400f, 0.2f), 1.2f, Swell(0.03f, 0.12f, 0.2f), 0.12f, 127 + v)
            .Band(0.02f, 0.24f, Const(1100f - v * 60f), 0.7f, Swell(0.02f, 0.16f, 0.24f), 0.1f, 130 + v)
            .Normalise(0.8f).FadeOut(0.05f);

        private static Signal Land() => new Signal(0.4f)
            .Noise(0f, 0.3f, Perc(0.002f, 0.07f), 1f, 131, 500f)
            .Tone(0f, 0.25f, Glide(80f, 45f, 0.1f), Perc(0.001f, 0.09f), 0.8f)
            .Band(0.02f, 0.1f, Const(2400f), 1.5f, Perc(0.005f, 0.03f), 0.35f, 132)
            .Normalise().FadeOut(0.05f);

        // A male "oof": buzzy voice through an "o/u" vowel.
        private static readonly (float, float, float)[] VowelO = { (500f, 150f, 1f), (900f, 200f, 0.8f), (2500f, 400f, 0.3f) };
        private static readonly (float, float, float)[] VowelA = { (750f, 150f, 1f), (1150f, 200f, 0.8f), (2600f, 400f, 0.3f) };

        private static Signal Hurt(int v) => new Signal(0.35f)
            .Voice(0f, 0.28f, Glide(170f + v * 15f, 120f, 0.28f), Swell(0.01f, 0.15f, 0.28f), 1f, VowelO, 0.25f, 0.2f, 141 + v)
            .Normalise().FadeOut(0.05f);

        private static Signal Death() => new Signal(1f)
            .Voice(0f, 0.9f, Glide(150f, 85f, 0.9f), Swell(0.02f, 0.6f, 0.9f), 1f, VowelA, 0.4f, 0.3f, 151)
            .Normalise().FadeOut(0.1f);

        private static Signal Respawn()
        {
            var s = new Signal(1.4f);
            int[] notes = { 72, 76, 79, 84 }; // C E G C
            for (int i = 0; i < notes.Length; i++)
                s.Tone(i * 0.08f, 0.9f, Const(N(notes[i])), Perc(0.002f, 0.5f), 0.4f);
            s.Noise(0f, 0.9f, Swell(0.2f, 0.5f, 0.9f), 0.12f, 161, 0f, 6000f);
            return s.Echo(0.12f, 0.3f, 3).Normalise().FadeOut(0.2f);
        }

        private static Signal Heartbeat() => new Signal(0.9f)
            .Tone(0f, 0.2f, Glide(70f, 45f, 0.08f), Perc(0.003f, 0.06f), 1f)
            .Tone(0.25f, 0.2f, Glide(60f, 40f, 0.08f), Perc(0.003f, 0.05f), 0.7f)
            .Loop(0.9f).Normalise(0.8f);

        private static Signal Medkit()
        {
            var s = new Signal(1.1f)
                .Band(0f, 0.3f, Const(3000f), 2f, t => (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 40f * t)) * Swell(0.02f, 0.05f, 0.3f)(t), 0.6f, 171);
            int[] notes = { 81, 85, 88 }; // A C# E — healing
            for (int i = 0; i < notes.Length; i++)
                s.Tone(0.35f + i * 0.1f, 0.6f, Const(N(notes[i])), Perc(0.002f, 0.35f), 0.35f);
            return s.Echo(0.1f, 0.25f, 2).Normalise().FadeOut(0.15f);
        }

        private static Signal ShieldOn() => new Signal(0.9f)
            .Tone(0f, 0.8f, Glide(220f, 880f, 0.6f), Swell(0.02f, 0.3f, 0.8f), 0.6f, Wave.Saw, 6)
            .Tone(0f, 0.8f, Glide(440f, 1760f, 0.6f), Swell(0.02f, 0.3f, 0.8f), 0.3f)
            .Noise(0f, 0.8f, Swell(0.3f, 0.3f, 0.8f), 0.15f, 181, 0f, 5000f)
            .LowPass(4000f).Normalise().FadeOut(0.1f);

        // Frequencies are whole multiples of 0.5 Hz, so the sound repeats exactly every 2 s; rendering past that and
        // crossfading hides the low-pass filter starting from silence.
        private static Signal ShieldHum() => new Signal(2.4f)
            .Tone(0f, 2.4f, Const(110f), Const(1f), 0.5f, Wave.Saw, 6)
            .Tone(0f, 2.4f, Const(165f), Const(1f), 0.3f)
            .Tone(0f, 2.4f, Const(220.5f), Const(1f), 0.15f)
            .LowPass(1200f).LoopCrossfade(2f, 0.4f).Normalise(0.7f);

        private static Signal ShieldOff() => new Signal(0.7f)
            .Tone(0f, 0.55f, Glide(800f, 160f, 0.5f), Swell(0.01f, 0.35f, 0.55f), 0.6f, Wave.Saw, 6)
            .Noise(0f, 0.3f, Perc(0.002f, 0.1f), 0.3f, 191, 0f, 3000f)
            .LowPass(3500f).Normalise().FadeOut(0.1f);

        private static Signal Pickup() => new Signal(0.6f)
            .Tone(0f, 0.4f, Const(N(88)), Perc(0.002f, 0.18f), 0.5f, Wave.Triangle)
            .Tone(0.07f, 0.5f, Const(N(95)), Perc(0.002f, 0.3f), 0.5f, Wave.Triangle)
            .Normalise().FadeOut(0.1f);

        // =====================================================================
        // World
        // =====================================================================

        private static Signal DoorOpen() => new Signal(1.4f)
            .Metal(0f, new[] { 900f, 2400f }, 0.02f, 0.6f)
            .Tone(0.05f, 1.1f, t => Glide(170f, 260f, 1.1f)(t) * (1f + 0.08f * Mathf.Sin(2f * Mathf.PI * 7f * t)),
                Swell(0.15f, 0.3f, 1.1f), 0.5f, Wave.Saw, 20)
            .Band(0.05f, 1.1f, Const(900f), 2f, Swell(0.15f, 0.3f, 1.1f), 0.15f, 201)
            .LowPass(2500f).Normalise().FadeOut(0.1f);

        private static Signal DoorClose() => new Signal(0.8f)
            .Noise(0f, 0.4f, Perc(0.002f, 0.12f), 1f, 211, 400f)
            .Tone(0f, 0.3f, Glide(90f, 50f, 0.1f), Perc(0.001f, 0.1f), 0.8f)
            .Metal(0.03f, new[] { 1200f, 3100f }, 0.015f, 0.6f)
            .Echo(0.07f, 0.2f, 1).Normalise().FadeOut(0.1f);

        private static Signal Scrape() => new Signal(1.9f)
            .Band(0f, 1.9f, t => 700f + 250f * Mathf.Sin(2f * Mathf.PI * 1.25f * t), 1.5f, Const(0.7f), 1f, 221)
            .Noise(0f, 1.9f, Const(1f), 0.3f, 222, 300f)
            .Band(0f, 1.9f, Const(2500f), 3f, Const(1f), 0.15f, 223)
            .LoopCrossfade(1.6f, 0.3f).Normalise(0.7f);

        private static Signal Settle() => new Signal(0.7f)
            .Noise(0f, 0.5f, Perc(0.003f, 0.15f), 1f, 231, 250f)
            .Tone(0f, 0.4f, Glide(70f, 40f, 0.15f), Perc(0.001f, 0.12f), 0.9f)
            .Echo(0.09f, 0.2f, 1).Normalise().FadeOut(0.1f);

        // =====================================================================
        // UI and game
        // =====================================================================

        private static Signal Click() => new Signal(0.08f)
            .Tone(0f, 0.06f, Const(1800f), Perc(0.0005f, 0.012f), 0.6f)
            .Noise(0f, 0.02f, Perc(0.0003f, 0.004f), 0.3f, 241, 0f, 3000f)
            .Normalise(0.7f).FadeOut(0.01f);

        private static Signal Coin() => new Signal(0.7f)
            .Tone(0f, 0.3f, Const(N(88)), Perc(0.002f, 0.12f), 0.5f, Wave.Triangle)
            .Tone(0.08f, 0.6f, Const(N(95)), Perc(0.002f, 0.4f), 0.5f, Wave.Triangle)
            .Echo(0.09f, 0.2f, 2).Normalise().FadeOut(0.1f);

        private static Signal Purchase()
        {
            var s = new Signal(0.9f).Metal(0f, new[] { 1800f, 2700f }, 0.02f, 0.4f);
            foreach (int note in new[] { 84, 88, 91 }) // C E G
                s.Tone(0.05f, 0.7f, Const(N(note)), Perc(0.002f, 0.5f), 0.3f, Wave.Triangle);
            s.Tone(0.15f, 0.6f, Const(N(96)), Perc(0.002f, 0.3f), 0.2f);
            return s.Normalise().FadeOut(0.1f);
        }

        private static Signal Denied() => new Signal(0.45f)
            .Tone(0f, 0.14f, Const(110f), Swell(0.005f, 0.02f, 0.14f), 0.5f, Wave.Square, 15)
            .Tone(0.2f, 0.14f, Const(110f), Swell(0.005f, 0.02f, 0.14f), 0.5f, Wave.Square, 15)
            .LowPass(3000f).Normalise(0.8f);

        // Airport alert: two tones alternating, like an apron warning horn.
        // "Here they come" (no siren, no rising pitch): a swell of air like a reversed cymbal into a deep boom and a
        // dark low brass chord that hangs.
        private static Signal WaveStart()
        {
            const float hit = 0.9f;
            var s = new Signal(3.6f)
                .Noise(0f, hit, t => Mathf.Pow(t / hit, 3f), 0.5f, 141, 6000f, 800f)
                .Tone(hit, 1.6f, Glide(80f, 32f, 0.5f), Perc(0.003f, 0.5f), 1f)
                .Noise(hit, 1.2f, Perc(0.002f, 0.25f), 0.5f, 142, 1200f);
            foreach (int note in new[] { 33, 45, 52, 46 }) // A1, A2, E3 and a grinding B-flat
                s.Tone(hit, 2.6f, Const(N(note)), Swell(0.03f, 1.6f, 2.6f), note == 46 ? 0.12f : 0.25f, Wave.Saw, 12);
            return s.LowPass(2200f).Echo(0.3f, 0.3f, 2).Saturate(1.3f).Normalise().FadeOut(0.4f);
        }

        private static Signal WaveCleared()
        {
            var s = new Signal(1.8f);
            int[] notes = { 72, 76, 79, 84 };
            for (int i = 0; i < notes.Length; i++)
                s.Tone(i * 0.12f, 0.6f, Const(N(notes[i])), Perc(0.005f, 0.5f), 0.35f, Wave.Saw, 8);
            foreach (int note in new[] { 84, 88, 91 })
                s.Tone(0.48f, 1f, Const(N(note)), Swell(0.02f, 0.6f, 1f), 0.25f, Wave.Saw, 8);
            return s.LowPass(3500f).Echo(0.15f, 0.25f, 2).Normalise().FadeOut(0.2f);
        }

        private static Signal Victory()
        {
            var s = new Signal(3.8f);
            (int note, float at, float len)[] fanfare = { (67, 0f, 0.2f), (72, 0.18f, 0.2f), (76, 0.36f, 0.2f), (79, 0.54f, 1.8f) };
            foreach ((int note, float at, float len) in fanfare)
            {
                s.Tone(at, len, Const(N(note)), Swell(0.02f, 0.4f, len), 0.4f, Wave.Saw, 10, 5f, 0.004f);
                s.Tone(at, len, Const(N(note - 12)), Swell(0.02f, 0.4f, len), 0.25f, Wave.Saw, 10);
            }
            foreach (int note in new[] { 72, 76 })
                s.Tone(0.54f, 1.8f, Const(N(note)), Swell(0.05f, 0.6f, 1.8f), 0.2f, Wave.Saw, 10);
            Drum(s, 0f, 0.8f);
            Drum(s, 0.54f, 1f);
            return s.LowPass(3000f).Echo(0.2f, 0.3f, 3).Normalise().FadeOut(0.5f);
        }

        // A kick-drum hit: a sine thump gliding down.
        private static void Drum(Signal s, float t, float amp) =>
            s.Tone(t, 0.35f, Glide(120f, 45f, 0.08f), Perc(0.002f, 0.16f), amp);

        // "You lost": one soft low drum, then a slow, sad melody falling down the A-minor scale over the chords
        // F, E and A minor, ending on a long, quiet low A. Mournful rather than loud — no crash, no boom.
        private static Signal GameOver()
        {
            var s = new Signal(6f)
                .Tone(0f, 1.2f, Glide(60f, 40f, 0.3f), Perc(0.004f, 0.4f), 0.6f);

            (int note, float at, float len)[] melody =
            {
                (76, 0.2f, 0.55f), (74, 0.7f, 0.55f), (72, 1.2f, 0.55f), (71, 1.7f, 0.9f), (69, 2.6f, 3f),
            };
            foreach ((int note, float at, float len) in melody)
            {
                s.Tone(at, len, Const(N(note)), Swell(0.04f, len * 0.6f, len), 0.32f, Wave.Triangle, 8, 5f, 0.004f);
                s.Tone(at, len, Const(N(note) * 2f), Perc(0.01f, 0.3f), 0.05f);
            }

            (int[] notes, float at, float len)[] chords =
            {
                (new[] { 41, 53, 57, 60 }, 0.2f, 1.6f), // F
                (new[] { 40, 52, 56, 59 }, 1.7f, 1.0f), // E
                (new[] { 33, 45, 52, 57, 60 }, 2.6f, 3.2f), // A minor, held
            };
            foreach ((int[] notes, float at, float len) in chords)
                foreach (int note in notes)
                    s.Tone(at, len + 0.3f, Const(N(note)), Swell(0.25f, len * 0.6f, len + 0.3f), 0.09f, Wave.Saw, 8);

            return s.LowPass(2200f).Echo(0.4f, 0.3f, 3).Normalise().FadeOut(1.2f);
        }

        // =====================================================================
        // Birds
        // =====================================================================

        private static Signal Flap(int v) => new Signal(0.18f)
            .Noise(0f, 0.15f, Perc(0.004f, 0.035f), 0.9f, 251 + v, 900f)
            .Band(0f, 0.1f, Const(400f + v * 80f), 1.2f, Perc(0.003f, 0.03f), 0.4f, 255 + v)
            .Tone(0f, 0.08f, Glide(110f, 70f, 0.05f), Perc(0.002f, 0.03f), 0.3f)
            .Normalise().FadeOut(0.03f);

        private static Signal Dive() => new Signal(1.2f)
            .Band(0f, 1.1f, Glide(400f, 2200f, 1.1f), 1.6f, Swell(0.6f, 0.25f, 1.1f), 1f, 261)
            .Noise(0f, 1.1f, Swell(0.5f, 0.2f, 1.1f), 0.3f, 262, 0f, 3000f)
            .Normalise().FadeOut(0.1f);

        private static readonly (float, float, float)[] BirdThroat = { (1400f, 400f, 1f), (2800f, 600f, 0.6f) };

        private static Signal BirdHit(int v) => new Signal(0.25f)
            .Voice(0f, 0.18f, Glide(950f + v * 80f, 650f, 0.18f), Perc(0.005f, 0.08f), 1f, BirdThroat, 0.5f, 0.25f, 271 + v)
            .Noise(0f, 0.08f, Perc(0.002f, 0.05f), 0.3f, 275 + v, 0f, 1500f)
            .Normalise().FadeOut(0.03f);

        private static Signal BirdDeath() => new Signal(0.6f)
            .Voice(0f, 0.5f, Glide(900f, 420f, 0.5f), Swell(0.01f, 0.3f, 0.5f), 1f, BirdThroat, 0.6f, 0.3f, 281)
            .Noise(0f, 0.12f, Perc(0.002f, 0.06f), 0.3f, 282, 0f, 1500f)
            .Normalise().FadeOut(0.08f);

        private static readonly (float, float, float)[] GullThroat = { (2200f, 500f, 1f), (3400f, 700f, 0.5f) };

        // "Kee-ow": a rising then falling, nasal cry. Variant 2 is a laughing "kek-kek-kek".
        private static Signal GullCall(int v)
        {
            var s = new Signal(1.1f);
            if (v == 0)
            {
                s.Voice(0f, 0.45f, t => t < 0.12f ? Mathf.Lerp(1150f, 1500f, t / 0.12f) : Mathf.Lerp(1500f, 950f, (t - 0.12f) / 0.33f),
                    Swell(0.02f, 0.15f, 0.45f), 1f, GullThroat, 0.15f, 0.1f, 291);
                s.Voice(0.55f, 0.3f, Glide(1300f, 900f, 0.3f), Swell(0.02f, 0.12f, 0.3f), 0.8f, GullThroat, 0.15f, 0.1f, 292);
            }
            else
            {
                for (int k = 0; k < 4; k++)
                    s.Voice(k * 0.17f, 0.11f, Glide(1400f, 1150f, 0.11f), Perc(0.005f, 0.05f), 0.9f, GullThroat, 0.25f, 0.12f, 293 + k);
            }

            return s.Normalise().FadeOut(0.1f);
        }

        private static readonly (float, float, float)[] PigeonThroat = { (350f, 120f, 1f), (700f, 200f, 0.3f) };

        // "Coo — COOO — coo": soft and low with a slight wobble.
        private static Signal PigeonCoo(int v)
        {
            float shift = v * 20f;
            var s = new Signal(1.4f)
                .Voice(0f, 0.3f, Glide(330f + shift, 300f + shift, 0.3f), Swell(0.08f, 0.1f, 0.3f), 0.7f, PigeonThroat, 0.05f, 0.15f, 301, 6)
                .Voice(0.4f, 0.55f, t => (300f + shift + 80f * Mathf.Sin(Mathf.PI * t / 0.55f)) * (1f + 0.02f * Mathf.Sin(2f * Mathf.PI * 9f * t)),
                    Swell(0.08f, 0.2f, 0.55f), 1f, PigeonThroat, 0.05f, 0.12f, 302, 6)
                .Voice(1.02f, 0.3f, Glide(320f + shift, 290f + shift, 0.3f), Swell(0.06f, 0.12f, 0.3f), 0.6f, PigeonThroat, 0.05f, 0.15f, 303, 6);
            return s.LowPass(1800f).Normalise().FadeOut(0.08f);
        }

        // The raptor's "kreee-ar": high, long and rough, with sky echo.
        private static Signal HawkScream() => new Signal(1.6f)
            .Voice(0f, 1.2f, t => t < 0.25f ? Mathf.Lerp(2100f, 2650f, t / 0.25f) : Mathf.Lerp(2650f, 1900f, (t - 0.25f) / 0.95f),
                Swell(0.03f, 0.5f, 1.2f), 1f, new[] { (3000f, 700f, 1f), (4500f, 900f, 0.5f) }, 0.55f, 0.25f, 311, 10)
            .Echo(0.25f, 0.25f, 2).Normalise().FadeOut(0.15f);

        // =====================================================================
        // Environment: looping beds, distant one-off events and a soft pad. Loops are 22.05 kHz; every slow change is
        // periodic over the loop length and the end is crossfaded into the start, so they loop without a seam.
        // =====================================================================

        private const int AmbienceRate = 22050;

        private static Func<float, float> Cycle(float length, float k, float phase = 0f) =>
            t => Mathf.Sin(2f * Mathf.PI * k * t / length + phase);

        // =====================================================================
        // Music: one quiet pad that always plays, plus four tension layers that join one per wave.
        // Every piece shares the same chords, tempo (120 bpm, 4 bars per chord) and 64 s length, and they are started
        // together, so any mix of layers fits. Each loops seamlessly (the tail folds back onto the start).
        // =====================================================================

        private const float MusicBeat = 0.5f;
        private const float ChordLength = 8f;

        private static readonly (int bass, int[] pad)[] MusicChords =
        {
            (45, new[] { 57, 60, 64 }), (41, new[] { 53, 57, 60 }), (48, new[] { 52, 55, 60 }), (43, new[] { 55, 59, 62 }),
            (45, new[] { 57, 60, 64 }), (41, new[] { 53, 57, 60 }), (38, new[] { 50, 53, 57 }), (40, new[] { 52, 55, 59 }),
        };

        private static float MusicLength => ChordLength * MusicChords.Length;

        // The base: slow warm chords that swell into each other (no drums, no melody), a soft low root, and now and
        // then one faint bell note.
        private static Signal AmbientPad()
        {
            float length = MusicLength, hold = ChordLength + 3f;
            var pad = new Signal(length + 4f, AmbienceRate);
            var bells = new Signal(length + 4f, AmbienceRate);

            for (int c = 0; c < MusicChords.Length; c++)
            {
                float t0 = c * ChordLength;
                foreach (int note in MusicChords[c].pad)
                {
                    pad.Tone(t0, hold, Const(N(note) * 1.002f), Swell(3f, 4f, hold), 0.1f, Wave.Triangle, 6, 0.2f, 0.002f);
                    pad.Tone(t0, hold, Const(N(note) * 0.998f), Swell(3.5f, 4f, hold), 0.08f);
                }
                pad.Tone(t0, hold, Const(N(MusicChords[c].bass)), Swell(2.5f, 4f, hold), 0.12f);

                // One faint bell per chord, on alternating chord tones, at slightly uneven times.
                int bell = MusicChords[c].pad[c % 3] + 12;
                float at = t0 + (c % 2 == 0 ? 2.5f : 4.5f);
                bells.Tone(at, 3f, Const(N(bell)), Perc(0.01f, 1.2f), 0.05f)
                    .Tone(at, 3f, Const(N(bell) * 2.76f), Perc(0.005f, 0.4f), 0.008f);
            }

            return new Signal(length + 4f, AmbienceRate)
                .Mix(pad.LowPass(1200f), 0f, 1f)
                .Mix(bells.LowPass(3000f).Echo(0.45f, 0.35f, 3), 0f, 1f)
                .Loop(length).Normalise(0.6f);
        }

        // Tension 1 (every wave): danger is close. A low staccato string ostinato in sixteenths on the chord root that
        // leans on the dark half-step above it, a sub drone, and a heavy heartbeat drum.
        private static Signal TensionPulse()
        {
            var strings = new Signal(MusicLength + 3f, AmbienceRate);
            var drums = new Signal(MusicLength + 3f, AmbienceRate);
            int[] lean = { 0, 0, 0, 1, 0, 0, 1, 0 }; // semitones above the root, per sixteenth
            for (int c = 0; c < MusicChords.Length; c++)
            {
                float t0 = c * ChordLength;
                int root = MusicChords[c].bass;
                for (int x = 0; x < 64; x++)
                {
                    float t = t0 + x * MusicBeat * 0.25f;
                    float accent = x % 4 == 0 ? 1f : 0.65f;
                    Staccato(strings, t, root + lean[x % 8], 0.3f * accent);
                    Staccato(strings, t, root + 12 + lean[x % 8], 0.12f * accent);
                }

                strings.Tone(t0, ChordLength + 0.5f, Const(N(root - 12)), Swell(0.5f, 0.6f, ChordLength + 0.5f), 0.25f);
                for (int b = 0; b < 16; b += 2) // heartbeat: a strong beat and a softer echo
                {
                    Drum(drums, t0 + b * MusicBeat, 0.9f);
                    Drum(drums, t0 + b * MusicBeat + 0.24f, 0.5f);
                }
            }

            return new Signal(MusicLength + 3f, AmbienceRate)
                .Mix(strings.LowPass(1800f), 0f, 1f)
                .Mix(drums, 0f, 1f)
                .Loop(MusicLength).Saturate(1.3f).Normalise(0.75f);
        }

        // Tension 2 (second wave on): the chase. Big syncopated war drums, low strings driving in eighths on the chord
        // fifth, and a dark brass chord swelling through each bar. No sweeps or warbles — nothing alarm-like.
        private static Signal TensionDrive()
        {
            var drums = new Signal(MusicLength + 3f, AmbienceRate);
            var strings = new Signal(MusicLength + 3f, AmbienceRate);
            var brass = new Signal(MusicLength + 3f, AmbienceRate);
            float[] hits = { 0f, 0.75f, 1.5f, 2f, 2.75f, 3.5f }; // in beats, per bar of 4
            for (int c = 0; c < MusicChords.Length; c++)
            {
                float t0 = c * ChordLength;
                for (int bar = 0; bar < 4; bar++)
                {
                    foreach (float h in hits)
                        WarDrum(drums, t0 + (bar * 4 + h) * MusicBeat, h == 0f ? 0.9f : 0.6f, c * 64 + bar * 8 + (int)(h * 4));
                    foreach (int note in MusicChords[c].pad)
                        brass.Tone(t0 + bar * 2f, 2f, Const(N(note - 12)), Swell(1.2f, 0.4f, 2f), 0.08f, Wave.Saw, 10);
                }

                for (int e = 0; e < 16; e++)
                    Staccato(strings, t0 + e * MusicBeat * 0.5f, MusicChords[c].pad[2] - 12, e % 2 == 0 ? 0.3f : 0.2f);
            }

            return new Signal(MusicLength + 3f, AmbienceRate)
                .Mix(drums.LowPass(3000f), 0f, 1f)
                .Mix(strings.LowPass(1600f), 0f, 1f)
                .Mix(brass.LowPass(1200f), 0f, 1f)
                .Loop(MusicLength).Saturate(1.3f).Normalise(0.75f);
        }

        // Tension 3 (late waves): all-out. A driving kit (kick every beat, snare on 2 and 4, eighth hats), low brass
        // stabs pushing on the off-beats, and a crash on every chord change.
        private static Signal TensionBattle()
        {
            var drums = new Signal(MusicLength + 3f, AmbienceRate);
            var brass = new Signal(MusicLength + 3f, AmbienceRate);
            for (int c = 0; c < MusicChords.Length; c++)
            {
                float t0 = c * ChordLength;
                drums.Noise(t0, 1.6f, Perc(0.002f, 0.5f), 0.35f, 950 + c, 0f, 4500f); // crash
                for (int b = 0; b < 16; b++)
                {
                    float t = t0 + b * MusicBeat;
                    Drum(drums, t, 1f);
                    if (b % 2 == 1)
                    {
                        drums.Band(t, 0.25f, Const(1800f), 0.7f, Perc(0.001f, 0.1f), 0.55f, 960 + c * 16 + b);
                        drums.Tone(t, 0.12f, Const(190f), Perc(0.001f, 0.05f), 0.3f);
                    }
                    for (int h = 0; h < 2; h++)
                        drums.Noise(t + h * MusicBeat * 0.5f, 0.05f, Perc(0.0005f, 0.015f), h == 1 ? 0.18f : 0.1f, 1200 + c * 32 + b * 2 + h, 0f, 7000f);
                }

                for (int bar = 0; bar < 4; bar++)
                    foreach (float off in new[] { 0f, 1.5f, 2.5f, 3.5f })
                        foreach (int note in MusicChords[c].pad)
                            brass.Tone(t0 + (bar * 4 + off) * MusicBeat, 0.3f, Const(N(note - 12)), Perc(0.01f, 0.14f), 0.12f, Wave.Saw, 12);
            }

            return new Signal(MusicLength + 3f, AmbienceRate)
                .Mix(drums.LowPass(8000f), 0f, 1f)
                .Mix(brass.LowPass(1500f), 0f, 1f)
                .Loop(MusicLength).Saturate(1.4f).Normalise(0.75f);
        }

        // Tension 4 (final wave): fear. A racing heartbeat (a heavy lub-dub on every beat), a clock-like tick in
        // sixteenths, low strings grinding a half-step apart, and a deep drone — all swelling inside every
        // chord, so each chord change feels like a fresh jolt. WaveMusic also speeds the music up as the wave drags on.
        private static Signal TensionPanic()
        {
            var heart = new Signal(MusicLength + 3f, AmbienceRate);
            var tick = new Signal(MusicLength + 3f, AmbienceRate);
            var dread = new Signal(MusicLength + 3f, AmbienceRate);
            Func<float, float> build = t => Mathf.Clamp01(t / ChordLength) * Mathf.Clamp01((ChordLength + 0.3f - t) / 0.3f);
            for (int c = 0; c < MusicChords.Length; c++)
            {
                float t0 = c * ChordLength;
                (int bass, int[] pad) chord = MusicChords[c];
                for (int b = 0; b < 16; b++)
                {
                    float t = t0 + b * MusicBeat;
                    heart.Tone(t, 0.3f, Glide(75f, 40f, 0.06f), Perc(0.002f, 0.09f), 1f);
                    heart.Tone(t + 0.17f, 0.3f, Glide(65f, 38f, 0.06f), Perc(0.002f, 0.07f), 0.65f);
                }

                for (int x = 0; x < 64; x++)
                    tick.Band(t0 + x * MusicBeat * 0.25f, 0.03f, Const(x % 4 == 0 ? 3800f : 4600f), 6f, Perc(0.0005f, 0.006f),
                        x % 4 == 0 ? 0.5f : 0.25f, 1500 + c * 64 + x);

                float hold = ChordLength + 0.3f;
                foreach (int note in new[] { chord.pad[0], chord.pad[0] + 1, chord.pad[2] - 12 }) // a low, grinding half-step
                    dread.Tone(t0, hold, Const(N(note)), build, 0.1f, Wave.Saw, 8);
                dread.Tone(t0, hold, Const(N(chord.bass - 12)), build, 0.3f, Wave.Saw, 6);
            }

            return new Signal(MusicLength + 3f, AmbienceRate)
                .Mix(heart.LowPass(400f), 0f, 1f)
                .Mix(tick, 0f, 0.6f)
                .Mix(dread.LowPass(1500f), 0f, 1f)
                .Loop(MusicLength).Saturate(1.3f).Normalise(0.75f);
        }

        // A short bowed-string note (bright saw, quick decay).
        private static void Staccato(Signal s, float t, int note, float amp) =>
            s.Tone(t, 0.12f, Const(N(note)), Perc(0.004f, 0.05f), amp, Wave.Saw, 10);

        // A big low drum: deep body plus a skin slap.
        private static void WarDrum(Signal s, float t, float amp, int seed)
        {
            s.Tone(t, 0.6f, Glide(110f, 55f, 0.12f), Perc(0.002f, 0.25f), amp);
            s.Band(t, 0.1f, Const(700f), 1f, Perc(0.001f, 0.03f), amp * 0.4f, 1300 + seed);
        }

        // =====================================================================
        // The airport far away: jet engines idling on distant stands, a low building hum and soft open air.
        // =====================================================================

        private static Signal AirportBed()
        {
            const float length = 30f;
            Func<float, float> slow = Cycle(length, 1f), drift = Cycle(length, 3f, 1f);
            return new Signal(length + 2f, AmbienceRate)
                .Noise(0f, length + 2f, t => 0.7f + 0.2f * drift(t), 0.45f, 501, 300f)
                .Band(0f, length + 2f, t => 150f + 50f * slow(t), 0.9f, t => 0.35f + 0.25f * Mathf.Max(0f, slow(t)), 0.45f, 503)
                .Band(0f, length + 2f, t => 2400f + 300f * drift(t), 6f, t => 0.25f + 0.1f * slow(t), 0.025f, 505) // far turbine whine
                .Tone(0f, length + 2f, Const(60f), t => 0.6f + 0.2f * drift(t), 0.04f)
                .LoopCrossfade(length, 2f).Normalise(0.6f);
        }

        // A light breeze in the open: soft low air that swells and settles slowly and unevenly (no whistle, no sharp
        // gusts) — it should be felt more than noticed.
        private static Signal Wind()
        {
            const float length = 60f;
            Func<float, float> g1 = Cycle(length, 2f), g2 = Cycle(length, 5f, 1.3f), g3 = Cycle(length, 9f, 2.1f);
            float Breeze(float t) => Mathf.Clamp01(0.55f + 0.2f * g1(t) + 0.12f * g2(t) + 0.06f * g3(t));
            return new Signal(length + 4f, AmbienceRate)
                .Noise(0f, length + 4f, Breeze, 0.6f, 511, 220f)
                .Band(0f, length + 4f, t => 260f + 160f * Breeze(t), 0.8f, t => Breeze(t) * Breeze(t), 0.12f, 512)
                .LowPass(900f).LoopCrossfade(length, 4f).Normalise(0.6f);
        }

        // A jet climbing out far away: a rumble that builds, roars past and fades, pitch sinking as it leaves.
        private static Signal JetFlyover() => new Signal(9f, AmbienceRate)
            .Noise(0f, 9f, Swell(4f, 4.5f, 9f), 0.8f, 521, 600f)
            .Band(0f, 9f, Glide(1300f, 700f, 9f), 1.5f, Swell(4.5f, 4f, 9f), 0.25f, 522)
            .Band(0f, 9f, Glide(3200f, 2200f, 9f), 8f, Swell(4f, 3.5f, 9f), 0.03f, 523)
            .LowPass(2500f).Normalise(0.8f);

        // The terminal PA "ding-dong", heard across the apron (echoing off buildings).
        private static Signal PaChime() => new Signal(3f, AmbienceRate)
            .Tone(0f, 1.4f, Const(N(79)), Perc(0.005f, 0.6f), 0.5f)
            .Tone(0f, 1.4f, Const(N(79) * 2.76f), Perc(0.002f, 0.2f), 0.08f)
            .Tone(0.65f, 1.6f, Const(N(75)), Perc(0.005f, 0.7f), 0.5f)
            .Tone(0.65f, 1.6f, Const(N(75) * 2.76f), Perc(0.002f, 0.2f), 0.08f)
            .LowPass(3000f).Echo(0.23f, 0.35f, 3).Normalise(0.7f).FadeOut(0.3f);

        // A gull far off: thin and soft (the bird prefabs make the close calls).
        private static Signal FarGull(int v) => new Signal(1.6f, AmbienceRate)
            .Voice(0f, 0.45f, Glide(1400f + v * 90f, 1000f, 0.45f), Swell(0.02f, 0.15f, 0.45f), 1f, GullThroat, 0.15f, 0.1f, 530 + v)
            .Voice(0.6f, 0.3f, Glide(1300f + v * 90f, 950f, 0.3f), Swell(0.02f, 0.12f, 0.3f), v == 1 ? 0.7f : 0f, GullThroat, 0.15f, 0.1f, 540 + v)
            .LowPass(2200f).Echo(0.3f, 0.2f, 2).Normalise(0.7f).FadeOut(0.2f);

        // A chain-link fence shaking in a gust: quick metallic ticks in a short burst.
        private static Signal FenceRattle()
        {
            var s = new Signal(1.6f);
            var random = new System.Random(550);
            for (int k = 0; k < 14; k++)
            {
                float t = k * 0.08f + (float)random.NextDouble() * 0.04f;
                float amp = Mathf.Sin(Mathf.PI * k / 14f) * (0.6f + 0.4f * (float)random.NextDouble());
                s.Band(t, 0.06f, Const(2600f + 1200f * (float)random.NextDouble()), 6f, Perc(0.001f, 0.012f), amp, 551 + k);
            }

            return s.Noise(0f, 1.6f, Swell(0.3f, 0.8f, 1.6f), 0.08f, 570, 900f).Normalise(0.7f).FadeOut(0.1f);
        }
    }
}
