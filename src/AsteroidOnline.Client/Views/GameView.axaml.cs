using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AsteroidOnline.Client.ViewModels;

namespace AsteroidOnline.Client.Views;

/// <summary>
/// Code-behind de l'écran de jeu.
/// Branche le <see cref="Rendering.GameRenderer"/> et l'<see cref="Input.InputHandler"/>
/// sur le ViewModel dès que la vue est intégrée dans l'arbre visuel.
/// </summary>
public partial class GameView : UserControl
{
    public GameView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is not GameViewModel vm) return;

        var topLevel  = TopLevel.GetTopLevel(this);
        var canvas    = this.FindControl<Canvas>("GameCanvas");

        if (topLevel is not null && canvas is not null)
            vm.Attach(topLevel, canvas);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (DataContext is GameViewModel vm)
            vm.Dispose();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);

        if (DataContext is GameViewModel vm)
            vm.ClearInputs();
    }
}
