using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AsteroidOnline.Client.ViewModels;

namespace AsteroidOnline.Client.Views;

/// <summary>
/// Code-behind de l'écran de lobby.
/// Toute la logique réside dans <see cref="ViewModels.LobbyViewModel"/>.
/// </summary>
public partial class LobbyView : UserControl
{
    public LobbyView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (DataContext is LobbyViewModel vm)
            vm.Dispose();
    }
}
