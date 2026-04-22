namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet client -&gt; serveur pour demander le lancement d'une manche solo
/// depuis le lobby quand un seul joueur est connecté.
/// </summary>
public sealed class StartSoloRequestPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.StartSoloRequest;

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        // Aucun payload nécessaire pour cette commande.
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        // Aucun payload à lire.
    }
}
