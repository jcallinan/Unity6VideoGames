using System;
using System.Threading;
using UnityEngine;

public sealed class SkyfortSounds
{
    public AudioClip Laser;
    public AudioClip EnemyShot;
    public AudioClip Explosion;
    public AudioClip BigExplosion;
    public AudioClip Fuel;
    public AudioClip Radar;
    public AudioClip LowFuel;
    public AudioClip Beep;
    public AudioClip Go;
    public AudioClip BossHit;
    public AudioClip BossAlarm;
    public AudioClip LevelClear;
    public AudioClip GameOver;
    public AudioClip Engine;
}

// Synthesizes every sound in code, so no audio files are needed (WebGL friendly).
// Unity 6: the sample math runs on a worker thread via Awaitable.BackgroundThreadAsync, then
// Awaitable.MainThreadAsync hops back to create the AudioClips (Unity API calls must be on the main thread).
public static class SkyfortAudio
{
    private const int SampleRate = 44100;

    public static async Awaitable<SkyfortSounds> LoadAsync(CancellationToken token)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        // WebGL has no worker threads, so the synthesis simply stays on the main thread there.
        await Awaitable.BackgroundThreadAsync();
#endif
        var noise = new System.Random(1979);
        var laser = Synth(0.14f, t =>
        {
            var f = 1900f * Mathf.Exp(-14f * t) + 320f;
            return Mathf.Exp(-16f * t) * Square(f, t) * 0.32f;
        });
        var enemyShot = Synth(0.2f, t =>
        {
            var f = 620f - 1500f * t;
            return Mathf.Exp(-11f * t) * Saw(f, t) * 0.22f;
        });
        var explosion = Noise(noise, 0.75f, 5.5f, 0.12f, 70f, 0.55f);
        var bigExplosion = Noise(noise, 1.8f, 2.4f, 0.06f, 45f, 0.7f);
        var fuel = Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.055f, 0.5f);
        var radar = Arpeggio(new[] { 1318.5f, 1760f, 1318.5f, 2093f }, 0.07f, 0.55f);
        var lowFuel = Synth(0.32f, t => (t < 0.14f ? Square(880f, t) : Square(660f, t)) * 0.18f * Mathf.Min(t / 0.004f, 1f));
        var beep = Beep(660f);
        var go = Beep(1320f);
        var bossHit = Synth(0.3f, t =>
            Mathf.Exp(-12f * t) * (Mathf.Sin(2f * Mathf.PI * 310f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 737f * t)
                                    + 0.4f * Mathf.Sin(2f * Mathf.PI * 1171f * t)) * 0.3f);
        var bossAlarm = Synth(1.6f, t =>
        {
            var f = 420f + 260f * Mathf.Abs(Mathf.Sin(Mathf.PI * 2.5f * t));
            return Square(f, t) * 0.16f * Mathf.Min(t / 0.02f, 1f);
        });
        var levelClear = Arpeggio(new[] { 392f, 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.1f, 1.2f);
        var gameOver = Arpeggio(new[] { 392f, 329.63f, 261.63f, 196f, 130.81f }, 0.22f, 1.6f);
        var engineState = 0f;
        var engine = Synth(4f, t =>
        {
            // Whole cycles per 4 seconds so the loop is seamless.
            engineState += (float)(noise.NextDouble() * 2.0 - 1.0 - engineState) * 0.04f;
            var throb = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 3f * t);
            var hum = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.5f + Saw(110f, t) * 0.12f * throb;
            return (hum + engineState * 0.9f) * 0.2f;
        }, false);

        await Awaitable.MainThreadAsync();
        token.ThrowIfCancellationRequested();
        return new SkyfortSounds
        {
            Laser = Clip("Laser", laser),
            EnemyShot = Clip("Enemy shot", enemyShot),
            Explosion = Clip("Explosion", explosion),
            BigExplosion = Clip("Big explosion", bigExplosion),
            Fuel = Clip("Fuel", fuel),
            Radar = Clip("Radar", radar),
            LowFuel = Clip("Low fuel", lowFuel),
            Beep = Clip("Countdown beep", beep),
            Go = Clip("Go", go),
            BossHit = Clip("Boss hit", bossHit),
            BossAlarm = Clip("Boss alarm", bossAlarm),
            LevelClear = Clip("Level clear", levelClear),
            GameOver = Clip("Game over", gameOver),
            Engine = Clip("Engine", engine)
        };
    }

    private static float[] Beep(float frequency) => Synth(0.22f, t =>
    {
        var env = Mathf.Min(t / 0.008f, 1f) * Mathf.Exp(-9f * t);
        return env * (Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * frequency * t)) * 0.4f;
    });

    private static float[] Noise(System.Random random, float seconds, float decay, float smoothing, float rumble, float gain)
    {
        var state = 0f;
        return Synth(seconds, t =>
        {
            state += ((float)(random.NextDouble() * 2.0 - 1.0) - state) * smoothing;
            var env = Mathf.Exp(-decay * t) * Mathf.Min(t / 0.003f, 1f);
            return env * (state * 2.2f + Mathf.Sin(2f * Mathf.PI * rumble * t) * 0.5f) * gain;
        });
    }

    private static float[] Arpeggio(float[] notes, float step, float length) => Synth(length, t =>
    {
        var sample = 0f;
        for (var i = 0; i < notes.Length; i++)
        {
            var local = t - i * step;
            if (local < 0f) continue;
            sample += Mathf.Exp(-4f * local) * Mathf.Min(local / 0.004f, 1f) * Bell(notes[i], local);
        }

        return sample * 0.26f;
    });

    private static float Bell(float frequency, float t)
    {
        var w = 2f * Mathf.PI * frequency * t;
        return Mathf.Sin(w) + 0.35f * Mathf.Sin(2f * w) * Mathf.Exp(-5f * t) + 0.15f * Mathf.Sin(3.01f * w);
    }

    private static float Square(float frequency, float t)
    {
        var w = 2f * Mathf.PI * frequency * t;
        return Mathf.Sin(w) + Mathf.Sin(3f * w) / 3f + Mathf.Sin(5f * w) / 5f;
    }

    private static float Saw(float frequency, float t)
    {
        var w = 2f * Mathf.PI * frequency * t;
        return Mathf.Sin(w) - Mathf.Sin(2f * w) / 2f + Mathf.Sin(3f * w) / 3f - Mathf.Sin(4f * w) / 4f;
    }

    private static float[] Synth(float seconds, Func<float, float> generator, bool fadeEnds = true)
    {
        var count = Mathf.CeilToInt(seconds * SampleRate);
        var data = new float[count];
        for (var i = 0; i < count; i++)
        {
            var sample = generator((float)i / SampleRate);
            if (fadeEnds)
            {
                sample *= Mathf.Min(1f, (count - i) / (0.01f * SampleRate));
            }

            data[i] = Mathf.Clamp(sample, -0.95f, 0.95f);
        }

        return data;
    }

    private static AudioClip Clip(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
