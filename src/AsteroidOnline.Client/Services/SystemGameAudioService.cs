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
    private const int MaxSimultaneousOneShots = 8;

    private readonly CachedSound? _shotSound;
    private readonly CachedSound? _explosionSound;
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

        _shotSound = TryLoadCachedSound(_shotPath);
        _explosionSound = TryLoadCachedSound(_explosionPath);
    }

    public void PlayShot()
    {
        if (!CanPlay(ref _lastShotAtMs, 35) || _shotSound is null)
            return;

        PlayOneShot(_shotSound, 0.90f);
    }

    public void PlayAsteroidExplosion()
    {
        if (!CanPlay(ref _lastExplosionAtMs, 90) || _explosionSound is null)
            return;

        PlayOneShot(_explosionSound, 0.95f);
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

    private void PlayOneShot(CachedSound sound, float volume)
    {
        WaveOutEvent? output = null;
        try
        {
            output = new WaveOutEvent
            {
                DesiredLatency = 60,
                NumberOfBuffers = 2,
            };
            output.Init(new CachedSoundSampleProvider(sound, volume));

            var capturedOutput = output;
            output.PlaybackStopped += (_, _) =>
            {
                lock (_sync)
                {
                    _activeOneShots.Remove(capturedOutput);
                }

                capturedOutput.Dispose();
            };

            lock (_sync)
            {
                if (_activeOneShots.Count >= MaxSimultaneousOneShots)
                {
                    output.Dispose();
                    return;
                }

                _activeOneShots.Add(output);
            }

            output.Play();
        }
        catch
        {
            output?.Dispose();
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

    private static CachedSound? TryLoadCachedSound(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return new CachedSound(path);
        }
        catch
        {
            return null;
        }
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

    private sealed class CachedSound
    {
        public CachedSound(string audioFileName)
        {
            using var audioFileReader = new AudioFileReader(audioFileName);
            WaveFormat = audioFileReader.WaveFormat;

            var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
            var readBuffer = new float[audioFileReader.WaveFormat.SampleRate * audioFileReader.WaveFormat.Channels];
            int samplesRead;
            while ((samplesRead = audioFileReader.Read(readBuffer, 0, readBuffer.Length)) > 0)
            {
                for (var i = 0; i < samplesRead; i++)
                    wholeFile.Add(readBuffer[i]);
            }

            AudioData = wholeFile.ToArray();
        }

        public float[] AudioData { get; }
        public WaveFormat WaveFormat { get; }
    }

    private sealed class CachedSoundSampleProvider : ISampleProvider
    {
        private readonly CachedSound _cachedSound;
        private readonly float _volume;
        private long _position;

        public CachedSoundSampleProvider(CachedSound cachedSound, float volume)
        {
            _cachedSound = cachedSound;
            _volume = Math.Clamp(volume, 0f, 1f);
        }

        public WaveFormat WaveFormat => _cachedSound.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var availableSamples = _cachedSound.AudioData.Length - _position;
            var samplesToCopy = (int)Math.Min(availableSamples, count);

            for (var i = 0; i < samplesToCopy; i++)
                buffer[offset + i] = _cachedSound.AudioData[(int)_position + i] * _volume;

            _position += samplesToCopy;
            return samplesToCopy;
        }
    }
}
