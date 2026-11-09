namespace OmniBlu.Managers;

/// <summary>
/// Chooses which device the control shortcut goes to. The rules:
/// when control is running, the next device in the list (round and round);
/// when it is not running, the device that was controlled last, or the first device if that one is not connected now.
/// </summary>
public static class ControlCycler
{
    /// <summary>
    /// Gives the key of the device to control, or null when there is nothing to do (no device is connected, or
    /// control is running and there is no other device to switch to).
    /// </summary>
    /// <param name="connectedKeys">Keys of the devices that accept input now, in priority order.</param>
    /// <param name="currentKey">Key of the device being controlled now, or null when control is not running.</param>
    /// <param name="lastKey">Key of the device controlled most recently, or null when none was.</param>
    public static string Next(IReadOnlyList<string> connectedKeys, string currentKey, string lastKey)
    {
        if (connectedKeys.Count == 0)
        {
            return null;
        }

        if (currentKey == null)
        {
            return lastKey != null && connectedKeys.Contains(lastKey) ? lastKey : connectedKeys[0];
        }

        if (connectedKeys.Count == 1 && connectedKeys[0] == currentKey)
        {
            return null;
        }

        var index = IndexOf(connectedKeys, currentKey);
        return connectedKeys[(index + 1) % connectedKeys.Count];
    }

    /// <summary>The position of the key, or -1 when it is not there (then the next device is the first one).</summary>
    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i] == key)
            {
                return i;
            }
        }

        return -1;
    }
}
