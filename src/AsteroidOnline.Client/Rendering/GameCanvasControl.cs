namespace AsteroidOnline.Client.Rendering;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

public sealed class GameCanvasControl : Control
{
    private GameRenderer? _renderer;
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#05060D"));

    public void AttachRenderer(GameRenderer renderer)
    {
        _renderer = renderer;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(BackgroundBrush, null, new Rect(Bounds.Size));
        _renderer?.RenderFrame(context, Bounds.Size);
    }
}
