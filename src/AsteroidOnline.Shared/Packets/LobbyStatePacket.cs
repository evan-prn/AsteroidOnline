namespace AsteroidOnline.Shared.Packets;

using AsteroidOnline.Domain.Entities;

/// <summary>
/// Informations sur un joueur présent dans le lobby.
/// </summary>
public sealed class LobbyPlayerInfo
{
    /// <summary>Identifiant unique du joueur.</summary>
    public int Id { get; set; }

    /// <summary>Pseudo affiché dans la liste du lobby.</summary>
    public string Pseudo { get; set; } = string.Empty;

    /// <summary>Couleur de vaisseau choisie.</summary>
    public PlayerColor Color { get; set; }

    /// <summary>
    /// Retourne la couleur hexadécimale correspondant à <see cref="Color"/>.
    /// Utilisé côté client pour l'affichage visuel sans dépendance Avalonia dans Shared.
    /// </summary>
    public string ColorHex => Color switch
    {
        PlayerColor.Rouge  => "#FF4444",
        PlayerColor.Bleu   => "#4488FF",
        PlayerColor.Vert   => "#44FF88",
        PlayerColor.Jaune  => "#FFDD44",
        PlayerColor.Violet => "#AA44FF",
        PlayerColor.Orange => "#FF8844",
        _                  => "#FFFFFF",
    };
}

/// <summary>
/// Paquet envoyé par le serveur pour diffuser l'état complet du lobby.
/// Renvoyé à chaque arrivée ou départ d'un joueur, et à chaque changement de couleur.
/// </summary>
public class LobbyStatePacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.LobbyState;

    /// <summary>Liste de tous les joueurs actuellement dans le lobby.</summary>
    public List<LobbyPlayerInfo> Players { get; set; } = new();

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Players.Count);
        foreach (var p in Players)
        {
            writer.Write(p.Id);
            writer.Write(p.Pseudo);
            writer.Write((byte)p.Color);
        }
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        Players.Clear();
        for (var i = 0; i < count; i++)
        {
            Players.Add(new LobbyPlayerInfo
            {
                Id     = reader.ReadInt32(),
                Pseudo = reader.ReadString(),
                Color  = (PlayerColor)reader.ReadByte(),
            });
        }
    }
}
