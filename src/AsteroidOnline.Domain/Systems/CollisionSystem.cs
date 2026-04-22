namespace AsteroidOnline.Domain.Systems;

using System.Numerics;
using AsteroidOnline.Domain.Entities;

/// <summary>
/// Système de détection de collisions cercle-cercle (US-15, US-22).
/// Exécuté côté serveur autoritaire à chaque tick de la boucle de jeu (US-27).
/// Génère des événements consommés par la couche serveur pour mettre à jour
/// l'état du monde et diffuser les notifications aux clients.
/// </summary>
public sealed class CollisionSystem
{
    // ──── Collision Projectile ↔ Joueur (US-22) ───────────────────────────────

    /// <summary>
    /// Teste si un projectile touche l'un des vaisseaux de la liste.
    /// Le propriétaire du projectile est exclu (pas d'auto-tir).
    /// </summary>
    /// <param name="projectile">Projectile à tester.</param>
    /// <param name="ships">Liste des vaisseaux en vie.</param>
    /// <returns>Le vaisseau touché, ou <see langword="null"/>.</returns>
    public Ship? CheckProjectileVsShips(Projectile projectile, IEnumerable<Ship> ships)
    {
        if (!projectile.IsActive)
            return null;

        foreach (var ship in ships)
        {
            // Pas de collision avec soi-même, ni avec un vaisseau déjà mort.
            if (ship.Id == projectile.OwnerId || !ship.IsAlive)
                continue;

            if (Overlaps(projectile.Position, projectile.CollisionRadius,
                         ship.Position,       ship.CollisionRadius))
                return ship;
        }

        return null;
    }

    // ──── Collision Astéroïde ↔ Joueur (US-15) ────────────────────────────────

    /// <summary>
    /// Teste si un astéroïde entre en collision avec l'un des vaisseaux.
    /// </summary>
    /// <param name="asteroid">Astéroïde à tester.</param>
    /// <param name="ships">Liste des vaisseaux en vie.</param>
    /// <returns>Le vaisseau touché, ou <see langword="null"/>.</returns>
    public Ship? CheckAsteroidVsShip(Asteroid asteroid, IEnumerable<Ship> ships)
    {
        if (!asteroid.IsActive)
            return null;

        foreach (var ship in ships)
        {
            if (!ship.IsAlive)
                continue;

            if (Overlaps(asteroid.Position, asteroid.CollisionRadius,
                         ship.Position,     ship.CollisionRadius))
                return ship;
        }

        return null;
    }

    // ──── Collision Projectile ↔ Astéroïde ────────────────────────────────────

    /// <summary>
    /// Teste si un projectile touche l'un des astéroïdes actifs.
    /// </summary>
    /// <param name="projectile">Projectile à tester.</param>
    /// <param name="asteroids">Liste des astéroïdes actifs.</param>
    /// <returns>L'astéroïde touché, ou <see langword="null"/>.</returns>
    public Asteroid? CheckProjectileVsAsteroids(Projectile projectile,
        IEnumerable<Asteroid> asteroids)
    {
        if (!projectile.IsActive)
            return null;

        foreach (var asteroid in asteroids)
        {
            if (!asteroid.IsActive)
                continue;

            if (Overlaps(projectile.Position, projectile.CollisionRadius,
                         asteroid.Position,   asteroid.CollisionRadius))
                return asteroid;
        }

        return null;
    }

    // ──── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Test de chevauchement de deux cercles.
    /// </summary>
    private static bool Overlaps(Vector2 posA, float radiusA, Vector2 posB, float radiusB)
    {
        var combinedRadius = radiusA + radiusB;
        // Comparaison sur les carrés pour éviter le sqrt coûteux.
        return Vector2.DistanceSquared(posA, posB) < combinedRadius * combinedRadius;
    }
}
