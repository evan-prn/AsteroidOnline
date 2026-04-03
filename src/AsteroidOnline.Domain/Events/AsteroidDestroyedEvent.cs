namespace AsteroidOnline.Domain.Events;

using System.Numerics;
using AsteroidOnline.Domain.Entities;

/// <summary>
/// Données d'un fragment généré lors de la fragmentation d'un astéroïde.
/// </summary>
public sealed class AsteroidFragment
{
    /// <summary>Identifiant du nouvel astéroïde fragment.</summary>
    public int Id { get; init; }

    /// <summary>Taille du fragment.</summary>
    public AsteroidSize Size { get; init; }

    /// <summary>Position initiale (héritée du parent).</summary>
    public Vector2 Position { get; init; }

    /// <summary>Vélocité initiale du fragment (parent + déviation aléatoire).</summary>
    public Vector2 Velocity { get; init; }

    /// <summary>Rotation angulaire initiale du fragment.</summary>
    public float AngularVelocity { get; init; }
}

/// <summary>
/// Événement déclenché quand un astéroïde est détruit (US-14, US-17).
/// Contient la liste des fragments fils à spawner et indique si un power-up
/// doit être lâché.
/// </summary>
public sealed class AsteroidDestroyedEvent
{
    /// <summary>Identifiant de l'astéroïde détruit.</summary>
    public int AsteroidId { get; init; }

    /// <summary>Position de la destruction (pour les effets visuels).</summary>
    public Vector2 Position { get; init; }

    /// <summary>
    /// Fragments à spawner suite à la destruction.
    /// Vide si l'astéroïde était de taille <see cref="AsteroidSize.Small"/>.
    /// </summary>
    public IReadOnlyList<AsteroidFragment> NewFragments { get; init; } = [];

    /// <summary>
    /// Indique qu'un power-up doit être spawné à cette position (US-17).
    /// 20% de chance pour Large et Medium.
    /// </summary>
    public bool DropsPowerUp { get; init; }
}
