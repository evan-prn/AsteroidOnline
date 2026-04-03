using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AsteroidOnline.Client.Converters;

/// <summary>
/// Convertit une progression (double entre 0.0 et 1.0) en largeur de pixel,
/// en multipliant par la largeur maximale passée en <c>ConverterParameter</c> (string).
/// Exemple : <c>ConverterParameter='136'</c> → 0.5 × 136 = 68 px.
/// Utilisé dans <c>GameView</c> pour la barre de recharge du dash (US-11).
/// </summary>
public sealed class ProgressToWidthConverter : IValueConverter
{
    /// <summary>Instance singleton à référencer dans les ressources XAML.</summary>
    public static readonly ProgressToWidthConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double progress)
            return 0d;

        if (!double.TryParse(parameter?.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var maxWidth))
            maxWidth = 100d;

        // Clamp entre 0 et maxWidth
        return Math.Clamp(progress, 0d, 1d) * maxWidth;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
