namespace AsteroidOnline.Client.Rendering;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using AsteroidOnline.Client.Services;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Domain.World;
using AsteroidOnline.Shared.Packets;

/// <summary>
/// Renderer gameplay direct via DrawingContext.
/// Evite la recreation de controles Avalonia a chaque frame.
/// </summary>
public sealed class GameRenderer
{
    private readonly GameCanvasControl _surface;
    private readonly IGameAudioService _audioService;
    private readonly Random _random = new();
    private readonly Dictionary<int, Vector2> _previousProjectilePositions = new();
    private readonly Dictionary<int, Vector2> _previousAsteroidPositions = new();
    private readonly Dictionary<int, int> _previousLivesByPlayer = new();
    private readonly List<TransientVfx> _vfx = new();
    private long _lastProcessedEventTimestamp = -1;

    private GameStateSnapshotPacket? _snapshot;
    private IReadOnlyDictionary<int, string> _playerNames = new Dictionary<int, string>();
    private int _localPlayerId;
    private int _cameraPlayerId;
    private float _shakeTimeRemaining;
    private float _shakeIntensity;

    private const float VisibleWorldWidth = 1600f;
    private const float VisibleWorldHeight = 900f;
    private const int MaxTransientVfx = 48;

    private static readonly Typeface LabelTypeface = new("Cascadia Mono");
    private static readonly IBrush WhiteBrush = Brushes.White;
    private static readonly IBrush TransparentBrush = Brushes.Transparent;
    private static readonly IBrush ProjectileBrush = new SolidColorBrush(Color.Parse("#FFF385"));
    private static readonly IBrush RadarAsteroidBrush = new SolidColorBrush(Color.Parse("#FFAE6B"));
    private static readonly IBrush RadarLocalPlayerBrush = new SolidColorBrush(Color.Parse("#7BFF7E"));
    private static readonly IBrush RadarRemotePlayerBrush = new SolidColorBrush(Color.Parse("#E9F2FF"));
    private static readonly Pen ProjectileGlowPen = new(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 0.8);
    private static readonly Pen ProjectileTrailPen = new(new SolidColorBrush(Color.FromArgb(150, 255, 243, 133)), 1.4);
    private static readonly Pen AsteroidPen = new(new SolidColorBrush(Color.FromArgb(180, 255, 222, 180)), 1.2);
    private static readonly Pen RadarFramePen = new(new SolidColorBrush(Color.FromArgb(180, 255, 199, 109)), 1.2);
    private static readonly IBrush RadarBackgroundBrush = new SolidColorBrush(Color.FromArgb(95, 8, 13, 25));
    private static readonly IReadOnlyDictionary<PlayerColor, Color> ShipColors =
        new Dictionary<PlayerColor, Color>
        {
            { PlayerColor.Rouge,  Color.Parse("#FF5B4A") },
            { PlayerColor.Bleu,   Color.Parse("#4AA7FF") },
            { PlayerColor.Vert,   Color.Parse("#52FFAA") },
            { PlayerColor.Jaune,  Color.Parse("#FFD84A") },
            { PlayerColor.Violet, Color.Parse("#C06CFF") },
            { PlayerColor.Orange, Color.Parse("#FF9C4A") },
        };

    public GameRenderer(GameCanvasControl surface, IGameAudioService audioService)
    {
        _surface = surface;
        _audioService = audioService;
        _surface.AttachRenderer(this);
    }

    public void Render(
        GameStateSnapshotPacket snapshot,
        int localPlayerId,
        int cameraPlayerId,
        IReadOnlyDictionary<int, string> playerNames)
    {
        _snapshot = snapshot;
        _localPlayerId = localPlayerId;
        _cameraPlayerId = cameraPlayerId;
        _playerNames = playerNames;

        if (snapshot.ServerTimestamp != _lastProcessedEventTimestamp)
        {
            UpdateTransientVfx(snapshot, localPlayerId);
            _lastProcessedEventTimestamp = snapshot.ServerTimestamp;
        }

        TickVfx(1f / 60f);
        _surface.InvalidateVisual();
    }

