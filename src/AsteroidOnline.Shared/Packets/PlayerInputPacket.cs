namespace AsteroidOnline.Shared.Packets;

/// <summary>
/// Paquet UDP envoyé par le client à chaque frame (≈60 Hz) pour transmettre
/// l'état courant des entrées clavier au serveur autoritaire (US-07).
/// Contient également les intentions de tir (US-09) et de dash (US-11).
/// Les 5 booléens sont compactés dans un seul octet (bitfield) pour minimiser
/// la bande passante sur le canal UDP non fiable.
/// </summary>
public class PlayerInputPacket : IPacket
{
    // ── Masques de bits pour la sérialisation compacte ────────────────────────
    private const byte MaskThrust      = 0x01;
    private const byte MaskRotateLeft  = 0x02;
    private const byte MaskRotateRight = 0x04;
    private const byte MaskFire        = 0x08;
    private const byte MaskDash        = 0x10;

    /// <inheritdoc/>
    public PacketType Type => PacketType.PlayerInput;

    /// <summary>
    /// <see langword="true"/> si la touche de poussée avant est maintenue
    /// (W ou flèche Haut).
    /// </summary>
    public bool ThrustForward { get; set; }

    /// <summary>
    /// <see langword="true"/> si la touche de rotation gauche est maintenue
    /// (A ou flèche Gauche).
    /// </summary>
    public bool RotateLeft { get; set; }

    /// <summary>
    /// <see langword="true"/> si la touche de rotation droite est maintenue
    /// (D ou flèche Droite).
    /// </summary>
    public bool RotateRight { get; set; }

    /// <summary>
    /// <see langword="true"/> si la touche de tir est maintenue
    /// (Espace ou F).
    /// </summary>
    public bool Fire { get; set; }

    /// <summary>
    /// <see langword="true"/> si la touche de dash est pressée ce frame
    /// (Shift ou E) (US-11).
    /// </summary>
    public bool Dash { get; set; }

    /// <summary>
    /// Timestamp client en millisecondes (Stopwatch.GetTimestamp converti).
    /// Permet au serveur de réordonner les paquets arrivés dans le désordre
    /// et d'implémenter la réconciliation d'inputs (US-26).
    /// </summary>
    public long Timestamp { get; set; }

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        // Compactage en un seul octet
        byte flags = 0;
        if (ThrustForward) flags |= MaskThrust;
        if (RotateLeft)    flags |= MaskRotateLeft;
        if (RotateRight)   flags |= MaskRotateRight;
        if (Fire)          flags |= MaskFire;
        if (Dash)          flags |= MaskDash;

        writer.Write(flags);
        writer.Write(Timestamp);
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        var flags = reader.ReadByte();

        ThrustForward = (flags & MaskThrust)      != 0;
        RotateLeft    = (flags & MaskRotateLeft)  != 0;
        RotateRight   = (flags & MaskRotateRight) != 0;
        Fire          = (flags & MaskFire)        != 0;
        Dash          = (flags & MaskDash)        != 0;

        Timestamp = reader.ReadInt64();
    }
}
