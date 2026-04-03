namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Contrat commun à tous les paquets réseau du jeu.
/// Définit le type du paquet ainsi que les méthodes de sérialisation/désérialisation
/// utilisées par la couche transport.
/// </summary>
public interface IPacket
{
    /// <summary>Identifiant du type de paquet (préfixe d'un octet sur le fil).</summary>
    PacketType Type { get; }

    /// <summary>
    /// Sérialise le contenu du paquet dans le flux binaire fourni.
    /// </summary>
    /// <param name="writer">Flux d'écriture binaire.</param>
    void Serialize(BinaryWriter writer);

    /// <summary>
    /// Désérialise le contenu du paquet depuis le flux binaire fourni.
    /// </summary>
    /// <param name="reader">Flux de lecture binaire positionné après l'octet de type.</param>
    void Deserialize(BinaryReader reader);
}