    public void RenderFrame(DrawingContext context, Size surfaceSize)
    {
        if (_snapshot is null || surfaceSize.Width <= 0 || surfaceSize.Height <= 0)
            return;

        var bounds = WorldBounds.Default;
        var cameraShip = FindAlivePlayer(_snapshot, _cameraPlayerId);
        var cameraPos = cameraShip is null
            ? new Vector2(bounds.Width / 2f, bounds.Height / 2f)
            : new Vector2(cameraShip.X, cameraShip.Y);

        var scale = Math.Min(surfaceSize.Width / VisibleWorldWidth, surfaceSize.Height / VisibleWorldHeight);
        var shake = GetCameraShakeOffset();
        var screenCenter = new Point((surfaceSize.Width / 2.0) + shake.X, (surfaceSize.Height / 2.0) + shake.Y);

        foreach (var asteroid in _snapshot.Asteroids)
            DrawAsteroid(context, asteroid, cameraPos, scale, screenCenter, bounds, surfaceSize);

        foreach (var projectile in _snapshot.Projectiles)
            DrawProjectile(context, projectile, cameraPos, scale, screenCenter, bounds, surfaceSize);

        foreach (var ship in _snapshot.Players)
            DrawShip(
                context,
                ship,
                cameraPos,
                scale,
                screenCenter,
                bounds,
                surfaceSize,
                ship.Id == _localPlayerId,
                ship.Id == _cameraPlayerId);

        DrawRadar(context, _snapshot, _localPlayerId, surfaceSize);
        DrawVfx(context, cameraPos, scale, screenCenter, bounds, surfaceSize);
    }

    private void DrawShip(DrawingContext context, PlayerSnapshot ship, Vector2 cameraPos, double scale,
        Point screenCenter, in WorldBounds bounds, Size surfaceSize, bool isLocal, bool isCameraTarget)
    {
        if (!ship.IsAlive)
            return;

        var center = ToScreen(new Vector2(ship.X, ship.Y), cameraPos, scale, screenCenter, bounds);
        var size = 16.0 * scale;
        if (!IsOnScreen(center, size * 3, surfaceSize))
            return;

        var nose = Rotate(0, -size, ship.Rotation);
        var leftWing = Rotate(-size * 0.7, size * 0.8, ship.Rotation);
        var rightWing = Rotate(size * 0.7, size * 0.8, ship.Rotation);
        var fillColor = ShipColors.TryGetValue(ship.Color, out var c) ? c : Colors.White;
        var blinkFactor = ship.IsInvulnerable
            ? (Math.Sin(Environment.TickCount64 / 80.0) * 0.5) + 0.5
            : 1.0;
        var alpha = (byte)(ship.IsInvulnerable ? 110 + (blinkFactor * 145) : 255);
        var shipBrush = new SolidColorBrush(Color.FromArgb(alpha, fillColor.R, fillColor.G, fillColor.B));
        var stroke = isLocal
            ? new Pen(WhiteBrush, 1.8)
            : new Pen(new SolidColorBrush(Color.FromArgb(170, 234, 243, 255)), 1.0);

        DrawPolygon(context, shipBrush, stroke, stackalloc Point[]
        {
            new(center.X + nose.X, center.Y + nose.Y),
            new(center.X + leftWing.X, center.Y + leftWing.Y),
            new(center.X + rightWing.X, center.Y + rightWing.Y),
        });

        if (ship.IsInvulnerable)
        {
            var pulse = 1.15 + (blinkFactor * 0.5);
            var haloRadius = size * pulse * 1.4;
            context.DrawEllipse(
                TransparentBrush,
                new Pen(new SolidColorBrush(Color.FromArgb(130, 255, 247, 116)), 1.4),
                center,
                haloRadius,
                haloRadius);
        }

        if (isLocal || isCameraTarget)
        {
            var radius = size * 1.55;
            context.DrawEllipse(
                TransparentBrush,
                new Pen(new SolidColorBrush(isLocal
                    ? Color.FromArgb(80, 255, 255, 255)
                    : Color.FromArgb(120, 255, 199, 109)), 1.0),
                center,
                radius,
                radius);
        }

        var speed = MathF.Sqrt((ship.VelocityX * ship.VelocityX) + (ship.VelocityY * ship.VelocityY));
        if (speed > 60f)
            DrawEngineTrail(context, center, ship.Rotation, size, scale);

        DrawPlayerName(context, center, size, ResolvePlayerName(_playerNames, ship.Id), isLocal, ship.Color);
    }

