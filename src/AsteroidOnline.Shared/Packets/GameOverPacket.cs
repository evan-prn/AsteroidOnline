namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet TCP envoyé par le serveur quand il ne reste qu'un joueur en vie (US-25).
/// Déclenche l'affichage de l'écran de fin de partie côté client.
/// </summary>
public class GameOverPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.GameOver;

    /// <summary>Pseudo du vainqueur.</summary>
    public string WinnerName { get; set; } = string.Empty;

    /// <summary>Identifiant du vainqueur.</summary>
    public int WinnerId { get; set; }

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(WinnerId);
        writer.Write(WinnerName);
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        WinnerId   = reader.ReadInt32();
        WinnerName = reader.ReadString();
    }
}
