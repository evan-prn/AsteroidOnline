using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using AsteroidOnline.GameLogic.Interfaces;
using AsteroidOnline.Shared.Packets;

namespace AsteroidOnline.Client.ViewModels;

/// <summary>
/// ViewModel de l'écran de lobby pré-partie (US-04, US-06).
/// Affiche la liste des joueurs connectés et le compte à rebours avant le lancement.
/// Reçoit les paquets <see cref="LobbyStatePacket"/> et <see cref="CountdownPacket"/>
/// via le service réseau et met à jour les bindings AvaloniaUI en conséquence.
/// </summary>
public partial class LobbyViewModel : ViewModelBase
{
    private readonly INetworkClientService _networkService;
    private readonly INavigationService    _navigationService;

    // ──── Propriétés liées ────────────────────────────────────────────────────

    /// <summary>
    /// Liste observable des joueurs présents dans le lobby (US-04).
    /// Mise à jour à chaque réception d'un <see cref="LobbyStatePacket"/>.
    /// </summary>
    public ObservableCollection<LobbyPlayerInfo> Players { get; } = new();

    /// <summary>Nombre total de joueurs dans le lobby.</summary>
    [ObservableProperty]
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
    private bool _isCountingDown;

    // ──── Constructeur ────────────────────────────────────────────────────────

    /// <summary>
    /// Initialise le LobbyViewModel et s'abonne aux paquets réseau.
    /// </summary>
    /// <param name="networkService">Service de communication réseau.</param>
    /// <param name="navigationService">Service de navigation entre vues.</param>
    public LobbyViewModel(
        INetworkClientService networkService,
        INavigationService    navigationService)
    {
        _networkService    = networkService;
        _navigationService = navigationService;

        _networkService.PacketReceived += OnPacketReceived;
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
            Players.Clear();
            foreach (var player in packet.Players)
                Players.Add(player);

            PlayerCount = Players.Count;
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
}
