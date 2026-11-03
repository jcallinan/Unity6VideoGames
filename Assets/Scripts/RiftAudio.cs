using UnityEngine;

// Synthesizes every sound at startup, so no audio files or imports are needed (WebGL friendly).
public static class RiftAudio
{
    private const int SampleRate = 44100;

    public static AudioClip Pickup() => Build("Crystal pickup", 0.45f, t =>
    {
        var env = Mathf.Exp(-6f * t);
        var sweep = 880f + 880f * Mathf.Min(t / 0.08f, 1f);
        return env * (Bell(sweep, t) + 0.5f * Bell(sweep * 1.5f, t)) * 0.45f;
    });

    public static AudioClip Warning() => Build("Sentinel warning", 0.3f, t =>
    {
        var env = Mathf.Exp(-4f * t) * Mathf.Min(t / 0.005f, 1f);
        var wobble = 1f + 0.08f * Mathf.Sin(2f * Mathf.PI * 22f * t);
        return env * Square(140f * wobble, t) * 0.28f;
    });

    public static AudioClip Beep(float frequency) => Build("Countdown beep", 0.22f, t =>
    {
        var env = Mathf.Min(t / 0.008f, 1f) * Mathf.Exp(-9f * t);
        return env * (Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * frequency * t)) * 0.4f;
    });

    public static AudioClip Go() => Beep(1320f);

    public static AudioClip Win() => Arpeggio("Win fanfare", new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.11f, 0.9f);

    public static AudioClip Lose() => Arpeggio("Time up", new[] { 392f, 329.63f, 261.63f, 196f }, 0.2f, 0.9f);

    public static AudioClip Ambient() => Build("Ambient hum", 4f, t =>
    {
        // Frequencies are whole cycles per 4 seconds so the loop is seamless.
        var drift = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
        var pad = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.5f
                  + Mathf.Sin(2f * Mathf.PI * 82.5f * t) * 0.3f * drift
                  + Mathf.Sin(2f * Mathf.PI * 110.25f * t) * 0.2f * (1f - drift);
        return pad * 0.22f;
    }, false);

    private static AudioClip Arpeggio(string name, float[] notes, float step, float length)
    {
        return Build(name, length, t =>
        {
            var sample = 0f;
            for (var i = 0; i < notes.Length; i++)
            {
                var local = t - i * step;
                if (local < 0f) continue;
                sample += Mathf.Exp(-3.5f * local) * Mathf.Min(local / 0.004f, 1f) * Bell(notes[i], local);
            }

            return sample * 0.3f;
        });
    }

    private static float Bell(float frequency, float t)
    {
        var w = 2f * Mathf.PI * frequency * t;
        return Mathf.Sin(w) + 0.35f * Mathf.Sin(2f * w) * Mathf.Exp(-5f * t) + 0.15f * Mathf.Sin(3.01f * w);
    }

    private static float Square(float frequency, float t)
    {
        // Band-limited-ish square: a few odd harmonics, softer than a raw square wave.
        var w = 2f * Mathf.PI * frequency * t;
        return Mathf.Sin(w) + Mathf.Sin(3f * w) / 3f + Mathf.Sin(5f * w) / 5f;
    }

    private static AudioClip Build(string name, float seconds, System.Func<float, float> generator, bool fadeEnds = true)
    {
        var count = Mathf.CeilToInt(seconds * SampleRate);
        var data = new float[count];
        for (var i = 0; i < count; i++)
        {
            var t = (float)i / SampleRate;
            var sample = generator(t);
            if (fadeEnds)
            {
                var fade = Mathf.Min(1f, (count - i) / (0.01f * SampleRate));
                sample *= fade;
            }

            data[i] = Mathf.Clamp(sample, -0.95f, 0.95f);
        }

        var clip = AudioClip.Create(name, count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
