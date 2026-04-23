namespace AsteroidOnline.Client.Services;

using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

/// <summary>
/// Service audio Windows base sur NAudio.
/// Permet de garder l'ambiance en boucle et de superposer des one-shots (tir/explosion).
/// </summary>
public sealed class SystemGameAudioService : IGameAudioService, IDisposable
{
    private readonly string? _shotPath;
    private readonly string? _explosionPath;
    private readonly string? _ambientPath;
    private readonly object _sync = new();
    private readonly List<IWavePlayer> _activeOneShots = new();

    private IWavePlayer? _ambientOutput;
    private AudioFileReader? _ambientReader;
    private LoopStream? _ambientLoop;
    private long _lastShotAtMs;
    private long _lastExplosionAtMs;
    private bool _ambientStarted;

    public SystemGameAudioService()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var outputRoot = AppContext.BaseDirectory;
        _shotPath = ResolveAssetPath(outputRoot, "shot2.wav", "shot.wav", "shot2.mp3", "shot.mp3");
        _explosionPath = ResolveAssetPath(
            outputRoot,
            "asteroid-explosion.wav",
            "asteroid-explosion.mp3",
            "asteroid-shot.wav");
        _ambientPath = ResolveAssetPath(
            outputRoot,
            "ambient.wav",
            "ambient.mp3",
            "ambience.mp3",
            "music.mp3",
            "ambience.wav",
            "music.wav");
    }

    public void PlayShot()
    {
        if (!CanPlay(ref _lastShotAtMs, 35) || string.IsNullOrWhiteSpace(_shotPath))
            return;

        PlayOneShot(_shotPath, 0.90f);
    }

    public void PlayAsteroidExplosion()
    {
        if (!CanPlay(ref _lastExplosionAtMs, 90) || string.IsNullOrWhiteSpace(_explosionPath))
            return;

        PlayOneShot(_explosionPath, 0.95f);
    }

    public void StartAmbientLoop()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(_ambientPath))
            return;

        lock (_sync)
        {
            if (_ambientStarted)
                return;

            try
            {
                _ambientReader = new AudioFileReader(_ambientPath) { Volume = 0.42f };
                _ambientLoop = new LoopStream(_ambientReader);
                _ambientOutput = new WaveOutEvent
                {
                    DesiredLatency = 80,
                    NumberOfBuffers = 2,
                };
                _ambientOutput.Init(_ambientLoop);
                _ambientOutput.Play();
                _ambientStarted = true;
            }
            catch
            {
                DisposeAmbient_NoLock();
                _ambientStarted = false;
            }
        }
    }

    public void StopAmbientLoop()
    {
        lock (_sync)
        {
            _ambientStarted = false;
            DisposeAmbient_NoLock();
        }
    }

    private void PlayOneShot(string path, float volume)
    {
        if (!File.Exists(path))
            return;

        AudioFileReader? reader = null;
        WaveOutEvent? output = null;
        try
        {
            reader = new AudioFileReader(path) { Volume = volume };
            output = new WaveOutEvent
            {
                DesiredLatency = 60,
                NumberOfBuffers = 2,
            };
            output.Init(reader);

            var capturedOutput = output;
            var capturedReader = reader;
            output.PlaybackStopped += (_, _) =>
            {
                lock (_sync)
                {
                    _activeOneShots.Remove(capturedOutput);
                }

                capturedOutput.Dispose();
                capturedReader.Dispose();
            };

            lock (_sync)
            {
                _activeOneShots.Add(output);
            }

            output.Play();
        }
        catch
        {
            output?.Dispose();
            reader?.Dispose();
        }
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

    private static string? ResolveAssetPath(string outputRoot, params string[] fileNames)
    {
        foreach (var fileName in fileNames)
        {
            var candidates = new[]
            {
                Path.Combine(outputRoot, "Assets", "Audio", fileName),
                Path.Combine(outputRoot, fileName),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private void DisposeAmbient_NoLock()
    {
        _ambientOutput?.Stop();
        _ambientOutput?.Dispose();
        _ambientOutput = null;

        _ambientLoop?.Dispose();
        _ambientLoop = null;

        _ambientReader?.Dispose();
        _ambientReader = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _ambientStarted = false;
            DisposeAmbient_NoLock();

            foreach (var output in _activeOneShots)
            {
                output.Stop();
                output.Dispose();
            }
            _activeOneShots.Clear();
        }
    }

    /// <summary>
    /// WaveStream qui boucle sur le flux source.
    /// </summary>
    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream _source;

        public LoopStream(WaveStream source)
        {
            _source = source;
        }

        public override WaveFormat WaveFormat => _source.WaveFormat;

        public override long Length => long.MaxValue;

        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = _source.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                {
                    _source.Position = 0;
                    continue;
                }

                totalRead += read;
            }

            return totalRead;
        }
    }
}
