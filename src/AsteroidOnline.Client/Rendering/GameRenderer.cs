using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AsteroidOnline.Client.Services;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Domain.World;
using AsteroidOnline.Shared.Packets;

namespace AsteroidOnline.Client.Rendering;

/// <summary>
/// Renderer client avec caméra centrée joueur + VFX légers.
/// Conçu pour maintenir la lisibilité sur une map large et un lobby jusqu'à 20 joueurs.
/// </summary>
public sealed class GameRenderer
{
    private readonly Canvas _canvas;
    private readonly IGameAudioService _audioService;
    private readonly Random _random = new();
    private readonly Dictionary<int, Vector2> _previousProjectilePositions = new();
    private readonly Dictionary<int, Vector2> _previousAsteroidPositions = new();
    private readonly Dictionary<int, int> _previousLivesByPlayer = new();
    private readonly List<TransientVfx> _vfx = new();
    private long _lastProcessedEventTimestamp = -1;

    private float _shakeTimeRemaining;
    private float _shakeIntensity;

    private const float VisibleWorldWidth = 1600f;
    private const float VisibleWorldHeight = 900f;

    private static readonly IReadOnlyDictionary<PlayerColor, ISolidColorBrush> ShipBrushes =
        new Dictionary<PlayerColor, ISolidColorBrush>
        {
            { PlayerColor.Rouge,  new SolidColorBrush(Color.Parse("#FF5B4A")) },
            { PlayerColor.Bleu,   new SolidColorBrush(Color.Parse("#4AA7FF")) },
            { PlayerColor.Vert,   new SolidColorBrush(Color.Parse("#52FFAA")) },
            { PlayerColor.Jaune,  new SolidColorBrush(Color.Parse("#FFD84A")) },
            { PlayerColor.Violet, new SolidColorBrush(Color.Parse("#C06CFF")) },
            { PlayerColor.Orange, new SolidColorBrush(Color.Parse("#FF9C4A")) },
        };

    public GameRenderer(Canvas canvas, IGameAudioService audioService)
    {
        _canvas = canvas;
        _audioService = audioService;
    }

    public void Render(
        GameStateSnapshotPacket snapshot,
        int localPlayerId,
        IReadOnlyDictionary<int, string> playerNames)
    {
        _canvas.Children.Clear();
        if (_canvas.Bounds.Width <= 0 || _canvas.Bounds.Height <= 0)
            return;

        // Les événements (VFX/audio) ne doivent être calculés qu'à la réception d'un
        // nouveau snapshot réseau, pas à chaque frame interpolée.
        if (snapshot.ServerTimestamp != _lastProcessedEventTimestamp)
        {
            UpdateTransientVfx(snapshot, localPlayerId);
            _lastProcessedEventTimestamp = snapshot.ServerTimestamp;
        }

        var bounds = WorldBounds.Default;
        var localShip = snapshot.Players.FirstOrDefault(p => p.Id == localPlayerId && p.IsAlive);
        var cameraPos = localShip is null
            ? new Vector2(bounds.Width / 2f, bounds.Height / 2f)
            : new Vector2(localShip.X, localShip.Y);

        var scale = Math.Min(
            _canvas.Bounds.Width / VisibleWorldWidth,
            _canvas.Bounds.Height / VisibleWorldHeight);

        var shake = GetCameraShakeOffset();
        var screenCenter = new Point(
            (_canvas.Bounds.Width / 2.0) + shake.X,
            (_canvas.Bounds.Height / 2.0) + shake.Y);

        foreach (var asteroid in snapshot.Asteroids)
            DrawAsteroid(asteroid, cameraPos, scale, screenCenter, bounds);

        foreach (var projectile in snapshot.Projectiles)
            DrawProjectile(projectile, cameraPos, scale, screenCenter, bounds);

        foreach (var ship in snapshot.Players)
            DrawShip(
                ship,
                cameraPos,
                scale,
                screenCenter,
                bounds,
                ship.Id == localPlayerId,
                ResolvePlayerName(playerNames, ship.Id));

        DrawRadar(snapshot, localPlayerId);
        DrawVfx(cameraPos, scale, screenCenter, bounds);
        TickVfx(1f / 60f);
    }

