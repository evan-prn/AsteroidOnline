using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AsteroidOnline.Client.Converters;

/// <summary>
/// Convertit le nombre de secondes restantes en couleur pour chaque segment
/// de la barre de progression du compte à rebours (US-06).
/// Le paramètre de conversion est l'index du segment (0 = dernier, 4 = premier).
/// Un segment est "allumé" (bleu cyan) si son index est inférieur aux secondes restantes,
/// sinon il est "éteint" (gris sombre).
/// </summary>
public sealed class CountdownSegmentConverter : IValueConverter
{
    /// <summary>Instance singleton à référencer dans les ressources XAML.</summary>
    public static readonly CountdownSegmentConverter Instance = new();

    private static readonly SolidColorBrush BrushActive   = new(Color.Parse("#00CFFF"));
    private static readonly SolidColorBrush BrushInactive = new(Color.Parse("#1E3A5F"));

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int secondsRemaining)
            return BrushInactive;

        if (!int.TryParse(parameter?.ToString(), out var segmentIndex))
            return BrushInactive;

        // Le segment est actif si son index est strictement inférieur aux secondes restantes.
        return segmentIndex < secondsRemaining ? BrushActive : BrushInactive;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
