namespace AsteroidOnline.Domain.Entities;

/// <summary>
/// Taille d'un astéroïde, qui détermine ses points de vie, son rayon de collision
/// et sa vitesse de déplacement (US-18).
/// </summary>
public enum AsteroidSize : byte
{
    /// <summary>Grand astéroïde : 3 PV, rayon 48 px, 60 u/s.</summary>
    Large  = 0,

    /// <summary>Astéroïde moyen : 2 PV, rayon 28 px, 100 u/s.</summary>
    Medium = 1,

    /// <summary>Petit astéroïde : 1 PV, rayon 14 px, 160 u/s.</summary>
    Small  = 2,
}
