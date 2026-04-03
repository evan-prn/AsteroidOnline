using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AsteroidOnline.Domain.Entities;
using AsteroidOnline.Shared.Packets;

namespace AsteroidOnline.Client.Rendering;

/// <summary>
/// Moteur de rendu 2D du canvas de jeu.
/// Dessine les vaisseaux, astéroïdes et projectiles sur un <see cref="Canvas"/> Avalonia
/// à partir du dernier <see cref="GameStateSnapshotPacket"/> reçu.
/// Le rendu est déclenché à chaque tick du DispatcherTimer de <see cref="ViewModels.GameViewModel"/>.
/// </summary>
public sealed class GameRenderer
{
    private readonly Canvas _canvas;

    // Pinceaux pré-alloués pour éviter les allocations à chaque frame
    private static readonly IReadOnlyDictionary<PlayerColor, ISolidColorBrush> ShipBrushes =
        new Dictionary<PlayerColor, ISolidColorBrush>
        {
            { PlayerColor.Rouge,  new SolidColorBrush(Color.Parse("#FF4444")) },
            { PlayerColor.Bleu,   new SolidColorBrush(Color.Parse("#4488FF")) },
            { PlayerColor.Vert,   new SolidColorBrush(Color.Parse("#44FF88")) },
            { PlayerColor.Jaune,  new SolidColorBrush(Color.Parse("#FFDD44")) },
            { PlayerColor.Violet, new SolidColorBrush(Color.Parse("#AA44FF")) },
            { PlayerColor.Orange, new SolidColorBrush(Color.Parse("#FF8844")) },
        };

    private static readonly ISolidColorBrush AsteroidLargeBrush  = new SolidColorBrush(Color.Parse("#9966AA"));
    private static readonly ISolidColorBrush AsteroidMediumBrush = new SolidColorBrush(Color.Parse("#7755AA"));
    private static readonly ISolidColorBrush AsteroidSmallBrush  = new SolidColorBrush(Color.Parse("#553388"));
    private static readonly ISolidColorBrush ProjectileBrush     = new SolidColorBrush(Color.Parse("#FFFF88"));
    private static readonly ISolidColorBrush ShieldBrush         = new SolidColorBrush(Color.FromArgb(80, 0, 200, 255));
    private static readonly IPen             AsteroidPen          = new Pen(Brushes.White, 1.5);
    private static readonly IPen             ShipPen              = new Pen(Brushes.White, 1.0);

    // Dimensions du monde de jeu (pour la mise à l'échelle)
    private const float WorldWidth  = 1920f;
    private const float WorldHeight = 1080f;

    /// <summary>
    /// Initialise le renderer sur le canvas fourni.
    /// </summary>
    /// <param name="canvas">Canvas cible dans <c>GameView.axaml</c>.</param>
    public GameRenderer(Canvas canvas)
    {
        _canvas = canvas;
    }

    /// <summary>
    /// Redessine l'intégralité du canvas à partir d'un snapshot.
    /// Doit être appelé depuis le UI thread.
    /// </summary>
    /// <param name="snapshot">Dernier snapshot reçu du serveur.</param>
    /// <param name="localPlayerId">ID du joueur local (pour le centrage et la colorisation).</param>
    public void Render(GameStateSnapshotPacket snapshot, int localPlayerId)
    {
        _canvas.Children.Clear();

        var scaleX = _canvas.Bounds.Width  / WorldWidth;
        var scaleY = _canvas.Bounds.Height / WorldHeight;
        var scale  = Math.Min(scaleX, scaleY);

        // Offset pour centrer le monde si le canvas n'est pas exactement 16:9
        var offsetX = (_canvas.Bounds.Width  - WorldWidth  * scale) / 2;
        var offsetY = (_canvas.Bounds.Height - WorldHeight * scale) / 2;

        // ── Astéroïdes ─────────────────────────────────────────────────────────
        foreach (var a in snapshot.Asteroids)
            DrawAsteroid(a, scale, offsetX, offsetY);

        // ── Projectiles ────────────────────────────────────────────────────────
        foreach (var p in snapshot.Projectiles)
            DrawProjectile(p, scale, offsetX, offsetY);

        // ── Vaisseaux ──────────────────────────────────────────────────────────
        foreach (var p in snapshot.Players)
            DrawShip(p, scale, offsetX, offsetY, p.Id == localPlayerId);
    }

    // ──── Dessin des entités ───────────────────────────────────────────────────

