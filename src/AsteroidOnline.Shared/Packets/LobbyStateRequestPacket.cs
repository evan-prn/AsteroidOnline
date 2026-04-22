namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet client -> serveur pour demander explicitement
/// la dernière version de l'état du lobby.
/// </summary>
public sealed class LobbyStateRequestPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.LobbyStateRequest;

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        // Commande sans payload.
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        // Aucun champ à lire.
    }
}
