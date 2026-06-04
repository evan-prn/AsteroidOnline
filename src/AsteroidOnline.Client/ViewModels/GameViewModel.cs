using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AsteroidOnline.Client.Input;
using AsteroidOnline.Client.Rendering;
using AsteroidOnline.Client.Services;
using AsteroidOnline.Domain.World;
using AsteroidOnline.Domain.Systems;
using AsteroidOnline.GameLogic.Interfaces;
using AsteroidOnline.Shared.Packets;

namespace AsteroidOnline.Client.ViewModels;

/// <summary>
/// ViewModel de l'écran de jeu principal.
/// Gère la boucle cliente à 60 Hz, l'envoi des inputs UDP,
/// la réception des snapshots serveur, le rendu canvas et le HUD.
/// </summary>
public partial class GameViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkClientService _networkService;
    private readonly INavigationService _navigationService;
    private readonly PlayerSession _playerSession;
    private readonly IGameAudioService _gameAudioService;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastTickTimestamp;
    private bool _isDisposed;
    private bool _animationFrameScheduled;
    private TopLevel? _animationTopLevel;

    private InputHandler? _inputHandler;
    private GameRenderer? _renderer;

    private GameStateSnapshotPacket? _previousSnapshot;
    private GameStateSnapshotPacket? _currentSnapshot;
    private double _currentSnapshotReceivedAtSec;
    private readonly GameStateSnapshotPacket _renderSnapshot = new();
    private readonly Dictionary<int, PlayerSnapshot> _previousPlayersById = new();
    private readonly Dictionary<int, AsteroidSnapshot> _previousAsteroidsById = new();
    private readonly Dictionary<int, ProjectileSnapshot> _previousProjectilesById = new();
    private readonly Dictionary<int, PlayerSnapshot> _renderPlayersById = new();
    private readonly Dictionary<int, AsteroidSnapshot> _renderAsteroidsById = new();
    private readonly Dictionary<int, ProjectileSnapshot> _renderProjectilesById = new();

    private const double SnapshotIntervalSec = 0.05; // 20 Hz

    // Prediction locale du dash
    private float _dashCooldownRemaining;
    private bool _dashWasPressedLastFrame;
    private bool _fireWasPressedLastFrame;
    private bool _spectateNextWasPressedLastFrame;
    private bool _spectatePreviousWasPressedLastFrame;
    private int _spectatedPlayerId;

    /// <summary>Identifiant du joueur local en session.</summary>
    public int LocalPlayerId => _playerSession.PlayerId;

    [ObservableProperty] private int _alivePlayersCount;
    [ObservableProperty] private int _myScore;
    [ObservableProperty] private int _myRank = 1;
    [ObservableProperty] private int _myLives = 3;
    [ObservableProperty] private double _dashCooldownProgress = 1.0;
    [ObservableProperty] private bool _isDashReady = true;
    [ObservableProperty] private bool _isInvulnerable;
    [ObservableProperty] private double _invulnerabilitySecondsRemaining;
    [ObservableProperty] private int _laserCharges;
    [ObservableProperty] private double _laserSecondsRemaining;
    [ObservableProperty] private bool _isLaserActive;
    [ObservableProperty] private bool _hasLaserCharge;
    [ObservableProperty] private bool _isSpectating;
    [ObservableProperty] private string _spectatedPlayerName = string.Empty;

    [ObservableProperty] private string _eliminationFeedText = string.Empty;
    [ObservableProperty] private bool _showEliminationFeed;

    [ObservableProperty] private int _fps;
    [ObservableProperty] private int _networkLatencyMs;

    private int _frameCount;
    private long _fpsWindowStart;

    public GameViewModel(
        INetworkClientService networkService,
        INavigationService navigationService,
        PlayerSession playerSession,
        IGameAudioService gameAudioService)
    {
        _networkService = networkService;
        _navigationService = navigationService;
        _playerSession = playerSession;
        _gameAudioService = gameAudioService;

        _networkService.PacketReceived += OnPacketReceived;

        _lastTickTimestamp = _stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// Branche l'InputHandler sur la source clavier et le renderer sur le canvas.
    /// </summary>
    public void Attach(Avalonia.Input.IInputElement inputSource, GameCanvasControl gameCanvas)
    {
        _inputHandler?.Dispose();
        _inputHandler = new InputHandler(inputSource);
        _renderer = new GameRenderer(gameCanvas, _gameAudioService);
        _gameAudioService.StartAmbientLoop();

        _animationTopLevel = inputSource as TopLevel ?? TopLevel.GetTopLevel(gameCanvas);
        ScheduleNextAnimationFrame();
    }

    /// <summary>
    /// Nettoie les touches maintenues (ex: perte de focus).
    /// </summary>
    public void ClearInputs()
    {
        _inputHandler?.ClearAll();
    }

    private void ScheduleNextAnimationFrame()
    {
        if (_isDisposed || _animationFrameScheduled || _animationTopLevel is null)
            return;

        _animationFrameScheduled = true;
        _animationTopLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan frameTime)
    {
        _animationFrameScheduled = false;
        OnGameTick();
        ScheduleNextAnimationFrame();
    }

    private void OnGameTick()
    {
        var now = _stopwatch.ElapsedMilliseconds;
        var deltaTime = (now - _lastTickTimestamp) / 1000f;
        _lastTickTimestamp = now;

        _frameCount++;
        var fpsElapsed = now - _fpsWindowStart;
        if (fpsElapsed >= 1000)
        {
            Fps = (int)Math.Round(_frameCount * 1000.0 / fpsElapsed);
            _frameCount = 0;
            _fpsWindowStart = now;
        }

        NetworkLatencyMs = _networkService.LatencyMs;

        if (_inputHandler is null) return;

        var inputState = _inputHandler.GetCurrentState();
        var renderSnapshot = BuildRenderSnapshot();
        var isSpectating = UpdateSpectatorState(inputState, renderSnapshot);

        _networkService.SendUnreliable(new PlayerInputPacket
        {
            ThrustForward = !isSpectating && inputState.ThrustForward,
            RotateLeft = !isSpectating && inputState.RotateLeft,
            RotateRight = !isSpectating && inputState.RotateRight,
            Fire = !isSpectating && inputState.Fire,
            Dash = !isSpectating && inputState.Dash,
            Laser = !isSpectating && inputState.Laser,
            Timestamp = now,
        });

        if (!isSpectating)
        {
            UpdateDashPrediction(inputState, deltaTime);
            UpdateShotAudio(inputState);
        }

        if (_renderer is not null && renderSnapshot is not null)
        {
            var cameraPlayerId = isSpectating && _spectatedPlayerId > 0
                ? _spectatedPlayerId
                : LocalPlayerId;
            _renderer.Render(renderSnapshot, LocalPlayerId, cameraPlayerId, _playerSession.GetRosterSnapshot());
        }
    }

    private bool UpdateSpectatorState(PlayerInputState inputState, GameStateSnapshotPacket? snapshot)
    {
        if (snapshot is null)
        {
            IsSpectating = false;
            SpectatedPlayerName = string.Empty;
            return false;
        }

        var localPlayer = snapshot.Players.Find(p => p.Id == LocalPlayerId);
        var alivePlayers = snapshot.Players
            .Where(p => p.IsAlive)
            .OrderBy(p => p.Id)
            .ToList();
        var shouldSpectate = localPlayer is not null && !localPlayer.IsAlive && alivePlayers.Count > 0;

        if (!shouldSpectate)
        {
            _spectatedPlayerId = 0;
            _spectateNextWasPressedLastFrame = inputState.SpectateNext;
            _spectatePreviousWasPressedLastFrame = inputState.SpectatePrevious;
            IsSpectating = false;
            SpectatedPlayerName = string.Empty;
            return false;
        }

        if (!alivePlayers.Any(p => p.Id == _spectatedPlayerId))
            _spectatedPlayerId = alivePlayers[0].Id;

        var nextPressed = inputState.SpectateNext && !_spectateNextWasPressedLastFrame;
        var previousPressed = inputState.SpectatePrevious && !_spectatePreviousWasPressedLastFrame;
        if (nextPressed ^ previousPressed)
        {
            var currentIndex = alivePlayers.FindIndex(p => p.Id == _spectatedPlayerId);
            if (currentIndex < 0)
                currentIndex = 0;

            var offset = nextPressed ? 1 : -1;
            var nextIndex = (currentIndex + offset + alivePlayers.Count) % alivePlayers.Count;
            _spectatedPlayerId = alivePlayers[nextIndex].Id;
        }

        _spectateNextWasPressedLastFrame = inputState.SpectateNext;
        _spectatePreviousWasPressedLastFrame = inputState.SpectatePrevious;
        IsSpectating = true;
        SpectatedPlayerName = _playerSession.GetPlayerName(_spectatedPlayerId);
        return true;
    }

    private void UpdateDashPrediction(PlayerInputState inputState, float deltaTime)
    {
        var dashJustPressed = inputState.Dash && !_dashWasPressedLastFrame;
        if (dashJustPressed && _dashCooldownRemaining <= 0f)
            _dashCooldownRemaining = DashSystem.CooldownDuration;

        _dashWasPressedLastFrame = inputState.Dash;

        if (_dashCooldownRemaining > 0f)
            _dashCooldownRemaining = MathF.Max(0f, _dashCooldownRemaining - deltaTime);

        DashCooldownProgress = _dashCooldownRemaining <= 0f
            ? 1.0
            : 1.0 - (_dashCooldownRemaining / DashSystem.CooldownDuration);

        IsDashReady = _dashCooldownRemaining <= 0f;
    }

    private void UpdateShotAudio(PlayerInputState inputState)
    {
        var fireJustPressed = inputState.Fire && !_fireWasPressedLastFrame;
        if (fireJustPressed)
            _gameAudioService.PlayShot();

        _fireWasPressedLastFrame = inputState.Fire;
    }

    private void OnPacketReceived(PacketType type, BinaryReader reader)
    {
        switch (type)
        {
            case PacketType.GameStateSnapshot:
                HandleSnapshot(reader);
                break;

            case PacketType.PlayerEliminated:
                HandlePlayerEliminated(reader);
                break;

            case PacketType.GameOver:
                HandleGameOver(reader);
                break;
        }
    }

    private void HandleSnapshot(BinaryReader reader)
    {
        var packet = new GameStateSnapshotPacket();
        packet.Deserialize(reader);

        Dispatcher.UIThread.Post(() =>
        {
            _previousSnapshot = _currentSnapshot;
            _currentSnapshot = packet;
            _currentSnapshotReceivedAtSec = _stopwatch.Elapsed.TotalSeconds;
            RebuildPreviousSnapshotIndexes(_previousSnapshot);

            AlivePlayersCount = packet.AlivePlayersCount;

            var mySnap = packet.Players.Find(p => p.Id == LocalPlayerId);
            if (mySnap is not null)
            {
                _dashCooldownRemaining = (1f - mySnap.DashCooldownProgress)
                    * DashSystem.CooldownDuration;
                MyScore = mySnap.Score;
                MyLives = mySnap.LivesRemaining;
                IsInvulnerable = mySnap.IsInvulnerable;
                InvulnerabilitySecondsRemaining = mySnap.InvulnerabilityRemaining;
                LaserCharges = mySnap.LaserCharges;
                LaserSecondsRemaining = mySnap.LaserRemaining;
                IsLaserActive = mySnap.IsLaserActive;
                HasLaserCharge = mySnap.LaserCharges > 0;
            }

            MyRank = ComputeRank(packet, LocalPlayerId);
        });
    }

    private void HandlePlayerEliminated(BinaryReader reader)
    {
        var packet = new PlayerEliminatedPacket();
        packet.Deserialize(reader);

        Dispatcher.UIThread.Post(() =>
        {
            EliminationFeedText = $"{packet.KillerName} a éliminé {packet.VictimName}";
            ShowEliminationFeed = true;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, _) =>
            {
                ShowEliminationFeed = false;
                ((DispatcherTimer)s!).Stop();
            };
            timer.Start();
        });
    }

    private void HandleGameOver(BinaryReader reader)
    {
        var packet = new GameOverPacket();
        packet.Deserialize(reader);

        Dispatcher.UIThread.Post(() =>
        {
            var winnerName = packet.WinnerName;
            if (!packet.IsSoloMode && string.IsNullOrWhiteSpace(winnerName))
                winnerName = packet.WinnerId == LocalPlayerId && LocalPlayerId > 0
                    ? _playerSession.Pseudo
                    : "Aucun survivant";

            _networkService.PacketReceived -= OnPacketReceived;
            _navigationService.NavigateTo(new GameOverViewModel(
                _navigationService,
                _networkService,
                winnerName,
                MyScore,
                packet.IsSoloMode,
                BuildFinalRanking()));
        });
    }

    private IReadOnlyList<FinalRankingEntry> BuildFinalRanking()
    {
        if (_currentSnapshot is null)
            return [];

        return _currentSnapshot.Players
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.IsAlive)
            .ThenBy(p => p.Id)
            .Select((p, index) => new FinalRankingEntry(
                index + 1,
                _playerSession.GetPlayerName(p.Id),
                p.Score,
                p.IsAlive))
            .ToList();
    }

    private GameStateSnapshotPacket? BuildRenderSnapshot()
    {
        if (_currentSnapshot is null)
            return null;

        if (_previousSnapshot is null)
            return _currentSnapshot;

        var elapsedFromCurrent = _stopwatch.Elapsed.TotalSeconds - _currentSnapshotReceivedAtSec;
        var alpha = Math.Clamp(elapsedFromCurrent / SnapshotIntervalSec, 0.0, 1.0);

        return InterpolateSnapshots(_previousSnapshot, _currentSnapshot, (float)alpha);
    }

    private GameStateSnapshotPacket InterpolateSnapshots(
        GameStateSnapshotPacket previous,
        GameStateSnapshotPacket current,
        float alpha)
    {
        _renderSnapshot.ServerTimestamp = current.ServerTimestamp;
        _renderSnapshot.AlivePlayersCount = current.AlivePlayersCount;
        _renderSnapshot.Players.Clear();
        _renderSnapshot.Asteroids.Clear();
        _renderSnapshot.Projectiles.Clear();
        _renderSnapshot.PowerUps.Clear();

        foreach (var p in current.Players)
        {
            _previousPlayersById.TryGetValue(p.Id, out var p0);
            var target = GetOrCreate(_renderPlayersById, p.Id, static () => new PlayerSnapshot());
            target.Id = p.Id;
            target.X = LerpWrapped(p0?.X ?? p.X, p.X, alpha, WorldBounds.Default.Width);
            target.Y = LerpWrapped(p0?.Y ?? p.Y, p.Y, alpha, WorldBounds.Default.Height);
            target.Rotation = LerpAngle(p0?.Rotation ?? p.Rotation, p.Rotation, alpha);
            target.VelocityX = Lerp(p0?.VelocityX ?? p.VelocityX, p.VelocityX, alpha);
            target.VelocityY = Lerp(p0?.VelocityY ?? p.VelocityY, p.VelocityY, alpha);
            target.Color = p.Color;
            target.IsAlive = p.IsAlive;
            target.DashCooldownProgress = Lerp(p0?.DashCooldownProgress ?? p.DashCooldownProgress, p.DashCooldownProgress, alpha);
            target.Score = p.Score;
            target.LivesRemaining = p.LivesRemaining;
            target.IsInvulnerable = p.IsInvulnerable;
            target.InvulnerabilityRemaining = p.InvulnerabilityRemaining;
            target.LaserCharges = p.LaserCharges;
            target.LaserRemaining = p.LaserRemaining;
            target.IsLaserActive = p.IsLaserActive;
            _renderSnapshot.Players.Add(target);
        }

        foreach (var a in current.Asteroids)
        {
            _previousAsteroidsById.TryGetValue(a.Id, out var a0);
            var target = GetOrCreate(_renderAsteroidsById, a.Id, static () => new AsteroidSnapshot());
            target.Id = a.Id;
            target.X = LerpWrapped(a0?.X ?? a.X, a.X, alpha, WorldBounds.Default.Width);
            target.Y = LerpWrapped(a0?.Y ?? a.Y, a.Y, alpha, WorldBounds.Default.Height);
            target.Rotation = LerpAngle(a0?.Rotation ?? a.Rotation, a.Rotation, alpha);
            target.Size = a.Size;
            target.HitPoints = a.HitPoints;
            _renderSnapshot.Asteroids.Add(target);
        }

        foreach (var pr in current.Projectiles)
        {
            _previousProjectilesById.TryGetValue(pr.Id, out var pr0);
            var target = GetOrCreate(_renderProjectilesById, pr.Id, static () => new ProjectileSnapshot());
            target.Id = pr.Id;
            target.X = LerpWrapped(pr0?.X ?? pr.X, pr.X, alpha, WorldBounds.Default.Width);
            target.Y = LerpWrapped(pr0?.Y ?? pr.Y, pr.Y, alpha, WorldBounds.Default.Height);
            target.OwnerId = pr.OwnerId;
            _renderSnapshot.Projectiles.Add(target);
        }

        foreach (var powerUp in current.PowerUps)
        {
            _renderSnapshot.PowerUps.Add(new PowerUpSnapshot
            {
                Id = powerUp.Id,
                X = powerUp.X,
                Y = powerUp.Y,
                Type = powerUp.Type,
            });
        }

        PruneRenderCache(_renderPlayersById, current.Players);
        PruneRenderCache(_renderAsteroidsById, current.Asteroids);
        PruneRenderCache(_renderProjectilesById, current.Projectiles);

        return _renderSnapshot;
    }

    private void RebuildPreviousSnapshotIndexes(GameStateSnapshotPacket? snapshot)
    {
        _previousPlayersById.Clear();
        _previousAsteroidsById.Clear();
        _previousProjectilesById.Clear();

        if (snapshot is null)
            return;

        foreach (var player in snapshot.Players)
            _previousPlayersById[player.Id] = player;
        foreach (var asteroid in snapshot.Asteroids)
            _previousAsteroidsById[asteroid.Id] = asteroid;
        foreach (var projectile in snapshot.Projectiles)
            _previousProjectilesById[projectile.Id] = projectile;
    }

    private static T GetOrCreate<T>(Dictionary<int, T> cache, int id, Func<T> factory)
        where T : class
    {
        if (cache.TryGetValue(id, out var value))
            return value;

        value = factory();
        cache[id] = value;
        return value;
    }

    private static void PruneRenderCache<TSnapshot>(
        Dictionary<int, TSnapshot> cache,
        IReadOnlyList<TSnapshot> liveSnapshots)
        where TSnapshot : class
    {
        if (cache.Count <= liveSnapshots.Count + 8)
            return;

        var liveIds = new HashSet<int>(liveSnapshots.Count);
        foreach (var snapshot in liveSnapshots)
            liveIds.Add(GetSnapshotId(snapshot));

        foreach (var id in cache.Keys.ToArray())
        {
            if (!liveIds.Contains(id))
                cache.Remove(id);
        }
    }

    private static int GetSnapshotId(object snapshot) => snapshot switch
    {
        PlayerSnapshot player => player.Id,
        AsteroidSnapshot asteroid => asteroid.Id,
        ProjectileSnapshot projectile => projectile.Id,
        _ => 0,
    };

    private static int ComputeRank(GameStateSnapshotPacket packet, int localPlayerId)
    {
        if (localPlayerId <= 0)
            return 1;

        var ordered = packet.Players
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.IsAlive)
            .ThenBy(p => p.Id)
            .ToList();

        var idx = ordered.FindIndex(p => p.Id == localPlayerId);
        return idx < 0 ? 1 : idx + 1;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float LerpWrapped(float a, float b, float t, float worldSize)
    {
        var delta = b - a;
        if (MathF.Abs(delta) > worldSize / 2f)
            delta -= MathF.Sign(delta) * worldSize;

        var value = a + (delta * t);
        while (value < 0f) value += worldSize;
        while (value >= worldSize) value -= worldSize;
        return value;
    }

    private static float LerpAngle(float a, float b, float t)
    {
        var delta = b - a;
        while (delta > MathF.PI) delta -= MathF.PI * 2f;
        while (delta < -MathF.PI) delta += MathF.PI * 2f;
        return a + (delta * t);
    }

    public void Dispose()
    {
        _isDisposed = true;
        _inputHandler?.Dispose();
        _gameAudioService.StopAmbientLoop();
        _networkService.PacketReceived -= OnPacketReceived;
    }
}
