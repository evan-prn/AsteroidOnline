namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet envoyé par le serveur chaque seconde pendant le compte à rebours pré-partie.
/// Le client affiche une animation "3... 2... 1... GO !" en réponse (US-06).
/// </summary>
public class CountdownPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.Countdown;

    /// <summary>
    /// Secondes restantes avant le début de la partie.
    /// La valeur 0 signifie "GO !" et déclenche la transition vers l'écran de jeu.
    /// </summary>
    public int SecondsRemaining { get; set; }

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(SecondsRemaining);
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        SecondsRemaining = reader.ReadInt32();
    }
}
