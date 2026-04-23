namespace AsteroidOnline.Shared.Packets;

using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Domain.World;

/// <summary>
/// Snapshot d'un vaisseau joueur inclus dans <see cref="GameStateSnapshotPacket"/>.
/// </summary>
public sealed class PlayerSnapshot
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public PlayerColor Color { get; set; }
    public bool IsAlive { get; set; }
    public float DashCooldownProgress { get; set; }
    public int Score { get; set; }
    public int LivesRemaining { get; set; }
    public bool IsInvulnerable { get; set; }
    public float InvulnerabilityRemaining { get; set; }
}

/// <summary>
/// Snapshot d'un asteroide inclus dans <see cref="GameStateSnapshotPacket"/>.
/// </summary>
public sealed class AsteroidSnapshot
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }
    public AsteroidSize Size { get; set; }
    public int HitPoints { get; set; }
}

/// <summary>
/// Snapshot d'un projectile actif inclus dans <see cref="GameStateSnapshotPacket"/>.
/// </summary>
public sealed class ProjectileSnapshot
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int OwnerId { get; set; }
}

/// <summary>
/// Snapshot UDP compact du monde.
/// Les positions et vitesses sont quantifiees pour rester sous la limite de paquet LiteNetLib.
/// </summary>
public class GameStateSnapshotPacket : IPacket
{
    private const float MaxEncodedVelocity = 1024f;

    public PacketType Type => PacketType.GameStateSnapshot;

    public long ServerTimestamp { get; set; }
    public int AlivePlayersCount { get; set; }
    public List<PlayerSnapshot> Players { get; set; } = new();
    public List<AsteroidSnapshot> Asteroids { get; set; } = new();
    public List<ProjectileSnapshot> Projectiles { get; set; } = new();

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(ServerTimestamp);
        writer.Write((byte)Math.Clamp(AlivePlayersCount, 0, byte.MaxValue));

        writer.Write((byte)Math.Clamp(Players.Count, 0, byte.MaxValue));
        foreach (var p in Players)
        {
            writer.Write((byte)Math.Clamp(p.Id, 0, byte.MaxValue));
            writer.Write(QuantizePosition(p.X, WorldBounds.Default.Width));
            writer.Write(QuantizePosition(p.Y, WorldBounds.Default.Height));
            writer.Write(QuantizeAngle(p.Rotation));
            writer.Write(QuantizeVelocity(p.VelocityX));
            writer.Write(QuantizeVelocity(p.VelocityY));
            writer.Write((byte)p.Color);

            var flags = (byte)0;
            if (p.IsAlive)
                flags |= 0x01;
            if (p.IsInvulnerable)
                flags |= 0x02;
            writer.Write(flags);

            writer.Write((byte)Math.Clamp((int)MathF.Round(p.DashCooldownProgress * 255f), 0, 255));
            writer.Write((ushort)Math.Clamp(p.Score, 0, ushort.MaxValue));
            writer.Write((byte)Math.Clamp(p.LivesRemaining, 0, byte.MaxValue));
            writer.Write((byte)Math.Clamp((int)MathF.Round(p.InvulnerabilityRemaining * 10f), 0, byte.MaxValue));
        }

        writer.Write((byte)Math.Clamp(Asteroids.Count, 0, byte.MaxValue));
        foreach (var asteroid in Asteroids)
        {
            writer.Write((ushort)Math.Clamp(asteroid.Id, 0, ushort.MaxValue));
            writer.Write(QuantizePosition(asteroid.X, WorldBounds.Default.Width));
            writer.Write(QuantizePosition(asteroid.Y, WorldBounds.Default.Height));
            writer.Write(QuantizeAngle(asteroid.Rotation));
            writer.Write((byte)asteroid.Size);
            writer.Write((byte)Math.Clamp(asteroid.HitPoints, 0, byte.MaxValue));
        }

