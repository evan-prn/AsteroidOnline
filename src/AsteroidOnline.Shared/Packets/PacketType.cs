namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Identifiants uniques de chaque type de paquet réseau.
/// Le premier octet de tout paquet reçu ou envoyé contient cette valeur,
/// ce qui permet au récepteur de désérialiser correctement le corps du paquet.
/// </summary>
public enum PacketType : byte
{
    // ── Client → Serveur ──────────────────────────────────────────────────────

    /// <summary>Demande de connexion avec pseudo et couleur.</summary>
    ConnectRequest = 0x01,

    /// <summary>État des entrées clavier du joueur (mouvement, tir).</summary>
    PlayerInput = 0x02,

    /// <summary>Demande de tir d'un projectile.</summary>
    FireInput = 0x03,

    /// <summary>Demande d'activation du dash.</summary>
    DashInput = 0x04,

    /// <summary>Changement de couleur de vaisseau dans le lobby.</summary>
    ColorSelect = 0x05,

    /// <summary>Demande de lancement d'une partie solo depuis le lobby.</summary>
    StartSoloRequest = 0x06,

    /// <summary>Confirmation client qu'il revient au lobby après GameOver.</summary>
    ReturnToLobbyRequest = 0x07,

    /// <summary>Demande explicite de resynchronisation de l'état lobby.</summary>
    LobbyStateRequest = 0x08,

    // ── Serveur → Client ──────────────────────────────────────────────────────

    /// <summary>Confirmation d'entrée dans le lobby, avec l'identifiant attribué.</summary>
    LobbyJoined = 0x10,

    /// <summary>État complet du lobby (liste des joueurs connectés).</summary>
    LobbyState = 0x11,

    /// <summary>Compte à rebours avant le début de la partie.</summary>
    Countdown = 0x12,

    /// <summary>Snapshot de l'état du jeu (positions, rotations) diffusé à 20 Hz.</summary>
    GameStateSnapshot = 0x13,

    /// <summary>Notification d'élimination d'un joueur.</summary>
    PlayerEliminated = 0x14,

    /// <summary>Fin de partie avec le nom du vainqueur.</summary>
    GameOver = 0x15,

    /// <summary>Mort d'un joueur (envoyé à tous les clients).</summary>
    PlayerKilled = 0x16,
}