    private void DrawShip(PlayerSnapshot ship, Vector2 cameraPos, double scale,
        Point screenCenter, in WorldBounds bounds, bool isLocal, string playerName)
    {
        if (!ship.IsAlive)
            return;

        var center = ToScreen(new Vector2(ship.X, ship.Y), cameraPos, scale, screenCenter, bounds);
        var size = 16.0 * scale;
        if (!IsOnScreen(center, size * 3))
            return;

        var points = new[]
        {
            Rotate(0, -size, ship.Rotation),
            Rotate(-size * 0.7, size * 0.8, ship.Rotation),
            Rotate(size * 0.7, size * 0.8, ship.Rotation),
        };

        var baseBrush = ShipBrushes.TryGetValue(ship.Color, out var b) ? b : Brushes.White;
        var blinkFactor = ship.IsInvulnerable
            ? (Math.Sin(Environment.TickCount64 / 80.0) * 0.5) + 0.5
            : 1.0;
        var fillColor = (baseBrush as SolidColorBrush)?.Color ?? Colors.White;
        var alpha = (byte)(ship.IsInvulnerable ? 110 + (blinkFactor * 145) : 255);

        var polygon = new Avalonia.Controls.Shapes.Polygon
        {
            Points = new Avalonia.Collections.AvaloniaList<Point>
            {
                new(center.X + points[0].X, center.Y + points[0].Y),
                new(center.X + points[1].X, center.Y + points[1].Y),
                new(center.X + points[2].X, center.Y + points[2].Y),
            },
            Fill = new SolidColorBrush(Color.FromArgb(alpha, fillColor.R, fillColor.G, fillColor.B)),
            Stroke = isLocal ? Brushes.White : new SolidColorBrush(Color.Parse("#AAEAF3FF")),
            StrokeThickness = isLocal ? 1.8 : 1.0,
        };
        _canvas.Children.Add(polygon);

        if (ship.IsInvulnerable)
        {
            var pulse = 1.15 + (blinkFactor * 0.5);
            var haloRadius = size * pulse * 1.4;
            var halo = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = haloRadius * 2,
                Height = haloRadius * 2,
                StrokeThickness = 1.4,
                Stroke = new SolidColorBrush(Color.FromArgb(130, 255, 247, 116)),
                Fill = Brushes.Transparent,
            };
            Canvas.SetLeft(halo, center.X - haloRadius);
            Canvas.SetTop(halo, center.Y - haloRadius);
            _canvas.Children.Add(halo);
        }

