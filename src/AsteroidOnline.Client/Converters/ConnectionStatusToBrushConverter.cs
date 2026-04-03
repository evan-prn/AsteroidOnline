using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AsteroidOnline.Client.ViewModels;

namespace AsteroidOnline.Client.Converters;

/// <summary>
/// Convertit un <see cref="ConnectionStatus"/> en <see cref="IBrush"/> AvaloniaUI.
/// Utilisé dans <c>ConnectView</c> pour coloriser dynamiquement le texte de statut (US-03).
/// </summary>
public sealed class ConnectionStatusToBrushConverter : IValueConverter
{
    /// <summary>Instance singleton à référencer dans les ressources XAML.</summary>
    public static readonly ConnectionStatusToBrushConverter Instance = new();

    private static readonly SolidColorBrush BrushIdle       = new(Color.Parse("#88AACC"));
    private static readonly SolidColorBrush BrushConnecting = new(Color.Parse("#FFAA00"));
    private static readonly SolidColorBrush BrushConnected  = new(Color.Parse("#44FF88"));
    private static readonly SolidColorBrush BrushFailed     = new(Color.Parse("#FF4444"));

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ConnectionStatus status
            ? status switch
            {
                ConnectionStatus.Idle       => BrushIdle,
                ConnectionStatus.Connecting => BrushConnecting,
                ConnectionStatus.Connected  => BrushConnected,
                ConnectionStatus.Failed     => BrushFailed,
                _                           => BrushIdle,
            }
            : BrushIdle;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
