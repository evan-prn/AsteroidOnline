namespace AsteroidOnline.Domain.Entities;

using System.Numerics;

/// <summary>
/// Classe de base pour toutes les entités soumises à la physique du jeu :
/// vaisseaux, astéroïdes, projectiles.
/// Regroupe les propriétés cinématiques manipulées par <see cref="../Systems/PhysicsSystem"/>.
/// </summary>
public abstract class PhysicalEntity
{
    /// <summary>Identifiant unique de l'entité (attribué par le serveur).</summary>
    public int Id { get; set; }

    /// <summary>
    /// Position dans l'espace de jeu en unités (origin = coin supérieur gauche).
    /// Modifiée à chaque tick par <see cref="../Systems/PhysicsSystem"/>.
    /// </summary>
    public Vector2 Position { get; set; }

    /// <summary>
    /// Vecteur de vélocité en unités/seconde.
    /// L'amortissement (drag 0.99) réduit progressivement cette valeur à chaque tick.
    /// </summary>
    public Vector2 Velocity { get; set; }

    /// <summary>
    /// Angle de rotation en radians. 0 = pointe vers le haut (axe Y négatif).
    /// Augmente dans le sens horaire.
    /// </summary>
    public float Rotation { get; set; }

    /// <summary>
    /// Vitesse angulaire en radians/seconde (non utilisée pour les vaisseaux contrôlés,
    /// mais appliquée aux astéroïdes et aux débris).
    /// </summary>
    public float AngularVelocity { get; set; }

    /// <summary>
    /// Rayon de collision en unités de jeu.
    /// Utilisé par <see cref="../Systems/CollisionSystem"/> pour la détection cercle-cercle.
    /// </summary>
    public abstract float CollisionRadius { get; }
}