    private void DrawPlayerName(DrawingContext context, Point center, double shipSize, string playerName, bool isLocal, PlayerColor color)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return;

        var accent = ShipColors.TryGetValue(color, out var c) ? c : Colors.White;
        var text = CreateText(playerName, isLocal ? 12 : 11, WhiteBrush);
        var width = text.Width + 12;
        var height = text.Height + 4;
        var left = center.X - (width / 2);
        var top = center.Y - (shipSize * 2.2) - height;
        var rect = new Rect(left, top, width, height);

        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(isLocal ? (byte)190 : (byte)150, 7, 10, 18)),
            new Pen(new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B)), 1),
            rect,
            6,
            6);
        context.DrawText(text, new Point(left + 6, top + 2));
    }

    private void DrawEngineTrail(DrawingContext context, Point center, float rotation, double shipSize, double scale)
    {
        var left = Rotate(-shipSize * 0.28, shipSize * 1.1, rotation);
        var right = Rotate(shipSize * 0.28, shipSize * 1.1, rotation);
        var tail = Rotate(0, shipSize * (1.6 + (_random.NextDouble() * 0.35)), rotation);

        DrawPolygon(
            context,
            new SolidColorBrush(Color.FromArgb(200, 255, 151, 64)),
            new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 230, 140)), Math.Max(1.0, scale)),
            stackalloc Point[]
            {
                new(center.X + left.X, center.Y + left.Y),
                new(center.X + tail.X, center.Y + tail.Y),
                new(center.X + right.X, center.Y + right.Y),
            });
    }

    private void DrawAsteroid(DrawingContext context, AsteroidSnapshot asteroid, Vector2 cameraPos, double scale,
        Point screenCenter, in WorldBounds bounds, Size surfaceSize)
    {
        var center = ToScreen(new Vector2(asteroid.X, asteroid.Y), cameraPos, scale, screenCenter, bounds);
        var radius = asteroid.Size switch
        {
            AsteroidSize.Large => 48.0 * scale,
            AsteroidSize.Medium => 28.0 * scale,
            _ => 14.0 * scale,
        };

        if (!IsOnScreen(center, radius * 2.2, surfaceSize))
            return;

        var baseColor = asteroid.Size switch
        {
            AsteroidSize.Large => Color.Parse("#AA8A5A"),
            AsteroidSize.Medium => Color.Parse("#8D6F48"),
            _ => Color.Parse("#6E5338"),
        };

        Span<Point> points = stackalloc Point[8];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = asteroid.Rotation + (i * Math.PI * 2 / points.Length);
            var jagged = radius * (0.78 + (0.24 * Math.Abs(Math.Sin(i * asteroid.Id * 1.17))));
            points[i] = new Point(
                center.X + (Math.Cos(angle) * jagged),
                center.Y + (Math.Sin(angle) * jagged));
        }

        DrawPolygon(context, new SolidColorBrush(baseColor), AsteroidPen, points);
    }

    private void DrawProjectile(DrawingContext context, ProjectileSnapshot projectile, Vector2 cameraPos, double scale,
        Point screenCenter, in WorldBounds bounds, Size surfaceSize)
    {
        var pos = new Vector2(projectile.X, projectile.Y);
        var center = ToScreen(pos, cameraPos, scale, screenCenter, bounds);
        var radius = 3.6 * scale;
        if (!IsOnScreen(center, radius * 2.4, surfaceSize))
            return;

        if (_previousProjectilePositions.TryGetValue(projectile.Id, out var prev))
        {
            var trailStart = ToScreen(prev, cameraPos, scale, screenCenter, bounds);
            context.DrawLine(ProjectileTrailPen, trailStart, center);
        }

        context.DrawEllipse(ProjectileBrush, ProjectileGlowPen, center, radius, radius);
    }

    private void DrawRadar(DrawingContext context, GameStateSnapshotPacket snapshot, int localPlayerId, Size surfaceSize)
    {
        const double radarSize = 130;
        const double padding = 16;
        var left = surfaceSize.Width - radarSize - padding;
        var top = surfaceSize.Height - radarSize - padding;
        var radarRect = new Rect(left, top, radarSize, radarSize);

        context.DrawRectangle(RadarBackgroundBrush, RadarFramePen, radarRect, 65, 65);

        foreach (var asteroid in snapshot.Asteroids)
        {
            var p = ToRadarPoint(asteroid.X, asteroid.Y, left, top, radarSize);
            context.DrawEllipse(RadarAsteroidBrush, null, p, 1.6, 1.6);
        }

        foreach (var player in snapshot.Players)
        {
            if (!player.IsAlive)
                continue;

            var p = ToRadarPoint(player.X, player.Y, left, top, radarSize);
            var brush = player.Id == localPlayerId ? RadarLocalPlayerBrush : RadarRemotePlayerBrush;
            var dotSize = player.Id == localPlayerId ? 1.9 : 1.4;
            context.DrawEllipse(brush, null, p, dotSize, dotSize);
        }
    }

    private void DrawVfx(DrawingContext context, Vector2 cameraPos, double scale, Point screenCenter, in WorldBounds bounds, Size surfaceSize)
    {
        foreach (var vfx in _vfx)
        {
            var center = ToScreen(vfx.Position, cameraPos, scale, screenCenter, bounds);
            var t = Math.Clamp(vfx.Age / vfx.Duration, 0f, 1f);
            var radius = vfx.RadiusStart + ((vfx.RadiusEnd - vfx.RadiusStart) * t);
            var alpha = (byte)(vfx.BaseColor.A * (1f - t));

            if (!IsOnScreen(center, radius * scale * 3, surfaceSize))
                continue;

            var scaledRadius = radius * scale;
            context.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(alpha, vfx.BaseColor.R, vfx.BaseColor.G, vfx.BaseColor.B)),
                new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, alpha + 35), 255, 255, 255)), 1),
                center,
                scaledRadius,
                scaledRadius);
        }
    }

    private void UpdateTransientVfx(GameStateSnapshotPacket snapshot, int localPlayerId)
    {
        foreach (var previous in _previousProjectilePositions)
        {
            if (!ContainsProjectile(snapshot, previous.Key))
                AddVfx(previous.Value, Color.FromArgb(200, 255, 215, 106), 0.20f, 6f, 28f);
        }
        _previousProjectilePositions.Clear();
        foreach (var projectile in snapshot.Projectiles)
            _previousProjectilePositions[projectile.Id] = new Vector2(projectile.X, projectile.Y);

        foreach (var previous in _previousAsteroidPositions)
        {
            if (!ContainsAsteroid(snapshot, previous.Key))
            {
                AddVfx(previous.Value, Color.FromArgb(220, 255, 125, 82), 0.35f, 16f, 80f);
                _audioService.PlayAsteroidExplosion();
            }
        }
        _previousAsteroidPositions.Clear();
        foreach (var asteroid in snapshot.Asteroids)
            _previousAsteroidPositions[asteroid.Id] = new Vector2(asteroid.X, asteroid.Y);

        foreach (var player in snapshot.Players)
        {
            if (_previousLivesByPlayer.TryGetValue(player.Id, out var previousLives)
                && player.LivesRemaining < previousLives)
            {
                var pos = new Vector2(player.X, player.Y);
                AddVfx(pos, Color.FromArgb(225, 255, 241, 100), 0.28f, 14f, 62f);
                if (player.Id == localPlayerId)
                    TriggerShake(0.22f, 8f);
            }

            _previousLivesByPlayer[player.Id] = player.LivesRemaining;
        }
    }

    private void AddVfx(Vector2 position, Color color, float duration, float radiusStart, float radiusEnd)
    {
        if (_vfx.Count >= MaxTransientVfx)
            _vfx.RemoveAt(0);

        _vfx.Add(new TransientVfx
        {
            Position = position,
            BaseColor = color,
            Duration = duration,
            RadiusStart = radiusStart,
            RadiusEnd = radiusEnd,
            Age = 0f,
        });
    }

    private void TickVfx(float dt)
    {
        for (var i = _vfx.Count - 1; i >= 0; i--)
        {
            var v = _vfx[i];
            v.Age += dt;
            if (v.Age >= v.Duration)
            {
                _vfx.RemoveAt(i);
                continue;
            }
            _vfx[i] = v;
        }

        if (_shakeTimeRemaining <= 0f)
            return;

        _shakeTimeRemaining = MathF.Max(0f, _shakeTimeRemaining - dt);
        if (_shakeTimeRemaining <= 0f)
            _shakeIntensity = 0f;
    }

    private void TriggerShake(float duration, float intensity)
    {
        _shakeTimeRemaining = MathF.Max(_shakeTimeRemaining, duration);
        _shakeIntensity = MathF.Max(_shakeIntensity, intensity);
    }

    private Vector2 GetCameraShakeOffset()
    {
        if (_shakeTimeRemaining <= 0f || _shakeIntensity <= 0f)
            return Vector2.Zero;

        var falloff = Math.Clamp(_shakeTimeRemaining / 0.25f, 0f, 1f);
        var amount = _shakeIntensity * falloff;
        return new Vector2(
            ((float)_random.NextDouble() * 2f - 1f) * amount,
            ((float)_random.NextDouble() * 2f - 1f) * amount);
    }

    private static void DrawPolygon(DrawingContext context, IBrush brush, Pen? pen, ReadOnlySpan<Point> points)
    {
        if (points.Length == 0)
            return;

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(points[0], true);
            for (var i = 1; i < points.Length; i++)
                geometryContext.LineTo(points[i]);
            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(brush, pen, geometry);
    }

    private static FormattedText CreateText(string text, double fontSize, IBrush brush)
        => new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            fontSize,
            brush);

    private static Point ToRadarPoint(float worldX, float worldY, double left, double top, double size)
    {
        var nx = worldX / WorldBounds.Default.Width;
        var ny = worldY / WorldBounds.Default.Height;
        return new Point(left + (nx * size), top + (ny * size));
    }

    private static Point ToScreen(Vector2 worldPos, Vector2 cameraPos, double scale, Point screenCenter,
        in WorldBounds bounds)
    {
        var delta = WrappedDelta(worldPos, cameraPos, bounds);
        return new Point(screenCenter.X + (delta.X * scale), screenCenter.Y + (delta.Y * scale));
    }

    private static Vector2 WrappedDelta(Vector2 target, Vector2 origin, in WorldBounds bounds)
    {
        var dx = target.X - origin.X;
        var dy = target.Y - origin.Y;

        if (MathF.Abs(dx) > bounds.Width / 2f)
            dx -= MathF.Sign(dx) * bounds.Width;
        if (MathF.Abs(dy) > bounds.Height / 2f)
            dy -= MathF.Sign(dy) * bounds.Height;

        return new Vector2(dx, dy);
    }

    private static bool IsOnScreen(Point p, double margin, Size surfaceSize)
        => p.X >= -margin
           && p.Y >= -margin
           && p.X <= surfaceSize.Width + margin
           && p.Y <= surfaceSize.Height + margin;

    private static (double X, double Y) Rotate(double x, double y, float angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return (x * cos - y * sin, x * sin + y * cos);
    }

    private static string ResolvePlayerName(IReadOnlyDictionary<int, string> playerNames, int playerId)
        => playerNames.TryGetValue(playerId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : $"Joueur{playerId}";

    private static PlayerSnapshot? FindAlivePlayer(GameStateSnapshotPacket snapshot, int playerId)
    {
        foreach (var player in snapshot.Players)
        {
            if (player.Id == playerId && player.IsAlive)
                return player;
        }

        return null;
    }

    private static bool ContainsProjectile(GameStateSnapshotPacket snapshot, int projectileId)
    {
        foreach (var projectile in snapshot.Projectiles)
        {
            if (projectile.Id == projectileId)
                return true;
        }

        return false;
    }

    private static bool ContainsAsteroid(GameStateSnapshotPacket snapshot, int asteroidId)
    {
        foreach (var asteroid in snapshot.Asteroids)
        {
            if (asteroid.Id == asteroidId)
                return true;
        }

        return false;
    }

    private struct TransientVfx
    {
        public Vector2 Position;
        public Color BaseColor;
        public float Duration;
        public float Age;
        public float RadiusStart;
        public float RadiusEnd;
    }
}