    private void DrawShip(PlayerSnapshot ship, double scale,
        double ox, double oy, bool isLocal)
    {
        if (!ship.IsAlive) return;

        var cx = ox + ship.X * scale;
        var cy = oy + ship.Y * scale;
        var r  = ship.Rotation;

        // Taille du vaisseau à l'écran
        var size = 16.0 * scale;

        // Les 3 sommets du triangle vaisseau (repère local, pointe vers le haut)
        var pts = new[]
        {
            Rotate(0,       -size,      r),  // pointe
            Rotate(-size * 0.7, size * 0.8, r),  // bas gauche
            Rotate( size * 0.7, size * 0.8, r),  // bas droite
        };

        var brush = ShipBrushes.TryGetValue(ship.Color, out var b) ? b : Brushes.White;

        var poly = new Avalonia.Controls.Shapes.Polygon
        {
            Points = new Avalonia.Collections.AvaloniaList<Point>
            {
                new(cx + pts[0].X, cy + pts[0].Y),
                new(cx + pts[1].X, cy + pts[1].Y),
                new(cx + pts[2].X, cy + pts[2].Y),
            },
            Fill   = brush,
            Stroke = isLocal ? Brushes.White : Brushes.Transparent,
            StrokeThickness = isLocal ? 1.5 : 0,
        };
        _canvas.Children.Add(poly);

        // Indicateur "local" : petit halo blanc
        if (isLocal)
        {
            var halo = new Avalonia.Controls.Shapes.Ellipse
            {
                Width  = size * 2.8,
                Height = size * 2.8,
                Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                StrokeThickness = 1,
                Fill   = Brushes.Transparent,
            };
            Canvas.SetLeft(halo, cx - size * 1.4);
            Canvas.SetTop(halo,  cy - size * 1.4);
            _canvas.Children.Add(halo);
        }

        // Barre de cooldown dash sous le vaisseau (si recharge en cours)
        if (ship.DashCooldownProgress < 1f)
            DrawDashBar(cx, cy, size, ship.DashCooldownProgress, scale);
    }

    private void DrawDashBar(double cx, double cy, double shipSize,
        float progress, double scale)
    {
        var barW = shipSize * 2.5;
        var barH = 3.0 * scale;
        var bx   = cx - barW / 2;
        var by   = cy + shipSize * 1.3;

        // Fond
        var bg = new Avalonia.Controls.Shapes.Rectangle
        {
            Width = barW, Height = barH,
            Fill  = new SolidColorBrush(Color.Parse("#1E3A5F")),
            RadiusX = 2, RadiusY = 2,
        };
        Canvas.SetLeft(bg, bx); Canvas.SetTop(bg, by);
        _canvas.Children.Add(bg);

        // Progression
        var fg = new Avalonia.Controls.Shapes.Rectangle
        {
            Width = barW * progress, Height = barH,
            Fill  = new SolidColorBrush(Color.Parse("#00CFFF")),
            RadiusX = 2, RadiusY = 2,
        };
        Canvas.SetLeft(fg, bx); Canvas.SetTop(fg, by);
        _canvas.Children.Add(fg);
    }

    private void DrawAsteroid(AsteroidSnapshot asteroid, double scale,
        double ox, double oy)
    {
        var cx = ox + asteroid.X * scale;
        var cy = oy + asteroid.Y * scale;

        var radius = asteroid.Size switch
        {
            AsteroidSize.Large  => 48.0 * scale,
            AsteroidSize.Medium => 28.0 * scale,
            AsteroidSize.Small  => 14.0 * scale,
            _                   => 14.0 * scale,
        };

        var brush = asteroid.Size switch
        {
            AsteroidSize.Large  => AsteroidLargeBrush,
            AsteroidSize.Medium => AsteroidMediumBrush,
            _                   => AsteroidSmallBrush,
        };

        // Forme polygonale irrégulière simulée par rotation d'un octogone
        var sides = 8;
        var pts   = new Avalonia.Collections.AvaloniaList<Point>();
        for (var i = 0; i < sides; i++)
        {
            var angle = asteroid.Rotation + i * Math.PI * 2 / sides;
            // Légère irrégularité basée sur l'ID (stable entre frames)
            var r2 = radius * (0.8 + 0.2 * Math.Abs(Math.Sin(i * asteroid.Id * 1.3)));
            pts.Add(new Point(cx + Math.Cos(angle) * r2, cy + Math.Sin(angle) * r2));
        }

        var poly = new Avalonia.Controls.Shapes.Polygon
        {
            Points = pts,
            Fill   = brush,
            Stroke = Brushes.LightGray,
            StrokeThickness = 1.2,
        };
        _canvas.Children.Add(poly);
    }

    private void DrawProjectile(ProjectileSnapshot proj, double scale,
        double ox, double oy)
    {
        var cx = ox + proj.X * scale;
        var cy = oy + proj.Y * scale;
        var r  = 3.0 * scale;

        var ellipse = new Avalonia.Controls.Shapes.Ellipse
        {
            Width  = r * 2,
            Height = r * 2,
            Fill   = ProjectileBrush,
        };
        Canvas.SetLeft(ellipse, cx - r);
        Canvas.SetTop(ellipse,  cy - r);
        _canvas.Children.Add(ellipse);
    }

    // ──── Utilitaire de rotation 2D ────────────────────────────────────────────

    private static (double X, double Y) Rotate(double x, double y, float angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return (x * cos - y * sin, x * sin + y * cos);
    }
}