        writer.Write((byte)Math.Clamp(Projectiles.Count, 0, byte.MaxValue));
        foreach (var projectile in Projectiles)
        {
            writer.Write((ushort)Math.Clamp(projectile.Id, 0, ushort.MaxValue));
            writer.Write(QuantizePosition(projectile.X, WorldBounds.Default.Width));
            writer.Write(QuantizePosition(projectile.Y, WorldBounds.Default.Height));
            writer.Write((byte)Math.Clamp(projectile.OwnerId, 0, byte.MaxValue));
        }
    }

    public void Deserialize(BinaryReader reader)
    {
        ServerTimestamp = reader.ReadInt64();
        AlivePlayersCount = reader.ReadByte();

        var playerCount = reader.ReadByte();
        Players.Clear();
        for (var i = 0; i < playerCount; i++)
        {
            var id = reader.ReadByte();
            var x = DequantizePosition(reader.ReadUInt16(), WorldBounds.Default.Width);
            var y = DequantizePosition(reader.ReadUInt16(), WorldBounds.Default.Height);
            var rotation = DequantizeAngle(reader.ReadUInt16());
            var velocityX = DequantizeVelocity(reader.ReadInt16());
            var velocityY = DequantizeVelocity(reader.ReadInt16());
            var color = (PlayerColor)reader.ReadByte();
            var flags = reader.ReadByte();
            var dash = reader.ReadByte() / 255f;
            var score = reader.ReadUInt16();
            var lives = reader.ReadByte();
            var invulnerability = reader.ReadByte() / 10f;

            Players.Add(new PlayerSnapshot
            {
                Id = id,
                X = x,
                Y = y,
                Rotation = rotation,
                VelocityX = velocityX,
                VelocityY = velocityY,
                Color = color,
                IsAlive = (flags & 0x01) != 0,
                IsInvulnerable = (flags & 0x02) != 0,
                DashCooldownProgress = dash,
                Score = score,
                LivesRemaining = lives,
                InvulnerabilityRemaining = invulnerability,
            });
        }

        var asteroidCount = reader.ReadByte();
        Asteroids.Clear();
        for (var i = 0; i < asteroidCount; i++)
        {
            Asteroids.Add(new AsteroidSnapshot
            {
                Id = reader.ReadUInt16(),
                X = DequantizePosition(reader.ReadUInt16(), WorldBounds.Default.Width),
                Y = DequantizePosition(reader.ReadUInt16(), WorldBounds.Default.Height),
                Rotation = DequantizeAngle(reader.ReadUInt16()),
                Size = (AsteroidSize)reader.ReadByte(),
                HitPoints = reader.ReadByte(),
            });
        }

        var projectileCount = reader.ReadByte();
        Projectiles.Clear();
        for (var i = 0; i < projectileCount; i++)
        {
            Projectiles.Add(new ProjectileSnapshot
            {
                Id = reader.ReadUInt16(),
                X = DequantizePosition(reader.ReadUInt16(), WorldBounds.Default.Width),
                Y = DequantizePosition(reader.ReadUInt16(), WorldBounds.Default.Height),
                OwnerId = reader.ReadByte(),
            });
        }
    }

    private static ushort QuantizePosition(float value, float worldSize)
    {
        var clamped = Math.Clamp(value, 0f, worldSize);
        var normalized = clamped / worldSize;
        return (ushort)Math.Clamp((int)MathF.Round(normalized * ushort.MaxValue), 0, ushort.MaxValue);
    }

    private static float DequantizePosition(ushort value, float worldSize)
        => value / (float)ushort.MaxValue * worldSize;

    private static ushort QuantizeAngle(float angle)
    {
        var normalized = angle % (MathF.PI * 2f);
        if (normalized < 0f)
            normalized += MathF.PI * 2f;

        return (ushort)Math.Clamp(
            (int)MathF.Round(normalized / (MathF.PI * 2f) * ushort.MaxValue),
            0,
            ushort.MaxValue);
    }

    private static float DequantizeAngle(ushort value)
        => value / (float)ushort.MaxValue * MathF.PI * 2f;

    private static short QuantizeVelocity(float velocity)
    {
        var clamped = Math.Clamp(velocity, -MaxEncodedVelocity, MaxEncodedVelocity);
        return (short)Math.Clamp(
            (int)MathF.Round(clamped / MaxEncodedVelocity * short.MaxValue),
            short.MinValue,
            short.MaxValue);
    }

    private static float DequantizeVelocity(short value)
        => value / (float)short.MaxValue * MaxEncodedVelocity;
}
