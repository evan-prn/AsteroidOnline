namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet TCP envoyé à tous les clients quand un joueur est éliminé (US-24).
/// Affiché dans le feed d'événements HUD pendant 4 secondes.
/// </summary>
public class PlayerEliminatedPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.PlayerEliminated;

    /// <summary>Pseudo du joueur éliminé.</summary>
    public string VictimName { get; set; } = string.Empty;

    /// <summary>Pseudo du joueur responsable de l'élimination.</summary>
    public string KillerName { get; set; } = string.Empty;

    /// <summary>Identifiant du joueur éliminé (pour masquer son vaisseau).</summary>
    public int VictimId { get; set; }

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(VictimId);
        writer.Write(VictimName);
        writer.Write(KillerName);
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        VictimId   = reader.ReadInt32();
        VictimName = reader.ReadString();
        KillerName = reader.ReadString();
    }
}
