using Avalonia;
using Avalonia.Controls;

namespace OmniBlu.UI;

/// <summary>
/// A small panel in the bottom-left corner of the screen. It is used for control mode when the main window is hidden.
/// It is a real window that takes the keyboard focus, because control mode needs it: the keys and mouse buttons go to
/// the window that has the focus, and the cursor is locked inside it. <see cref="MainWindow"/> shows and hides it.
/// </summary>
public partial class ControlOverlayWindow : Window
{
    /// <summary>Space between the panel and the edges of the screen, in device-independent units.</summary>
    private const double EdgeSpace = 16;

    public ControlOverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>Moves the panel to the bottom-left corner of the work area of the main screen.</summary>
    public void PlaceAtCorner()
    {
        var screen = Screens.Primary;
        if (screen == null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var (x, y) = OverlayPlacement.BottomLeft(area.X, area.Y, area.Right, area.Bottom, screen.Scaling, Width, Height, EdgeSpace);
        Position = new PixelPoint(x, y);
    }
}
