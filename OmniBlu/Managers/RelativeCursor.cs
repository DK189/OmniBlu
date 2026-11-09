namespace OmniBlu.Managers;

/// <summary>
/// Turns cursor movement into relative movement: it reads how far the cursor is from a fixed centre point, then puts
/// the cursor back on that point.
/// <para>
/// The distance is read from the cursor itself (whole screen pixels) and not from the position written in a mouse
/// event. Events wait in a queue, so their positions can be old: counting them would add the same movement twice, and
/// the pointer on the phone would run ahead. Reading the cursor now means an old event just finds it already in the
/// centre and adds nothing.
/// </para>
/// </summary>
public sealed class RelativeCursor
{
    private readonly ICursorLock cursor;
    private readonly int centerX;
    private readonly int centerY;

    public RelativeCursor(ICursorLock cursor, int centerX, int centerY)
    {
        this.cursor = cursor;
        this.centerX = centerX;
        this.centerY = centerY;
    }

    /// <summary>Puts the cursor on the centre point.</summary>
    public void Center() => cursor.SetPosition(centerX, centerY);

    /// <summary>
    /// Reads the movement since the cursor was last centred, then centres it again. Returns false when there is no
    /// movement (or the cursor cannot be read).
    /// </summary>
    public bool TryRead(out int dx, out int dy)
    {
        dx = 0;
        dy = 0;

        if (!cursor.TryGetPosition(out var x, out var y))
        {
            return false;
        }

        dx = x - centerX;
        dy = y - centerY;
        if (dx == 0 && dy == 0)
        {
            return false;
        }

        Center();
        return true;
    }
}
