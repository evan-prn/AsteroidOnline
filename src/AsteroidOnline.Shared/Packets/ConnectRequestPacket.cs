namespace AsteroidOnline.Shared.Packets;

using AsteroidOnline.Domain.Entities;

/// <summary>
/// Paquet envoyé par le client pour demander l'accès au serveur.
/// Contient le pseudo choisi par le joueur et la couleur de son vaisseau.
/// Envoyé en mode fiable (ReliableOrdered) juste après l'établissement de la connexion.
/// </summary>
public class ConnectRequestPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.ConnectRequest;

    /// <summary>Pseudo du joueur (1 à 20 caractères, non vide).</summary>
    public string Pseudo { get; set; } = string.Empty;

    /// <summary>Couleur de vaisseau choisie par le joueur dans le lobby.</summary>
    public PlayerColor Color { get; set; } = PlayerColor.Bleu;

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Pseudo);
        writer.Write((byte)Color);
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        Pseudo = reader.ReadString();
        Color  = (PlayerColor)reader.ReadByte();
    }
}
