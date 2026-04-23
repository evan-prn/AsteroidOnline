namespace AsteroidOnline.Server.Services;

using System.Numerics;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Domain.Events;
using AsteroidOnline.Domain.World;

/// <summary>
/// Service de spawn et de fragmentation des astéroïdes (US-13, US-14, US-16, US-17).
/// Spawn les astéroïdes aux bords de la map et gère leur fragmentation à la destruction.
/// </summary>
public sealed class AsteroidSpawnService
{
    private readonly Random      _random;
    private readonly WorldBounds _bounds;
    private int _nextId;

    // Probabilité de lâcher un power-up à la destruction (US-17).
    private const float PowerUpDropChance = 0.20f;

    /// <summary>
    /// Initialise le service avec les dimensions du monde.
    /// </summary>
    public AsteroidSpawnService(WorldBounds bounds, int? seed = null)
    {
        _bounds = bounds;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _nextId = 1000; // Plage réservée aux astéroïdes (joueurs : 1–999)
    }

    /// <summary>
    /// Spawne <paramref name="count"/> gros astéroïdes sur les bords de la carte.
    /// Appelé au démarrage d'une partie (10 astéroïdes selon US-13).
    /// </summary>
    public IReadOnlyList<Asteroid> SpawnInitialWave(int count = 10)
    {
        var result = new List<Asteroid>(count);
        for (var i = 0; i < count; i++)
            result.Add(CreateAsteroid(AsteroidSize.Large));
        return result;
    }

    /// <summary>
    /// Spawne une nouvelle vague d'astéroïdes (US-16 : toutes les 30s, +20%).
    /// Retourne les nouveaux astéroïdes à ajouter au monde.
    /// </summary>
    /// <param name="currentCount">Nombre d'astéroïdes actifs actuellement.</param>
    /// <param name="maxAsteroids">Plafond global (80 selon US-16).</param>
    public IReadOnlyList<Asteroid> SpawnWave(int currentCount, int maxAsteroids = 80)
    {
        var toSpawn = (int)(currentCount * 0.20f);
        toSpawn = Math.Clamp(toSpawn, 1, maxAsteroids - currentCount);

        var result = new List<Asteroid>(toSpawn);
        for (var i = 0; i < toSpawn; i++)
            result.Add(CreateAsteroid(AsteroidSize.Large));
        return result;
    }

    /// <summary>
    /// Calcule les fragments issus de la destruction d'un astéroïde (US-14).
    /// Retourne l'événement avec les fragments à spawner et le flag power-up.
    /// </summary>
    /// <param name="asteroid">Astéroïde détruit.</param>
    public AsteroidDestroyedEvent CreateDestroyedEvent(Asteroid asteroid)
    {
        var fragments   = new List<AsteroidFragment>();
        var nextSize    = GetFragmentSize(asteroid.Size);
        var fragmentCount = nextSize.HasValue ? _random.Next(2, 4) : 0; // 2 ou 3 fragments

        for (var i = 0; i < fragmentCount; i++)
        {
            // Déviation angulaire aléatoire par rapport à la vélocité parente
            var angle = (float)(_random.NextDouble() * Math.PI * 2);
            var speed = Asteroid.GetBaseSpeed(nextSize!.Value);
            var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed
                         + asteroid.Velocity * 0.3f; // hérite d'une partie de la vélocité

            fragments.Add(new AsteroidFragment
            {
                Id              = _nextId++,
                Size            = nextSize.Value,
                Position        = asteroid.Position,
                Velocity        = velocity,
                AngularVelocity = (float)((_random.NextDouble() - 0.5) * 2.0),
            });
        }

        // 20% de chance pour les Large et Medium (US-17)
        var dropsPowerUp = asteroid.Size != AsteroidSize.Small
                        && _random.NextDouble() < PowerUpDropChance;

        return new AsteroidDestroyedEvent
        {
            AsteroidId  = asteroid.Id,
            Position    = asteroid.Position,
            NewFragments = fragments,
            DropsPowerUp = dropsPowerUp,
        };
    }

    // ──── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Retourne la taille du fragment fils, ou null si pas de fragmentation.</summary>
    private static AsteroidSize? GetFragmentSize(AsteroidSize size) => size switch
    {
        AsteroidSize.Large  => AsteroidSize.Medium,
        AsteroidSize.Medium => AsteroidSize.Small,
        AsteroidSize.Small  => null,
        _                   => null,
    };

    /// <summary>Crée un astéroïde de la taille spécifiée en le spawnant sur un bord de carte.</summary>
    private Asteroid CreateAsteroid(AsteroidSize size)
    {
        var position = SpawnOnEdge();
        var speed    = Asteroid.GetBaseSpeed(size);

        // Direction vers le centre de la carte avec une légère déviation aléatoire
        var toCenter = new Vector2(_bounds.Width / 2f, _bounds.Height / 2f) - position;
        var baseAngle = MathF.Atan2(toCenter.Y, toCenter.X);
        var deviation = (float)((_random.NextDouble() - 0.5) * Math.PI * 0.6);
        var finalAngle = baseAngle + deviation;

        return new Asteroid
        {
            Id              = _nextId++,
            Size            = size,
            HitPoints       = Asteroid.GetInitialHitPoints(size),
            Position        = position,
            Velocity        = new Vector2(MathF.Cos(finalAngle), MathF.Sin(finalAngle)) * speed,
            AngularVelocity = (float)((_random.NextDouble() - 0.5) * 1.5),
            IsActive        = true,
        };
    }

    /// <summary>
    /// Génère une position aléatoire sur l'un des quatre bords de la carte.
    /// </summary>
    private Vector2 SpawnOnEdge()
    {
        var edge = _random.Next(4);
        return edge switch
        {
            0 => new Vector2((float)(_random.NextDouble() * _bounds.Width), 0f),
            1 => new Vector2((float)(_random.NextDouble() * _bounds.Width), _bounds.Height),
            2 => new Vector2(0f, (float)(_random.NextDouble() * _bounds.Height)),
            _ => new Vector2(_bounds.Width,  (float)(_random.NextDouble() * _bounds.Height)),
        };
    }

    /// <summary>
    /// Crée un astéroïde fragment à partir des données d'un <see cref="AsteroidFragment"/>.
    /// Appelé par la GameLoop après réception d'un <see cref="AsteroidDestroyedEvent"/>.
    /// </summary>
    public static Asteroid CreateFromFragment(AsteroidFragment fragment) => new()
    {
        Id              = fragment.Id,
        Size            = fragment.Size,
        HitPoints       = Asteroid.GetInitialHitPoints(fragment.Size),
        Position        = fragment.Position,
        Velocity        = fragment.Velocity,
        AngularVelocity = fragment.AngularVelocity,
        IsActive        = true,
    };

    /// <summary>RÃ©initialise le gÃ©nÃ©rateur d'identifiants pour une nouvelle manche.</summary>
    public void Reset()
    {
        _nextId = 1000;
    }
}
