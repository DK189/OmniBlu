namespace OmniBlu.Managers;

/// <summary>Bluetooth addresses in one format: 12 lowercase hex digits, no separators.</summary>
public static class BluetoothAddress
{
    /// <summary>Accepts "b0:8c:75:1d:06:52", "B0-8C-75-1D-06-52" or "B08C751D0652". Returns null when it is not an address.</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = text.Replace(":", "").Replace("-", "").Trim().ToLowerInvariant();
        return digits.Length == 12 && digits.All(Uri.IsHexDigit) ? digits : null;
    }

    public static string FromUInt64(ulong address) => address.ToString("x12");
}
