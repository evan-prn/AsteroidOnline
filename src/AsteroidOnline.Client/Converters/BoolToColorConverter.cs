using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AsteroidOnline.Client.Converters;

/// <summary>
/// Convertit un booléen en <see cref="SolidColorBrush"/> selon deux couleurs hex passées
/// en <c>ConverterParameter</c> sous la forme <c>"ColorTrue|ColorFalse"</c>.
/// Exemple : <c>ConverterParameter='#00CFFF|#224455'</c>
/// Utilisé dans <c>GameView</c> pour coloriser la barre de recharge du dash (US-11).
/// </summary>
public sealed class BoolToColorConverter : IValueConverter
{
    /// <summary>Instance singleton à référencer dans les ressources XAML.</summary>
    public static readonly BoolToColorConverter Instance = new();

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        var parts = parameter?.ToString()?.Split('|');

        if (parts is { Length: 2 })
        {
            try
            {
                var hex = flag ? parts[0] : parts[1];
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch { /* couleur invalide → fallback */ }
        }

        return new SolidColorBrush(flag ? Colors.White : Colors.Gray);
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
