namespace AsteroidOnline.Shared.Packets;

using AsteroidOnline.Domain.Entities;

/// <summary>
/// Snapshot d'un vaisseau joueur inclus dans <see cref="GameStateSnapshotPacket"/>.
/// </summary>
public sealed class PlayerSnapshot
{
    /// <summary>Identifiant unique du joueur.</summary>
    public int Id { get; set; }

    /// <summary>Position X dans le monde (u).</summary>
    public float X { get; set; }

    /// <summary>Position Y dans le monde (u).</summary>
    public float Y { get; set; }

    /// <summary>Angle de rotation en radians.</summary>
    public float Rotation { get; set; }

    /// <summary>Vélocité X (pour l'extrapolation côté client).</summary>
    public float VelocityX { get; set; }

    /// <summary>Vélocité Y (pour l'extrapolation côté client).</summary>
    public float VelocityY { get; set; }

    /// <summary>Couleur du vaisseau.</summary>
    public PlayerColor Color { get; set; }

    /// <summary><see langword="true"/> si le joueur est encore en vie.</summary>
    public bool IsAlive { get; set; }

    /// <summary>Progression de la recharge du dash [0.0 – 1.0].</summary>
    public float DashCooldownProgress { get; set; }
}

/// <summary>
/// Snapshot d'un astéroïde inclus dans <see cref="GameStateSnapshotPacket"/>.
/// </summary>
public sealed class AsteroidSnapshot
{
    /// <summary>Identifiant unique de l'astéroïde.</summary>
    public int Id { get; set; }

    /// <summary>Position X.</summary>
    public float X { get; set; }

    /// <summary>Position Y.</summary>
    public float Y { get; set; }

    /// <summary>Angle de rotation en radians.</summary>
    public float Rotation { get; set; }

    /// <summary>Taille de l'astéroïde (Large / Medium / Small).</summary>
    public AsteroidSize Size { get; set; }

    /// <summary>Points de vie restants.</summary>
    public int HitPoints { get; set; }
}

/// <summary>
/// Snapshot d'un projectile actif inclus dans <see cref="GameStateSnapshotPacket"/>.
/// </summary>
public sealed class ProjectileSnapshot
{
    /// <summary>Identifiant unique du projectile.</summary>
    public int Id { get; set; }

    /// <summary>Position X.</summary>
    public float X { get; set; }

    /// <summary>Position Y.</summary>
    public float Y { get; set; }

    /// <summary>Identifiant du joueur propriétaire (pour la colorisation).</summary>
    public int OwnerId { get; set; }
}

/// <summary>
/// Paquet UDP diffusé par le serveur à 20 Hz (toutes les 3 ticks à 60 Hz).
/// Contient l'état complet du monde : vaisseaux, astéroïdes, projectiles.
/// Le client interpole entre les snapshots reçus pour un rendu fluide (US-23, US-26).
/// </summary>
public class GameStateSnapshotPacket : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => PacketType.GameStateSnapshot;

    /// <summary>Timestamp serveur en millisecondes (pour l'interpolation client).</summary>
    public long ServerTimestamp { get; set; }

    /// <summary>Nombre de joueurs encore en vie (pour le HUD US-29).</summary>
    public int AlivePlayersCount { get; set; }

    /// <summary>Snapshots de tous les vaisseaux (vivants et morts inclus).</summary>
    public List<PlayerSnapshot> Players { get; set; } = new();

    /// <summary>Snapshots de tous les astéroïdes actifs.</summary>
    public List<AsteroidSnapshot> Asteroids { get; set; } = new();

    /// <summary>Snapshots de tous les projectiles actifs.</summary>
    public List<ProjectileSnapshot> Projectiles { get; set; } = new();

    /// <inheritdoc/>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(ServerTimestamp);
        writer.Write(AlivePlayersCount);

        // ── Joueurs ────────────────────────────────────────────────────────
        writer.Write(Players.Count);
        foreach (var p in Players)
        {
            writer.Write(p.Id);
            writer.Write(p.X);
            writer.Write(p.Y);
            writer.Write(p.Rotation);
            writer.Write(p.VelocityX);
            writer.Write(p.VelocityY);
            writer.Write((byte)p.Color);
            writer.Write(p.IsAlive);
            writer.Write(p.DashCooldownProgress);
        }

        // ── Astéroïdes ─────────────────────────────────────────────────────
        writer.Write(Asteroids.Count);
        foreach (var a in Asteroids)
        {
            writer.Write(a.Id);
            writer.Write(a.X);
            writer.Write(a.Y);
            writer.Write(a.Rotation);
            writer.Write((byte)a.Size);
            writer.Write(a.HitPoints);
        }

        // ── Projectiles ────────────────────────────────────────────────────
        writer.Write(Projectiles.Count);
        foreach (var pr in Projectiles)
        {
            writer.Write(pr.Id);
            writer.Write(pr.X);
            writer.Write(pr.Y);
            writer.Write(pr.OwnerId);
        }
    }

    /// <inheritdoc/>
    public void Deserialize(BinaryReader reader)
    {
        ServerTimestamp    = reader.ReadInt64();
        AlivePlayersCount  = reader.ReadInt32();

        // ── Joueurs ────────────────────────────────────────────────────────
        var playerCount = reader.ReadInt32();
        Players.Clear();
        for (var i = 0; i < playerCount; i++)
        {
            Players.Add(new PlayerSnapshot
            {
                Id                   = reader.ReadInt32(),
                X                    = reader.ReadSingle(),
                Y                    = reader.ReadSingle(),
                Rotation             = reader.ReadSingle(),
                VelocityX            = reader.ReadSingle(),
                VelocityY            = reader.ReadSingle(),
                Color                = (PlayerColor)reader.ReadByte(),
                IsAlive              = reader.ReadBoolean(),
                DashCooldownProgress = reader.ReadSingle(),
            });
        }

        // ── Astéroïdes ─────────────────────────────────────────────────────
        var asteroidCount = reader.ReadInt32();
        Asteroids.Clear();
        for (var i = 0; i < asteroidCount; i++)
        {
            Asteroids.Add(new AsteroidSnapshot
            {
                Id        = reader.ReadInt32(),
                X         = reader.ReadSingle(),
                Y         = reader.ReadSingle(),
                Rotation  = reader.ReadSingle(),
                Size      = (AsteroidSize)reader.ReadByte(),
                HitPoints = reader.ReadInt32(),
            });
        }

        // ── Projectiles ────────────────────────────────────────────────────
        var projCount = reader.ReadInt32();
        Projectiles.Clear();
        for (var i = 0; i < projCount; i++)
        {
            Projectiles.Add(new ProjectileSnapshot
            {
                Id      = reader.ReadInt32(),
                X       = reader.ReadSingle(),
                Y       = reader.ReadSingle(),
                OwnerId = reader.ReadInt32(),
            });
        }
    }
}
