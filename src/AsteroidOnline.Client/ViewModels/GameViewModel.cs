using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AsteroidOnline.Client.Input;
using AsteroidOnline.Client.Rendering;
using AsteroidOnline.Domain.Systems;
using AsteroidOnline.GameLogic.Interfaces;
using AsteroidOnline.Shared.Packets;

namespace AsteroidOnline.Client.ViewModels;

/// <summary>
/// ViewModel de l'écran de jeu principal.
/// Gère la boucle cliente à 60 Hz, l'envoi des inputs UDP,
/// la réception des snapshots serveur, le rendu canvas et le HUD (US-29).
/// </summary>
public partial class GameViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkClientService _networkService;
    private readonly INavigationService    _navigationService;

    private readonly DispatcherTimer _gameTimer;
    private readonly Stopwatch       _stopwatch = Stopwatch.StartNew();
    private long _lastTickTimestamp;

    private InputHandler?  _inputHandler;
    private GameRenderer?  _renderer;

    // Dernier snapshot reçu du serveur (null tant que la partie n'a pas commencé)
    private GameStateSnapshotPacket? _lastSnapshot;

    // Identifiant du joueur local (reçu lors du LobbyJoined — passé depuis App)
    public int LocalPlayerId { get; set; }

    // Prédiction locale du dash (US-11)
    private float _dashCooldownRemaining;
    private bool  _dashWasPressedLastFrame;

    // ──── Propriétés HUD (US-29) ───────────────────────────────────────────────

    /// <summary>Nombre de joueurs encore en vie.</summary>
    [ObservableProperty] private int    _alivePlayersCount;

    /// <summary>Score du joueur local.</summary>
    [ObservableProperty] private int    _myScore;

    /// <summary>Rang actuel parmi les survivants.</summary>
    [ObservableProperty] private int    _myRank = 1;

    /// <summary>Progression recharge dash [0.0 – 1.0] (US-11).</summary>
    [ObservableProperty] private double _dashCooldownProgress = 1.0;

    /// <summary>Dash immédiatement disponible.</summary>
    [ObservableProperty] private bool   _isDashReady = true;

    // Feed d'éliminations HUD (US-24)
    [ObservableProperty] private string _eliminationFeedText = string.Empty;
    [ObservableProperty] private bool   _showEliminationFeed;

    // ──── Constructeur ────────────────────────────────────────────────────────

    public GameViewModel(INetworkClientService networkService, INavigationService navigationService)
    {
        _networkService    = networkService;
        _navigationService = navigationService;

        _networkService.PacketReceived += OnPacketReceived;

        _gameTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnGameTick);

        _lastTickTimestamp = _stopwatch.ElapsedMilliseconds;
        _gameTimer.Start();
    }

    // ──── Attachement par GameView ────────────────────────────────────────────

    /// <summary>
    /// Branche l'InputHandler sur la source clavier et le renderer sur le canvas.
    /// Appelé depuis <see cref="Views.GameView.OnAttachedToVisualTree"/>.
    /// </summary>
    public void Attach(Avalonia.Input.IInputElement inputSource, Canvas gameCanvas)
    {
        _inputHandler?.Dispose();
        _inputHandler = new InputHandler(inputSource);
        _renderer     = new GameRenderer(gameCanvas);
    }

    // ──── Boucle de jeu ───────────────────────────────────────────────────────

    private void OnGameTick(object? sender, EventArgs e)
    {
        var now       = _stopwatch.ElapsedMilliseconds;
        var deltaTime = (now - _lastTickTimestamp) / 1000f;
        _lastTickTimestamp = now;

        // 1. Poll réseau
        _networkService.PollEvents();

        if (_inputHandler is null) return;

        // 2. Lecture inputs
        var inputState = _inputHandler.GetCurrentState();

        // 3. Envoi UDP
        _networkService.SendUnreliable(new PlayerInputPacket
        {
            ThrustForward = inputState.ThrustForward,
            RotateLeft    = inputState.RotateLeft,
            RotateRight   = inputState.RotateRight,
            Fire          = inputState.Fire,
            Dash          = inputState.Dash,
            Timestamp     = now,
        });

        // 4. Prédiction dash HUD
        UpdateDashPrediction(inputState, deltaTime);

        // 5. Rendu canvas
        if (_renderer is not null && _lastSnapshot is not null)
            _renderer.Render(_lastSnapshot, LocalPlayerId);
    }

    // ──── Prédiction locale dash ──────────────────────────────────────────────

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

    // ──── Réception des paquets ───────────────────────────────────────────────

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

        // Mise à jour depuis le thread réseau → post sur UI thread pour les propriétés
        Dispatcher.UIThread.Post(() =>
        {
            _lastSnapshot      = packet;
            AlivePlayersCount  = packet.AlivePlayersCount;

            // Synchronisation du cooldown dash depuis le snapshot serveur
            var mySnap = packet.Players.Find(p => p.Id == LocalPlayerId);
            if (mySnap is not null)
            {
                _dashCooldownRemaining = (1f - mySnap.DashCooldownProgress)
                                        * DashSystem.CooldownDuration;
            }
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

            // Masquage automatique après 4 secondes (US-24)
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
            _networkService.PacketReceived -= OnPacketReceived;
            // Navigation vers l'écran de fin (US-25) — GameOverViewModel créé en BLOC 5
            // Pour l'instant on revient au lobby
            _navigationService.NavigateTo<LobbyViewModel>();
        });
    }

    // ──── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        _gameTimer.Stop();
        _inputHandler?.Dispose();
        _networkService.PacketReceived -= OnPacketReceived;
    }
}
