namespace OmniBlu.UI;

/// <summary>Works out where the small control panel goes on the screen.</summary>
public static class OverlayPlacement
{
    /// <summary>
    /// Gives the top-left corner (in screen pixels) of a window that sits in the bottom-left corner of the work area.
    /// The bottom-right corner is not used, because Windows shows its notifications there and they take the keyboard focus away.
    /// The work area is in pixels, but the size and margin of the window are in device-independent units, so they are
    /// multiplied by <paramref name="scaling"/> first. A window that is too large for the area is kept inside it, at
    /// the top-left of the area.
    /// </summary>
    /// <param name="areaLeft">Left edge of the work area, in pixels.</param>
    /// <param name="areaTop">Top edge of the work area, in pixels.</param>
    /// <param name="areaRight">Right edge of the work area, in pixels.</param>
    /// <param name="areaBottom">Bottom edge of the work area, in pixels.</param>
    /// <param name="scaling">Screen scaling (1.0 = 100%, 1.5 = 150%).</param>
    /// <param name="width">Window width in device-independent units.</param>
    /// <param name="height">Window height in device-independent units.</param>
    /// <param name="margin">Space to the screen edges in device-independent units.</param>
    public static (int X, int Y) BottomLeft(
        int areaLeft, int areaTop, int areaRight, int areaBottom, double scaling, double width, double height, double margin)
    {
        var factor = scaling > 0 ? scaling : 1.0;
        var x = areaLeft + (int)Math.Round(margin * factor);
        var y = areaBottom - (int)Math.Round((height + margin) * factor);
        if ((int)Math.Round((width + margin) * factor) > areaRight - areaLeft)
        {
            x = areaLeft;
        }

        return (x, Math.Max(areaTop, y));
    }
}
