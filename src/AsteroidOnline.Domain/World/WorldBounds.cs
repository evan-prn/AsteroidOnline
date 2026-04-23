namespace AsteroidOnline.Domain.World;

/// <summary>
/// Dimensions du monde de jeu.
/// Utilisé par <see cref="Systems.PhysicsSystem"/> pour le wrap-around toroïdal (US-12)
/// et par <see cref="../Server/Services/SpawnService"/> pour les positions de spawn (US-10).
/// </summary>
public readonly struct WorldBounds
{
    /// <summary>Largeur du monde en unités de jeu.</summary>
    public float Width  { get; init; }

    /// <summary>Hauteur du monde en unités de jeu.</summary>
    public float Height { get; init; }

    /// <summary>Dimensions par défaut : 3200 × 1800 unités (ratio 16:9).</summary>
    public static readonly WorldBounds Default = new() { Width = 3200f, Height = 1800f };
}
