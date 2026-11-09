using OmniBlu.Models;

namespace OmniBlu.Managers;

/// <summary>Result of matching a connected HID client (a phone) to the device list.</summary>
/// <param name="Key">Key of the matching device, or a new key for a phone that is not in the list.</param>
/// <param name="IsKnown">True when the phone is a device in the list.</param>
/// <param name="ByName">True when the match was made by name, because the address did not match.</param>
public sealed record HidMatch(string Key, bool IsKnown, bool ByName);

/// <summary>Finds which known device a connected HID client (a phone) is.</summary>
public static class HidClientMatcher
{
    /// <summary>Prefix of the key given to a phone that is not in the device list.</summary>
    public const string UnknownKeyPrefix = "ble:";

    /// <summary>
    /// Matches by Bluetooth address first: it is the same over classic Bluetooth and LE once the phone is paired.
    /// The container ID cannot be used, because the classic and LE records of one phone can differ.
    /// <para>
    /// A phone that is not paired over LE yet connects with a random address that matches nothing. Then the name is
    /// used, but only when exactly one known device has that name (two phones called "iPhone" must not be mixed up).
    /// </para>
    /// A client with no match gets its own key, so it is never dropped.
    /// </summary>
    public static HidMatch Match(IEnumerable<SmartDeviceInfo> devices, string address, string name)
    {
        var list = devices.ToList();
        var normalized = BluetoothAddress.Normalize(address);

        var byAddress = normalized == null ? null : list.FirstOrDefault(d => d.Address == normalized);
        if (byAddress != null)
        {
            return new HidMatch(byAddress.Key, true, false);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var sameName = list
                .Where(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (sameName.Count == 1)
            {
                return new HidMatch(sameName[0].Key, true, true);
            }
        }

        return new HidMatch(UnknownKeyPrefix + (normalized ?? address), false, false);
    }

    /// <summary>The address stored in the key of a phone that is not in the list, or null for any other key.</summary>
    public static string AddressOfUnknownKey(string key) =>
        key != null && key.StartsWith(UnknownKeyPrefix, StringComparison.Ordinal)
            ? BluetoothAddress.Normalize(key[UnknownKeyPrefix.Length..])
            : null;
}
