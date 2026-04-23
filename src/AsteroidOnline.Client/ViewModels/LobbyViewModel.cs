using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsteroidOnline.Client.Services;
using AsteroidOnline.GameLogic.Interfaces;
using AsteroidOnline.Shared.Packets;

namespace AsteroidOnline.Client.ViewModels;

/// <summary>
/// ViewModel de l'écran de lobby pré-partie (US-04, US-06).
/// Affiche la liste des joueurs connectés et le compte à rebours avant le lancement.
/// Reçoit les paquets <see cref="LobbyStatePacket"/> et <see cref="CountdownPacket"/>
/// via le service réseau et met à jour les bindings AvaloniaUI en conséquence.
/// </summary>
public partial class LobbyViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkClientService _networkService;
    private readonly INavigationService    _navigationService;
    private readonly PlayerSession         _playerSession;
    private readonly DispatcherTimer       _lobbySyncRetryTimer;
    private bool _hasReceivedAuthoritativeLobbyState;
    private int  _lobbySyncRetryCount;
    private const int MaxLobbySyncRetries = 6;

    // ──── Propriétés liées ────────────────────────────────────────────────────

    /// <summary>
    /// Liste observable des joueurs présents dans le lobby (US-04).
    /// Mise à jour à chaque réception d'un <see cref="LobbyStatePacket"/>.
    /// </summary>
    public ObservableCollection<LobbyPlayerInfo> Players { get; } = new();

    /// <summary>Nombre total de joueurs dans le lobby.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSoloCommand))]
    [NotifyPropertyChangedFor(nameof(CanStartSolo))]
    private int _playerCount;

    /// <summary>
    /// Secondes restantes avant le début de la partie.
    /// Vaut -1 tant que le compte à rebours n'a pas démarré.
    /// </summary>
    [ObservableProperty]
    private int _countdownSeconds = -1;

    /// <summary>
    /// Texte affiché pour le compte à rebours (ex : "3...", "GO !").
    /// Animé côté vue via des transitions sur ce binding (US-06).
    /// </summary>
    [ObservableProperty]
    private string _countdownText = "En attente des joueurs...";

    /// <summary>Indique si le compte à rebours est en cours.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSoloCommand))]
    [NotifyPropertyChangedFor(nameof(CanStartSolo))]
    private bool _isCountingDown;

    /// <summary>Identifiant du joueur hôte courant.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSoloCommand))]
    [NotifyPropertyChangedFor(nameof(CanStartSolo))]
    private int _hostPlayerId = -1;

    /// <summary>Nom de l'hôte courant.</summary>
    [ObservableProperty]
    private string _hostName = "Aucun";

    /// <summary>
    /// Vrai quand le bouton de lancement solo doit être proposé.
    /// </summary>
    public bool CanStartSolo => PlayerCount == 1 && !IsCountingDown && HostPlayerId == _playerSession.PlayerId;

    // ──── Constructeur ────────────────────────────────────────────────────────

    /// <summary>
    /// Initialise le LobbyViewModel et s'abonne aux paquets réseau.
    /// </summary>
    /// <param name="networkService">Service de communication réseau.</param>
    /// <param name="navigationService">Service de navigation entre vues.</param>
    public LobbyViewModel(
        INetworkClientService networkService,
        INavigationService    navigationService,
        PlayerSession         playerSession)
    {
        _networkService    = networkService;
        _navigationService = navigationService;
        _playerSession     = playerSession;

        _networkService.PacketReceived += OnPacketReceived;

        // Évite l'affichage "0 joueur / Hôte: Aucun" à l'ouverture du lobby.
        BootstrapLocalLobbyState();
        // Demande explicite de resynchronisation pour couvrir les cas où
        // un broadcast lobby serveur a été manqué pendant la transition d'écran.
        _lobbySyncRetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _lobbySyncRetryTimer.Tick += OnLobbySyncRetryTick;

        RequestLobbyStateSync();
        _lobbySyncRetryTimer.Start();
    }

    // ──── Réception des paquets ───────────────────────────────────────────────

    /// <summary>
    /// Dispatche les paquets entrants vers les gestionnaires appropriés.
    /// </summary>
    private void OnPacketReceived(PacketType type, BinaryReader reader)
    {
        switch (type)
        {
            case PacketType.LobbyState:
                HandleLobbyState(reader);
                break;

            case PacketType.Countdown:
                HandleCountdown(reader);
                break;
        }
    }

    /// <summary>
    /// Met à jour la liste des joueurs depuis un <see cref="LobbyStatePacket"/> (US-04).
    /// </summary>
    private void HandleLobbyState(BinaryReader reader)
    {
        var packet = new LobbyStatePacket();
        packet.Deserialize(reader);

        // Mise à jour de la collection depuis le thread UI pour éviter les exceptions de binding.
        Dispatcher.UIThread.Post(() =>
        {
            // Retour explicite à l'état "lobby en attente" quand on reçoit un état lobby.
            // Sans ce reset, IsCountingDown peut rester bloqué à true après une manche,
            // ce qui masque le bouton "LANCER EN SOLO".
            IsCountingDown = false;
            CountdownSeconds = -1;
            CountdownText = "En attente des joueurs...";
            HostPlayerId = packet.HostPlayerId;

            Players.Clear();
            foreach (var player in packet.Players)
                Players.Add(player);

            PlayerCount = Players.Count;
            HostName = Players.FirstOrDefault(p => p.Id == HostPlayerId)?.Pseudo ?? "Aucun";
            OnPropertyChanged(nameof(CanStartSolo));
            _hasReceivedAuthoritativeLobbyState = true;
            _lobbySyncRetryTimer.Stop();
        });
    }

    /// <summary>
    /// Met à jour le compte à rebours affiché dans le lobby (US-06).
    /// Navigue vers le GameViewModel lorsque le compte atteint 0.
    /// </summary>
    private void HandleCountdown(BinaryReader reader)
    {
        var packet = new CountdownPacket();
        packet.Deserialize(reader);

        Dispatcher.UIThread.Post(() =>
        {
            CountdownSeconds = packet.SecondsRemaining;
            IsCountingDown   = true;
            OnPropertyChanged(nameof(CanStartSolo));

            CountdownText = packet.SecondsRemaining switch
            {
                > 0 => $"{packet.SecondsRemaining}...",
                0   => "GO !",
                _   => "En attente des joueurs...",
            };

            if (packet.SecondsRemaining == 0)
            {
                _networkService.PacketReceived -= OnPacketReceived;
                // Navigation vers l'écran de jeu (US-31)
                _navigationService.NavigateTo<GameViewModel>();
            }
        });
    }

    partial void OnIsCountingDownChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanStartSolo));
    }

    [RelayCommand(CanExecute = nameof(CanStartSolo))]
    private void StartSolo()
    {
        _networkService.SendReliable(new StartSoloRequestPacket());
    }

    private void OnLobbySyncRetryTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        if (_hasReceivedAuthoritativeLobbyState)
        {
            _lobbySyncRetryTimer.Stop();
            return;
        }

        if (_lobbySyncRetryCount >= MaxLobbySyncRetries)
        {
            _lobbySyncRetryTimer.Stop();
            return;
        }

        _lobbySyncRetryCount++;
        RequestLobbyStateSync();
    }

    private void RequestLobbyStateSync()
    {
        _networkService.SendReliable(new LobbyStateRequestPacket());
    }

    private void BootstrapLocalLobbyState()
    {
        if (_playerSession.PlayerId <= 0)
            return;

        IsCountingDown = false;
        CountdownSeconds = -1;
        CountdownText = "En attente des joueurs...";
        HostPlayerId = _playerSession.PlayerId;
        HostName = string.IsNullOrWhiteSpace(_playerSession.Pseudo)
            ? $"Joueur{_playerSession.PlayerId}"
            : _playerSession.Pseudo;

        Players.Clear();
        Players.Add(new LobbyPlayerInfo
        {
            Id = _playerSession.PlayerId,
            Pseudo = HostName,
            Color = _playerSession.Color,
            IsHost = true,
        });

        PlayerCount = 1;
        OnPropertyChanged(nameof(CanStartSolo));
    }

    public void Dispose()
    {
        _lobbySyncRetryTimer.Stop();
        _lobbySyncRetryTimer.Tick -= OnLobbySyncRetryTick;
        _networkService.PacketReceived -= OnPacketReceived;
    }
}
