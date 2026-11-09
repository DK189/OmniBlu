namespace OmniBlu.Managers;

/// <summary>
/// Keeps the mouse cursor inside the window and moves it back to the middle, so mouse movement can be read as endless
/// relative movement (like a game). Positions are screen pixels.
/// </summary>
public interface ICursorLock
{
    /// <summary>False on platforms where this is not implemented. Control mode then works with the keyboard only.</summary>
    bool IsSupported { get; }

    /// <summary>Keeps the cursor inside the given screen rectangle.</summary>
    void Clip(int left, int top, int right, int bottom);

    /// <summary>Lets the cursor move anywhere again. Safe to call at any time, even when not clipped.</summary>
    void Unclip();

    void SetPosition(int x, int y);

    /// <summary>Reads where the cursor is right now. Returns false when it cannot be read.</summary>
    bool TryGetPosition(out int x, out int y);
}
