using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AsteroidOnline.Domain.Entities;

namespace AsteroidOnline.Client.Converters;

/// <summary>
/// Convertit un <see cref="PlayerColor"/> en <see cref="SolidColorBrush"/> AvaloniaUI.
/// Utilisé dans <c>ConnectView</c> et <c>LobbyView</c> pour coloriser les indicateurs
/// de vaisseau (US-05).
/// </summary>
public sealed class PlayerColorToBrushConverter : IValueConverter
{
    /// <summary>Instance singleton à référencer dans les ressources XAML.</summary>
    public static readonly PlayerColorToBrushConverter Instance = new();

    // Table de correspondance couleur → pinceau, initialisée une seule fois.
    private static readonly IReadOnlyDictionary<PlayerColor, SolidColorBrush> Brushes =
        new Dictionary<PlayerColor, SolidColorBrush>
        {
            { PlayerColor.Rouge,  new SolidColorBrush(Color.Parse("#FF4444")) },
            { PlayerColor.Bleu,   new SolidColorBrush(Color.Parse("#4488FF")) },
            { PlayerColor.Vert,   new SolidColorBrush(Color.Parse("#44FF88")) },
            { PlayerColor.Jaune,  new SolidColorBrush(Color.Parse("#FFDD44")) },
            { PlayerColor.Violet, new SolidColorBrush(Color.Parse("#AA44FF")) },
            { PlayerColor.Orange, new SolidColorBrush(Color.Parse("#FF8844")) },
        };

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlayerColor color && Brushes.TryGetValue(color, out var brush))
            return brush;

        return new SolidColorBrush(Avalonia.Media.Colors.White);
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
