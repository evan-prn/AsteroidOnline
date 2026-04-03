namespace AsteroidOnline.Server.Services;

using System.Numerics;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Domain.World;

/// <summary>
/// Service de spawn aléatoire des joueurs à leur connexion (US-10).
/// Garantit une zone de sécurité de 150 unités autour de chaque position générée :
/// aucun astéroïde ni autre joueur ne doit s'y trouver au moment du spawn.
/// </summary>
public sealed class SpawnService
{
    // Rayon de la zone de sécurité autour du point de spawn.
    private const float SafeZoneRadius = 150f;

    // Nombre maximum de tentatives avant d'utiliser la position la moins dangereuse.
    private const int MaxAttempts = 50;

    // Marge par rapport aux bords du monde pour éviter un spawn exactement sur un bord.
    private const float EdgeMargin = 80f;

    private readonly Random _random;
    private readonly WorldBounds _bounds;

    /// <summary>
    /// Initialise le service de spawn pour les dimensions de monde spécifiées.
    /// </summary>
    /// <param name="bounds">Dimensions du monde de jeu.</param>
    /// <param name="seed">
    ///   Graine du générateur aléatoire. Utiliser <c>null</c> pour un seed non déterministe.
    /// </param>
    public SpawnService(WorldBounds bounds, int? seed = null)
    {
        _bounds = bounds;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Trouve une position de spawn valide pour un nouveau joueur.
    /// La position est tirée aléatoirement dans le monde et validée contre la liste
    /// des entités existantes (astéroïdes et vaisseaux).
    /// </summary>
    /// <param name="existingEntities">
    ///   Collection de toutes les entités déjà présentes dans le monde.
    ///   La méthode s'assure qu'aucune d'entre elles ne se trouve dans le rayon de sécurité.
    /// </param>
    /// <returns>
    ///   Position de spawn valide. Si aucune position sûre n'est trouvée après
    ///   <see cref="MaxAttempts"/> tentatives, retourne la position la plus éloignée trouvée.
    /// </returns>
    public Vector2 FindSpawnPosition(IReadOnlyCollection<PhysicalEntity> existingEntities)
    {
        var bestPosition    = GenerateRandomPosition();
        var bestMinDistance = 0f;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate   = GenerateRandomPosition();
            var minDistance = GetMinDistanceToEntities(candidate, existingEntities);

            // Position sûre trouvée : on l'utilise immédiatement
            if (minDistance >= SafeZoneRadius)
                return candidate;

            // Mémorisation de la meilleure position trouvée jusqu'ici
            if (minDistance > bestMinDistance)
            {
                bestMinDistance = minDistance;
                bestPosition    = candidate;
            }
        }

        // Aucune position parfaitement sûre : on retourne la moins dangereuse
        return bestPosition;
    }

    // ──── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Génère une position aléatoire à l'intérieur des limites du monde,
    /// en respectant la marge par rapport aux bords.
    /// </summary>
    private Vector2 GenerateRandomPosition()
    {
        var x = EdgeMargin + (float)_random.NextDouble() * (_bounds.Width  - 2 * EdgeMargin);
        var y = EdgeMargin + (float)_random.NextDouble() * (_bounds.Height - 2 * EdgeMargin);
        return new Vector2(x, y);
    }

    /// <summary>
    /// Calcule la distance minimale entre un point candidat et toutes les entités existantes.
    /// Retourne <see cref="float.MaxValue"/> si la collection est vide.
    /// </summary>
    private static float GetMinDistanceToEntities(Vector2 candidate,
        IReadOnlyCollection<PhysicalEntity> entities)
    {
        if (entities.Count == 0)
            return float.MaxValue;

        var minDistance = float.MaxValue;
        foreach (var entity in entities)
        {
            var distance = Vector2.Distance(candidate, entity.Position);
            if (distance < minDistance)
                minDistance = distance;
        }
        return minDistance;
    }
}
