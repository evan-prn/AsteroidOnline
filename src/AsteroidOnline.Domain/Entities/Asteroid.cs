namespace AsteroidOnline.Domain.Entities;

/// <summary>
/// Astéroïde naviguant dans le monde de jeu (US-13, US-18).
/// Sa taille détermine ses points de vie, son rayon de collision et sa vitesse.
/// À sa destruction, il se fragmente en astéroïdes plus petits (US-14).
/// </summary>
public class Asteroid : PhysicalEntity
{
    /// <summary>Taille de l'astéroïde (Large / Medium / Small).</summary>
    public AsteroidSize Size { get; set; } = AsteroidSize.Large;

    /// <summary>Points de vie restants. 0 = détruit.</summary>
    public int HitPoints { get; set; }

    /// <summary>Indique si l'astéroïde est encore actif dans le monde.</summary>
    public bool IsActive { get; set; } = true;

    // ── Paramètres selon la taille (US-18) ───────────────────────────────────

    /// <summary>Rayon de collision selon la taille.</summary>
    public override float CollisionRadius => Size switch
    {
        AsteroidSize.Large  => 48f,
        AsteroidSize.Medium => 28f,
        AsteroidSize.Small  => 14f,
        _                   => 14f,
    };

    /// <summary>Points de vie initiaux selon la taille.</summary>
    public static int GetInitialHitPoints(AsteroidSize size) => size switch
    {
        AsteroidSize.Large  => 3,
        AsteroidSize.Medium => 2,
        AsteroidSize.Small  => 1,
        _                   => 1,
    };

    /// <summary>Vitesse de base selon la taille (en u/s).</summary>
    public static float GetBaseSpeed(AsteroidSize size) => size switch
    {
        AsteroidSize.Large  =>  60f,
        AsteroidSize.Medium => 100f,
        AsteroidSize.Small  => 160f,
        _                   => 100f,
    };
}
