namespace AsteroidOnline.Domain.Systems;

using System.Numerics;
using AsteroidOnline.Domain.Entities;

/// <summary>
/// Système de gestion des armes (US-09).
/// Contrôle le cooldown de tir et crée les projectiles à chaque tir valide.
/// Exécuté côté serveur autoritaire (US-27).
/// </summary>
public sealed class WeaponSystem
{
    // Cooldown normal entre deux tirs en secondes.
    private const float NormalCooldown = 0.25f;

    // Cooldown en mode RapidFire (US-20, BLOC 4).
    private const float RapidFireCooldown = 0.08f;

    // Décalage de spawn du projectile devant la pointe du vaisseau (en unités).
    private const float ProjectileSpawnOffset = 20f;

    /// <summary>
    /// Met à jour le cooldown de l'arme du vaisseau.
    /// Doit être appelé à chaque tick même si le joueur ne tire pas.
    /// </summary>
    /// <param name="ship">Vaisseau dont le cooldown est mis à jour.</param>
    /// <param name="deltaTime">Durée du tick en secondes.</param>
    public void UpdateCooldown(Ship ship, float deltaTime)
    {
        if (ship.WeaponCooldown > 0f)
            ship.WeaponCooldown = MathF.Max(0f, ship.WeaponCooldown - deltaTime);
    }

    /// <summary>
    /// Tente de créer un projectile si le joueur appuie sur "Feu" et que le cooldown est écoulé.
    /// </summary>
    /// <param name="ship">Vaisseau tirant le projectile.</param>
    /// <param name="fireInput">
    ///   <see langword="true"/> si la touche de tir est pressée ce tick.
    /// </param>
    /// <param name="nextProjectileId">Identifiant à attribuer au projectile créé.</param>
    /// <param name="hasRapidFire">
    ///   <see langword="true"/> si le power-up RapidFire (US-20) est actif.
    /// </param>
    /// <returns>
    ///   Le nouveau <see cref="Projectile"/> si le tir est déclenché,
    ///   <see langword="null"/> sinon.
    /// </returns>
    public Projectile? TryFire(Ship ship, bool fireInput, int nextProjectileId,
        bool hasRapidFire = false)
    {
        // Pas d'input de tir, ou cooldown pas encore écoulé, ou vaisseau mort
        if (!fireInput || ship.WeaponCooldown > 0f || !ship.IsAlive)
            return null;

        // Calcul de la direction de tir (même direction que la poussée)
        var direction = new Vector2(
            MathF.Sin(ship.Rotation),
           -MathF.Cos(ship.Rotation));

        // Position de spawn légèrement devant la pointe du vaisseau
        var spawnPosition = ship.Position + direction * ProjectileSpawnOffset;

        // Application du cooldown selon le mode de tir
        ship.WeaponCooldown = hasRapidFire ? RapidFireCooldown : NormalCooldown;

        var projectile = new Projectile
        {
            Id        = nextProjectileId,
            OwnerId   = ship.Id,
            Position  = spawnPosition,
            Direction = direction,
        };
        // La vélocité utilise Speed du projectile pour rester cohérente si Speed est modifié.
        projectile.Velocity = direction * projectile.Speed + ship.Velocity;
        return projectile;
    }
}
