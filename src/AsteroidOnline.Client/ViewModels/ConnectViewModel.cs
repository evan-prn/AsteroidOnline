using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.GameLogic.Interfaces;
using AsteroidOnline.Shared.Packets;

namespace AsteroidOnline.Client.ViewModels;

/// <summary>
/// États possibles de la tentative de connexion au serveur (US-03).
/// </summary>
public enum ConnectionStatus
{
    /// <summary>Aucune tentative en cours — état initial.</summary>
    Idle,

    /// <summary>Connexion TCP en cours d'établissement.</summary>
    Connecting,

    /// <summary>Connexion établie avec succès, attente du paquet LobbyJoined.</summary>
    Connected,

    /// <summary>Échec de connexion (serveur inaccessible ou refus).</summary>
    Failed,
}

/// <summary>
/// Option de couleur affichée dans le sélecteur de vaisseau.
/// Regroupe l'enum métier, le nom localisé et la couleur hexadécimale.
/// </summary>
/// <param name="Color">Valeur de l'enum <see cref="PlayerColor"/>.</param>
/// <param name="Name">Nom affiché dans l'interface (ex : "Bleu").</param>
/// <param name="HexColor">Code hexadécimal de la couleur (ex : "#4488FF").</param>
public record PlayerColorOption(PlayerColor Color, string Name, string HexColor);

/// <summary>
/// ViewModel de l'écran de connexion (US-01, US-02, US-03, US-05).
/// Gère la saisie du pseudo, de l'adresse serveur et l'initiation de la connexion TCP.
/// Une fois la connexion confirmée par <see cref="LobbyJoinedPacket"/>,
/// navigue automatiquement vers le <see cref="LobbyViewModel"/>.
/// </summary>
public partial class ConnectViewModel : ViewModelBase
{
    private readonly INetworkClientService _networkService;
    private readonly INavigationService    _navigationService;

    // ──── Propriétés liées ────────────────────────────────────────────────────

    /// <summary>Pseudo saisi par le joueur. Doit être non vide pour activer la connexion.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _pseudo = string.Empty;

    /// <summary>Adresse IP ou nom d'hôte du serveur (défaut : localhost).</summary>
    [ObservableProperty]
    private string _serverAddress = "127.0.0.1";

    /// <summary>Port TCP du serveur (défaut : 7777).</summary>
    [ObservableProperty]
    private int _serverPort = 7777;

    /// <summary>État courant de la tentative de connexion (US-03).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private ConnectionStatus _connectionStatus = ConnectionStatus.Idle;

    /// <summary>Message de statut affiché sous le bouton de connexion.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Option de couleur sélectionnée dans le sélecteur de vaisseau (US-05).</summary>
    [ObservableProperty]
    private PlayerColorOption _selectedColorOption;

    // ──── Données statiques ───────────────────────────────────────────────────

    /// <summary>
    /// Liste de toutes les couleurs disponibles, avec leur nom et code hexadécimal.
    /// Liée au sélecteur de couleur dans <c>ConnectView</c>.
    /// </summary>
    public IReadOnlyList<PlayerColorOption> AvailableColors { get; } =
    [
        new(PlayerColor.Rouge,  "Rouge",  "#FF4444"),
        new(PlayerColor.Bleu,   "Bleu",   "#4488FF"),
        new(PlayerColor.Vert,   "Vert",   "#44FF88"),
        new(PlayerColor.Jaune,  "Jaune",  "#FFDD44"),
        new(PlayerColor.Violet, "Violet", "#AA44FF"),
        new(PlayerColor.Orange, "Orange", "#FF8844"),
    ];

    // ──── Constructeur ────────────────────────────────────────────────────────

    /// <summary>
    /// Initialise le ConnectViewModel avec les services injectés.
    /// </summary>
    /// <param name="networkService">Service réseau client.</param>
    /// <param name="navigationService">Service de navigation entre vues.</param>
    public ConnectViewModel(
        INetworkClientService networkService,
        INavigationService    navigationService)
    {
        _networkService    = networkService;
        _navigationService = navigationService;

        // Sélection de la couleur Bleu par défaut.
        _selectedColorOption = AvailableColors[1];
    }

    // ──── Commandes ───────────────────────────────────────────────────────────

    /// <summary>Condition d'activation du bouton "Se connecter".</summary>
    private bool CanConnect =>
        !string.IsNullOrWhiteSpace(Pseudo) &&
        ConnectionStatus != ConnectionStatus.Connecting;

    /// <summary>
    /// Lance la connexion TCP au serveur, puis envoie le <see cref="ConnectRequestPacket"/>.
    /// Navigation automatique vers le lobby lors de la réception du <see cref="LobbyJoinedPacket"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        ConnectionStatus = ConnectionStatus.Connecting;
        StatusMessage    = "Connexion en cours...";

        // Abonnement aux paquets avant la connexion pour ne rien manquer.
        _networkService.PacketReceived += OnPacketReceived;

        try
        {
            var connected = await _networkService.ConnectAsync(ServerAddress, ServerPort);

            if (!connected)
            {
                ConnectionStatus = ConnectionStatus.Failed;
                StatusMessage    = "Impossible de joindre le serveur.";
                _networkService.PacketReceived -= OnPacketReceived;
                return;
            }

            // Envoi de la demande d'identification avec pseudo + couleur.
            _networkService.SendReliable(new ConnectRequestPacket
            {
                Pseudo = Pseudo.Trim(),
                Color  = SelectedColorOption.Color,
            });
        }
        catch (Exception ex)
        {
            ConnectionStatus = ConnectionStatus.Failed;
            StatusMessage    = $"Erreur : {ex.Message}";
            _networkService.PacketReceived -= OnPacketReceived;
        }
    }

    // ──── Réception des paquets ───────────────────────────────────────────────

    /// <summary>
    /// Traite les paquets reçus pendant la phase de connexion.
    /// Seul <see cref="PacketType.LobbyJoined"/> est attendu à ce stade.
    /// </summary>
    private void OnPacketReceived(PacketType type, BinaryReader reader)
    {
        if (type != PacketType.LobbyJoined)
            return;

        var packet = new LobbyJoinedPacket();
        packet.Deserialize(reader);

        // Mise à jour du statut depuis le thread UI.
        Dispatcher.UIThread.Post(() =>
        {
            ConnectionStatus = ConnectionStatus.Connected;
            StatusMessage    = $"Connecté — ID joueur : {packet.PlayerId}";

            // Navigation vers le lobby (US-02) : se désabonner puis naviguer.
            _networkService.PacketReceived -= OnPacketReceived;
            _navigationService.NavigateTo<LobbyViewModel>();
        });
    }
}
