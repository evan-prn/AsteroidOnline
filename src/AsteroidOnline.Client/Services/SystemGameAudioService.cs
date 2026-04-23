namespace AsteroidOnline.Client.Services;

using System;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Audio gameplay non bloquant, sans beep systeme.
/// Les sons sont synthetises en WAV puis joues via WinMM en mode asynchrone.
/// </summary>
public sealed class SystemGameAudioService : IGameAudioService
{
    private const int SndAsync = 0x0001;
    private const int SndNodefault = 0x0002;
    private const int SndFilename = 0x00020000;

    private readonly string? _shotPath;
    private readonly string? _explosionPath;
    private long _lastShotAtMs;
    private long _lastExplosionAtMs;

    public SystemGameAudioService()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var audioDirectory = Path.Combine(Path.GetTempPath(), "AsteroidOnlineAudio");
        Directory.CreateDirectory(audioDirectory);
        _shotPath = Path.Combine(audioDirectory, "shot.wav");
        _explosionPath = Path.Combine(audioDirectory, "asteroid-explosion.wav");

        if (!File.Exists(_shotPath))
            File.WriteAllBytes(_shotPath, BuildShotWav());

        if (!File.Exists(_explosionPath))
            File.WriteAllBytes(_explosionPath, BuildExplosionWav());
    }

    public void PlayShot()
    {
        if (!CanPlay(ref _lastShotAtMs, 55) || string.IsNullOrWhiteSpace(_shotPath))
            return;

        PlaySound(_shotPath, IntPtr.Zero, SndAsync | SndNodefault | SndFilename);
    }

    public void PlayAsteroidExplosion()
    {
        if (!CanPlay(ref _lastExplosionAtMs, 120) || string.IsNullOrWhiteSpace(_explosionPath))
            return;

        PlaySound(_explosionPath, IntPtr.Zero, SndAsync | SndNodefault | SndFilename);
    }

    private static bool CanPlay(ref long lastPlayedAtMs, int minIntervalMs)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var now = Environment.TickCount64;
        if ((now - lastPlayedAtMs) < minIntervalMs)
            return false;

        lastPlayedAtMs = now;
        return true;
    }

    private static byte[] BuildShotWav()
    {
        const int sampleRate = 22050;
        const double duration = 0.09;
        var samples = new short[(int)(sampleRate * duration)];

        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)sampleRate;
            var envelope = Math.Exp(-28.0 * t);
            var signal =
                (Math.Sin(2 * Math.PI * 1360 * t) * 0.65) +
                (Math.Sin(2 * Math.PI * 1820 * t) * 0.35);
            samples[i] = (short)(signal * envelope * short.MaxValue * 0.42);
        }

        return BuildWave(samples, sampleRate);
    }

    private static byte[] BuildExplosionWav()
    {
        const int sampleRate = 22050;
        const double duration = 0.42;
        var samples = new short[(int)(sampleRate * duration)];
        var random = new Random(42);
        var lowpass = 0.0;

        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)sampleRate;
            var envelope = Math.Exp(-6.5 * t);
            var noise = (random.NextDouble() * 2.0) - 1.0;
            lowpass = (lowpass * 0.86) + (noise * 0.14);
            var rumble = Math.Sin(2 * Math.PI * (80 + (30 * t)) * t) * 0.25;
            var signal = ((lowpass * 0.82) + rumble) * envelope;
            samples[i] = (short)(signal * short.MaxValue * 0.55);
        }

        return BuildWave(samples, sampleRate);
    }

    private static byte[] BuildWave(short[] samples, int sampleRate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var dataLength = samples.Length * sizeof(short);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        foreach (var sample in samples)
            writer.Write(sample);

        writer.Flush();
        return stream.ToArray();
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, int fdwSound);
}
