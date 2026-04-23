namespace AsteroidOnline.Client.Services;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

/// <summary>
/// Audio Windows non bloquant.
/// Les SFX WAV passent par PlaySound, les MP3 passent par MCI hors thread UI.
/// La musique d'ambiance tourne sur un alias dedie en boucle.
/// </summary>
public sealed class SystemGameAudioService : IGameAudioService
{
    private const int SndAsync = 0x0001;
    private const int SndNodefault = 0x0002;
    private const int SndFilename = 0x00020000;
    private const string AmbientAlias = "ambient_loop";

    private readonly string? _shotPath;
    private readonly string? _explosionPath;
    private readonly string? _ambientPath;
    private long _lastShotAtMs;
    private long _lastExplosionAtMs;
    private int _shotAliasCounter;
    private int _explosionAliasCounter;
    private bool _ambientStarted;

    public SystemGameAudioService()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var outputRoot = AppContext.BaseDirectory;
        _shotPath = ResolveAssetPath(outputRoot, "shot2.mp3", "shot.mp3", "shot.wav");
        _explosionPath = ResolveAssetPath(
            outputRoot,
            "asteroid-explosion.mp3",
            "asteroid-explosion.wav");
        _ambientPath = ResolveAssetPath(
            outputRoot,
            "ambient.mp3",
            "ambience.mp3",
            "music.mp3",
            "ambient.wav");
    }

    public void PlayShot()
    {
        if (!CanPlay(ref _lastShotAtMs, 55) || string.IsNullOrWhiteSpace(_shotPath))
            return;

        PlayOneShot(_shotPath, "shot", ref _shotAliasCounter);
    }

    public void PlayAsteroidExplosion()
    {
        if (!CanPlay(ref _lastExplosionAtMs, 120) || string.IsNullOrWhiteSpace(_explosionPath))
            return;

        PlayOneShot(_explosionPath, "explosion", ref _explosionAliasCounter);
    }

    public void StartAmbientLoop()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(_ambientPath) || _ambientStarted)
            return;

        _ambientStarted = true;
        _ = Task.Run(() =>
        {
            _ = MciSendString($"close {AmbientAlias}", null, 0, IntPtr.Zero);
            _ = MciSendString($"open \"{_ambientPath}\" alias {AmbientAlias}", null, 0, IntPtr.Zero);
            _ = MciSendString($"play {AmbientAlias} repeat", null, 0, IntPtr.Zero);
        });
    }

    public void StopAmbientLoop()
    {
        if (!OperatingSystem.IsWindows() || !_ambientStarted)
            return;

        _ambientStarted = false;
        _ = Task.Run(() => _ = MciSendString($"close {AmbientAlias}", null, 0, IntPtr.Zero));
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

    private static void PlayOneShot(string path, string aliasPrefix, ref int aliasCounter)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            PlaySound(path, IntPtr.Zero, SndAsync | SndNodefault | SndFilename);
            return;
        }

        if (!extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            return;

        aliasCounter++;
        var alias = $"{aliasPrefix}{aliasCounter}";
        var safePath = path.Replace("\"", "\"\"");
        _ = Task.Run(async () =>
        {
            _ = MciSendString($"close {alias}", null, 0, IntPtr.Zero);
            _ = MciSendString($"open \"{safePath}\" type mpegvideo alias {alias}", null, 0, IntPtr.Zero);
            _ = MciSendString($"play {alias} from 0", null, 0, IntPtr.Zero);
            await Task.Delay(1500).ConfigureAwait(false);
            _ = MciSendString($"close {alias}", null, 0, IntPtr.Zero);
        });
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, int fdwSound);

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MciSendString(string command, System.Text.StringBuilder? buffer, int bufferSize, IntPtr hwndCallback);
}
