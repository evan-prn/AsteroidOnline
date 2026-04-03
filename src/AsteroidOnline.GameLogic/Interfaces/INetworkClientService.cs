namespace AsteroidOnline.GameLogic.Interfaces;

using AsteroidOnline.Shared.Packets;

/// <summary>
/// Service de communication réseau côté client.
/// Abstrait la couche transport (LiteNetLib) derrière une interface applicative
/// afin de ne pas coupler les ViewModels à une bibliothèque réseau spécifique.
/// </summary>
public interface INetworkClientService
{
    /// <summary>Indique si le client est actuellement connecté au serveur.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Événement déclenché à la réception de tout paquet venant du serveur.
    /// Le <see cref="BinaryReader"/> est positionné immédiatement après l'octet de type ;
    /// l'abonné doit désérialiser le corps du paquet avant de retourner.
    /// </summary>
    event Action<PacketType, BinaryReader>? PacketReceived;

    /// <summary>Événement déclenché lors d'une déconnexion (volontaire ou non).</summary>
    event Action? Disconnected;

    /// <summary>
    /// Tente une connexion asynchrone au serveur spécifié.
    /// </summary>
    /// <param name="address">Adresse IP ou nom d'hôte du serveur.</param>
    /// <param name="port">Port d'écoute du serveur (défaut : 7777).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns><see langword="true"/> si la connexion est établie, <see langword="false"/> sinon.</returns>
    Task<bool> ConnectAsync(string address, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envoie un paquet en mode fiable et ordonné (équivalent TCP).
    /// À utiliser pour les événements de jeu importants (connexion, élimination…).
    /// </summary>
    /// <param name="packet">Le paquet à envoyer.</param>
    void SendReliable(IPacket packet);

    /// <summary>
    /// Envoie un paquet sans garantie de livraison ni d'ordre (équivalent UDP).
    /// À utiliser pour les inputs temps-réel où la latence prime sur la fiabilité.
    /// </summary>
    /// <param name="packet">Le paquet à envoyer.</param>
    void SendUnreliable(IPacket packet);

    /// <summary>
    /// Traite les événements réseau en attente dans la file interne de LiteNetLib.
    /// Doit être appelé régulièrement (chaque frame ou sur un timer dédié).
    /// </summary>
    void PollEvents();

    /// <summary>Déconnecte proprement le client du serveur.</summary>
    void Disconnect();
}
