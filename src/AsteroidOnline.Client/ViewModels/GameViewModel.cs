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

    private readonly DispatcherTimer _gameTimer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastTickTimestamp;

    private InputHandler? _inputHandler;
    private GameRenderer? _renderer;

    private GameStateSnapshotPacket? _previousSnapshot;
    private GameStateSnapshotPacket? _currentSnapshot;
    private double _currentSnapshotReceivedAtSec;

    private const double SnapshotIntervalSec = 0.05; // 20 Hz

    // Prediction locale du dash
    private float _dashCooldownRemaining;
    private bool _dashWasPressedLastFrame;
    private bool _fireWasPressedLastFrame;

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

        _gameTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnGameTick);

        _lastTickTimestamp = _stopwatch.ElapsedMilliseconds;
        _gameTimer.Start();
    }

    /// <summary>
    /// Branche l'InputHandler sur la source clavier et le renderer sur le canvas.
    /// </summary>
    public void Attach(Avalonia.Input.IInputElement inputSource, Canvas gameCanvas)
    {
        _inputHandler?.Dispose();
        _inputHandler = new InputHandler(inputSource);
        _renderer = new GameRenderer(gameCanvas, _gameAudioService);
        _gameAudioService.StartAmbientLoop();
    }

    /// <summary>
    /// Nettoie les touches maintenues (ex: perte de focus).
    /// </summary>
    public void ClearInputs()
    {
        _inputHandler?.ClearAll();
    }

    private void OnGameTick(object? sender, EventArgs e)
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

        _networkService.PollEvents();

        if (_inputHandler is null) return;

        var inputState = _inputHandler.GetCurrentState();

        _networkService.SendUnreliable(new PlayerInputPacket
        {
            ThrustForward = inputState.ThrustForward,
            RotateLeft = inputState.RotateLeft,
            RotateRight = inputState.RotateRight,
            Fire = inputState.Fire,
            Dash = inputState.Dash,
            Timestamp = now,
        });

        UpdateDashPrediction(inputState, deltaTime);
        UpdateShotAudio(inputState);

        if (_renderer is not null)
        {
            var renderSnapshot = BuildRenderSnapshot();
            if (renderSnapshot is not null)
                _renderer.Render(renderSnapshot, LocalPlayerId, _playerSession.GetRosterSnapshot());
        }
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
                packet.IsSoloMode));
        });
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

    private static GameStateSnapshotPacket InterpolateSnapshots(
        GameStateSnapshotPacket previous,
        GameStateSnapshotPacket current,
        float alpha)
    {
        var result = new GameStateSnapshotPacket
        {
            ServerTimestamp = current.ServerTimestamp,
            AlivePlayersCount = current.AlivePlayersCount,
        };

        var previousPlayersById = previous.Players.ToDictionary(p => p.Id);
        foreach (var p in current.Players)
        {
            previousPlayersById.TryGetValue(p.Id, out var p0);
            result.Players.Add(new PlayerSnapshot
            {
                Id = p.Id,
                X = LerpWrapped(p0?.X ?? p.X, p.X, alpha, AsteroidOnline.Domain.World.WorldBounds.Default.Width),
                Y = LerpWrapped(p0?.Y ?? p.Y, p.Y, alpha, AsteroidOnline.Domain.World.WorldBounds.Default.Height),
                Rotation = LerpAngle(p0?.Rotation ?? p.Rotation, p.Rotation, alpha),
                VelocityX = Lerp(p0?.VelocityX ?? p.VelocityX, p.VelocityX, alpha),
                VelocityY = Lerp(p0?.VelocityY ?? p.VelocityY, p.VelocityY, alpha),
                Color = p.Color,
                IsAlive = p.IsAlive,
                DashCooldownProgress = Lerp(p0?.DashCooldownProgress ?? p.DashCooldownProgress, p.DashCooldownProgress, alpha),
                Score = p.Score,
                LivesRemaining = p.LivesRemaining,
                IsInvulnerable = p.IsInvulnerable,
                InvulnerabilityRemaining = p.InvulnerabilityRemaining,
            });
        }

        var previousAsteroidsById = previous.Asteroids.ToDictionary(a => a.Id);
        foreach (var a in current.Asteroids)
        {
            previousAsteroidsById.TryGetValue(a.Id, out var a0);
            result.Asteroids.Add(new AsteroidSnapshot
            {
                Id = a.Id,
                X = LerpWrapped(a0?.X ?? a.X, a.X, alpha, AsteroidOnline.Domain.World.WorldBounds.Default.Width),
                Y = LerpWrapped(a0?.Y ?? a.Y, a.Y, alpha, AsteroidOnline.Domain.World.WorldBounds.Default.Height),
                Rotation = LerpAngle(a0?.Rotation ?? a.Rotation, a.Rotation, alpha),
                Size = a.Size,
                HitPoints = a.HitPoints,
            });
        }

        var previousProjectilesById = previous.Projectiles.ToDictionary(pr => pr.Id);
        foreach (var pr in current.Projectiles)
        {
            previousProjectilesById.TryGetValue(pr.Id, out var pr0);
            result.Projectiles.Add(new ProjectileSnapshot
            {
                Id = pr.Id,
                X = LerpWrapped(pr0?.X ?? pr.X, pr.X, alpha, AsteroidOnline.Domain.World.WorldBounds.Default.Width),
                Y = LerpWrapped(pr0?.Y ?? pr.Y, pr.Y, alpha, AsteroidOnline.Domain.World.WorldBounds.Default.Height),
                OwnerId = pr.OwnerId,
            });
        }

        return result;
    }

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
        _gameTimer.Stop();
        _inputHandler?.Dispose();
        _gameAudioService.StopAmbientLoop();
        _networkService.PacketReceived -= OnPacketReceived;
    }
}
