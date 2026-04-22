namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet client -&gt; serveur envoyé depuis l'écran de fin de partie
/// pour indiquer que le joueur est prêt à revenir au lobby.
/// </summary>
public sealed class ReturnToLobbyRequestPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.ReturnToLobbyRequest;

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        // Commande sans payload.
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        // Rien à lire.
    }
}
