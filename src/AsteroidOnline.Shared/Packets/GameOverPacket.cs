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

    /// <summary>Vrai si la manche terminée était en mode solo.</summary>
    public bool IsSoloMode { get; set; }

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(WinnerId);
        writer.Write(WinnerName);
        writer.Write(IsSoloMode);
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        WinnerId   = reader.ReadInt32();
        WinnerName = reader.ReadString();
        IsSoloMode = reader.ReadBoolean();
    }
}
