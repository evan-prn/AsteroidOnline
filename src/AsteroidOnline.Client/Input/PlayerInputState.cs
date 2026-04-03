namespace AsteroidOnline.Client.Input;

/// <summary>
/// Snapshot immutable de l'état courant des touches de jeu (US-07).
/// Produit par <see cref="InputHandler"/> à chaque frame et converti en
/// <see cref="AsteroidOnline.Shared.Packets.PlayerInputPacket"/> avant envoi UDP.
/// </summary>
public readonly struct PlayerInputState
{
    /// <summary>W ou flèche Haut — poussée avant.</summary>
    public bool ThrustForward  { get; init; }

    /// <summary>A ou flèche Gauche — rotation antihoraire.</summary>
    public bool RotateLeft     { get; init; }

    /// <summary>D ou flèche Droite — rotation horaire.</summary>
    public bool RotateRight    { get; init; }

    /// <summary>Espace ou F — tir d'un projectile (US-09).</summary>
    public bool Fire           { get; init; }

    /// <summary>Shift ou E — activation du dash (US-11).</summary>
    public bool Dash           { get; init; }

    /// <summary>Indique si au moins une touche est active.</summary>
    public bool IsAnyKeyPressed =>
        ThrustForward || RotateLeft || RotateRight || Fire || Dash;
}
