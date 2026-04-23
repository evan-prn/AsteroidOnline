namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet client -&gt; serveur pour demander le lancement de la manche
/// depuis le lobby. Seul l'hôte est autorisé côté serveur.
/// </summary>
public sealed class StartGameRequestPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.StartGameRequest;

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
