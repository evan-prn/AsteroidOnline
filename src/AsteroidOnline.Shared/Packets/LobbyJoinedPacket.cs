namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet envoyé par le serveur pour confirmer l'entrée dans le lobby.
/// Le serveur attribue un identifiant unique au joueur à ce moment-là.
/// Déclenche la navigation automatique vers l'écran de lobby côté client (US-02).
/// </summary>
public class LobbyJoinedPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.LobbyJoined;

    /// <summary>Identifiant unique attribué au joueur par le serveur.</summary>
    public int PlayerId { get; set; }

    /// <summary>Message de bienvenue optionnel (ex : "Bienvenue dans le lobby !").</summary>
    public string Message { get; set; } = string.Empty;

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(PlayerId);
        writer.Write(Message);
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        PlayerId = reader.ReadInt32();
        Message  = reader.ReadString();
    }
}
