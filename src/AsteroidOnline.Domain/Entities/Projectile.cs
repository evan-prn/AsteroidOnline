namespace AsteroidOnline.Domain.Entities;

using System.Numerics;

/// <summary>
/// Projectile tiré par un vaisseau (US-09).
/// Se déplace en ligne droite à vitesse constante et expire après 2 secondes.
/// La collision avec un autre joueur génère un <c>PlayerKilledEvent</c> (US-22).
/// </summary>
public class Projectile : PhysicalEntity
{
    /// <summary>Identifiant du joueur ayant tiré ce projectile.</summary>
    public int OwnerId { get; set; }

    /// <summary>
    /// Direction normalisée du projectile (calculée d'après la rotation du vaisseau au moment du tir).
    /// </summary>
    public Vector2 Direction { get; set; }

    /// <summary>Vitesse de déplacement en unités/seconde.</summary>
    public float Speed { get; set; } = 700f;

    /// <summary>Durée de vie restante en secondes. Le projectile est détruit quand cette valeur atteint 0.</summary>
    public float LifetimeRemaining { get; set; } = 2f;

    /// <summary>
    /// Indique si le projectile est encore actif.
    /// Mis à <see langword="false"/> par le serveur lors d'un impact ou d'une expiration.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ──── PhysicalEntity ──────────────────────────────────────────────────────

    /// <summary>Rayon de collision du projectile : 4 unités.</summary>
    public override float CollisionRadius => 4f;
}
