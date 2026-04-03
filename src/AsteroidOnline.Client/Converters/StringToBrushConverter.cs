using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AsteroidOnline.Client.Converters;

/// <summary>
/// Convertit une chaîne hexadécimale de couleur (ex : "#FF4444") en <see cref="SolidColorBrush"/>.
/// Utilisé dans les DataTemplates des vues pour afficher les couleurs des vaisseaux
/// sans exposer de types Avalonia dans les couches Shared ou Domain.
/// </summary>
public sealed class StringToBrushConverter : IValueConverter
{
    /// <summary>Instance singleton à référencer dans les ressources XAML.</summary>
    public static readonly StringToBrushConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch
            {
                // Couleur invalide → blanc par défaut
            }
        }
        return new SolidColorBrush(Colors.White);
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
