namespace AsteroidOnline.Domain.Entities;

/// <summary>
/// Bonus collectable par un joueur. La simulation reste cote serveur autoritaire.
/// </summary>
public sealed class PowerUp : PhysicalEntity
{
    public PowerUpType Type { get; set; } = PowerUpType.Laser;

    /// <summary>Duree de vie restante dans le monde, en secondes.</summary>
    public float LifetimeRemaining { get; set; } = 18f;

    public bool IsActive { get; set; } = true;

    public override float CollisionRadius => 18f;
}