        if (isLocal)
        {
            var localRing = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = size * 3.1,
                Height = size * 3.1,
                Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                StrokeThickness = 1.0,
                Fill = Brushes.Transparent,
            };
            Canvas.SetLeft(localRing, center.X - (size * 1.55));
            Canvas.SetTop(localRing, center.Y - (size * 1.55));
            _canvas.Children.Add(localRing);
        }

        var speed = MathF.Sqrt((ship.VelocityX * ship.VelocityX) + (ship.VelocityY * ship.VelocityY));
        if (speed > 60f)
            DrawEngineTrail(center, ship.Rotation, size, scale);

        DrawPlayerName(center, size, playerName, isLocal, ship.Color);
    }

    private void DrawPlayerName(Point center, double shipSize, string playerName, bool isLocal, PlayerColor color)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return;

        var accent = ShipBrushes.TryGetValue(color, out var brush)
            ? ((SolidColorBrush)brush).Color
            : Colors.White;

        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(isLocal ? (byte)190 : (byte)150, 7, 10, 18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2),
            Child = new TextBlock
            {
                Text = playerName,
                FontSize = isLocal ? 12 : 11,
                FontWeight = isLocal ? FontWeight.SemiBold : FontWeight.Medium,
                Foreground = Brushes.White,
            },
        };

        label.Measure(Size.Infinity);
        Canvas.SetLeft(label, center.X - (label.DesiredSize.Width / 2));
        Canvas.SetTop(label, center.Y - (shipSize * 2.2) - label.DesiredSize.Height);
        _canvas.Children.Add(label);
    }

    private void DrawEngineTrail(Point center, float rotation, double shipSize, double scale)
    {
        var basePoint = Rotate(0, shipSize * 0.9, rotation);
        var left = Rotate(-shipSize * 0.28, shipSize * 1.1, rotation);
        var right = Rotate(shipSize * 0.28, shipSize * 1.1, rotation);
        var tail = Rotate(0, shipSize * (1.6 + (_random.NextDouble() * 0.35)), rotation);

        var flame = new Avalonia.Controls.Shapes.Polygon
        {
            Points = new Avalonia.Collections.AvaloniaList<Point>
            {
                new(center.X + left.X, center.Y + left.Y),
                new(center.X + tail.X, center.Y + tail.Y),
                new(center.X + right.X, center.Y + right.Y),
            },
            Fill = new SolidColorBrush(Color.FromArgb(200, 255, 151, 64)),
            Stroke = new SolidColorBrush(Color.FromArgb(100, 255, 230, 140)),
            StrokeThickness = Math.Max(1.0, scale * 1.0),
        };
        _canvas.Children.Add(flame);
    }

    private void DrawAsteroid(AsteroidSnapshot asteroid, Vector2 cameraPos, double scale,
        Point screenCenter, in WorldBounds bounds)
    {
        var center = ToScreen(new Vector2(asteroid.X, asteroid.Y), cameraPos, scale, screenCenter, bounds);
        var radius = asteroid.Size switch
        {
            AsteroidSize.Large => 48.0 * scale,
            AsteroidSize.Medium => 28.0 * scale,
            _ => 14.0 * scale,
        };

        if (!IsOnScreen(center, radius * 2.2))
            return;

        var baseColor = asteroid.Size switch
        {
            AsteroidSize.Large => Color.Parse("#AA8A5A"),
            AsteroidSize.Medium => Color.Parse("#8D6F48"),
            _ => Color.Parse("#6E5338"),
        };

        var points = new Avalonia.Collections.AvaloniaList<Point>();
        const int sides = 8;
        for (var i = 0; i < sides; i++)
        {
            var angle = asteroid.Rotation + (i * Math.PI * 2 / sides);
            var jagged = radius * (0.78 + (0.24 * Math.Abs(Math.Sin(i * asteroid.Id * 1.17))));
            points.Add(new Point(
                center.X + (Math.Cos(angle) * jagged),
                center.Y + (Math.Sin(angle) * jagged)));
        }

        var poly = new Avalonia.Controls.Shapes.Polygon
        {
            Points = points,
            Fill = new SolidColorBrush(baseColor),
            Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 222, 180)),
            StrokeThickness = 1.2,
        };
        _canvas.Children.Add(poly);
    }

    private void DrawProjectile(ProjectileSnapshot projectile, Vector2 cameraPos, double scale,
        Point screenCenter, in WorldBounds bounds)
    {
        var pos = new Vector2(projectile.X, projectile.Y);
        var center = ToScreen(pos, cameraPos, scale, screenCenter, bounds);
        var radius = 3.6 * scale;
        if (!IsOnScreen(center, radius * 2.4))
            return;

        if (_previousProjectilePositions.TryGetValue(projectile.Id, out var prev))
        {
            var trailStart = ToScreen(prev, cameraPos, scale, screenCenter, bounds);
            var line = new Avalonia.Controls.Shapes.Line
            {
                StartPoint = trailStart,
                EndPoint = center,
                Stroke = new SolidColorBrush(Color.FromArgb(150, 255, 243, 133)),
                StrokeThickness = Math.Max(1.0, scale * 1.4),
            };
            _canvas.Children.Add(line);
        }

        var bullet = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = new SolidColorBrush(Color.Parse("#FFF385")),
            Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            StrokeThickness = 0.8,
        };
        Canvas.SetLeft(bullet, center.X - radius);
        Canvas.SetTop(bullet, center.Y - radius);
        _canvas.Children.Add(bullet);
    }

    private void DrawRadar(GameStateSnapshotPacket snapshot, int localPlayerId)
    {
        const double radarSize = 130;
        const double padding = 16;
        var left = _canvas.Bounds.Width - radarSize - padding;
        var top = _canvas.Bounds.Height - radarSize - padding;

        var frame = new Border
        {
            Width = radarSize,
            Height = radarSize,
            CornerRadius = new CornerRadius(999),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 199, 109)),
            BorderThickness = new Thickness(1.2),
            Background = new SolidColorBrush(Color.FromArgb(95, 8, 13, 25)),
        };
        Canvas.SetLeft(frame, left);
        Canvas.SetTop(frame, top);
        _canvas.Children.Add(frame);

        foreach (var asteroid in snapshot.Asteroids)
        {
            var p = ToRadarPoint(asteroid.X, asteroid.Y, left, top, radarSize);
            AddRadarDot(p, 3.2, Color.Parse("#FFAE6B"));
        }

        foreach (var player in snapshot.Players.Where(p => p.IsAlive))
        {
            var p = ToRadarPoint(player.X, player.Y, left, top, radarSize);
            var color = player.Id == localPlayerId
                ? Color.Parse("#7BFF7E")
                : Color.Parse("#E9F2FF");
            AddRadarDot(p, player.Id == localPlayerId ? 3.8 : 2.8, color);
        }
    }

    private Point ToRadarPoint(float worldX, float worldY, double left, double top, double size)
    {
        var nx = worldX / WorldBounds.Default.Width;
        var ny = worldY / WorldBounds.Default.Height;
        return new Point(left + (nx * size), top + (ny * size));
    }

    private void AddRadarDot(Point p, double size, Color color)
    {
        var dot = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
        };
        Canvas.SetLeft(dot, p.X - (size / 2));
        Canvas.SetTop(dot, p.Y - (size / 2));
        _canvas.Children.Add(dot);
    }

    private void DrawVfx(Vector2 cameraPos, double scale, Point screenCenter, in WorldBounds bounds)
    {
        foreach (var vfx in _vfx)
        {
            var center = ToScreen(vfx.Position, cameraPos, scale, screenCenter, bounds);
            var t = Math.Clamp(vfx.Age / vfx.Duration, 0f, 1f);
            var radius = vfx.RadiusStart + ((vfx.RadiusEnd - vfx.RadiusStart) * t);
            var alpha = (byte)(vfx.BaseColor.A * (1f - t));

            if (!IsOnScreen(center, radius * scale * 3))
                continue;

            var circle = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = radius * 2 * scale,
                Height = radius * 2 * scale,
                Fill = new SolidColorBrush(Color.FromArgb(alpha, vfx.BaseColor.R, vfx.BaseColor.G, vfx.BaseColor.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, alpha + 35), 255, 255, 255)),
                StrokeThickness = 1.0,
            };
            Canvas.SetLeft(circle, center.X - (circle.Width / 2));
            Canvas.SetTop(circle, center.Y - (circle.Height / 2));
            _canvas.Children.Add(circle);
        }
    }

    private void UpdateTransientVfx(GameStateSnapshotPacket snapshot, int localPlayerId)
    {
        var previousProjectileIds = _previousProjectilePositions.Keys.ToHashSet();
        var currentProjectilePositions = snapshot.Projectiles.ToDictionary(
            p => p.Id,
            p => new Vector2(p.X, p.Y));

        foreach (var previous in _previousProjectilePositions)
        {
            if (!currentProjectilePositions.ContainsKey(previous.Key))
            {
                AddVfx(previous.Value, Color.FromArgb(200, 255, 215, 106), 0.20f, 6f, 28f);
            }
        }
        _previousProjectilePositions.Clear();
        foreach (var current in currentProjectilePositions)
            _previousProjectilePositions[current.Key] = current.Value;

        var currentAsteroidPositions = snapshot.Asteroids.ToDictionary(
            a => a.Id,
            a => new Vector2(a.X, a.Y));

        foreach (var previous in _previousAsteroidPositions)
        {
            if (!currentAsteroidPositions.ContainsKey(previous.Key))
            {
                AddVfx(previous.Value, Color.FromArgb(220, 255, 125, 82), 0.35f, 16f, 80f);
                _audioService.PlayAsteroidExplosion();
            }
        }
        _previousAsteroidPositions.Clear();
        foreach (var current in currentAsteroidPositions)
            _previousAsteroidPositions[current.Key] = current.Value;

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

    private Point ToScreen(Vector2 worldPos, Vector2 cameraPos, double scale, Point screenCenter,
        in WorldBounds bounds)
    {
        var delta = WrappedDelta(worldPos, cameraPos, bounds);
        return new Point(
            screenCenter.X + (delta.X * scale),
            screenCenter.Y + (delta.Y * scale));
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

    private bool IsOnScreen(Point p, double margin)
    {
        return p.X >= -margin
               && p.Y >= -margin
               && p.X <= _canvas.Bounds.Width + margin
               && p.Y <= _canvas.Bounds.Height + margin;
    }

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
