namespace OmniBlu.Models;

/// <summary>
/// A smart device (phone, tablet...) as seen by the operating system. The same physical device can appear in several
/// Windows lists with different IDs, so <see cref="Key"/> is the stable identity used everywhere in the app.
/// </summary>
/// <param name="Key">Stable identity: the Windows container ID, or the device ID when no container ID exists.</param>
/// <param name="Name">Name reported by Windows.</param>
/// <param name="A2dpEndpointId">ID of the A2DP sink endpoint, or null when the device cannot send audio to this PC.</param>
/// <param name="Address">
/// Bluetooth address as 12 lowercase hex digits (see <c>BluetoothAddress</c>), or null when unknown. A phone keeps the
/// same address over classic Bluetooth and Bluetooth LE, so this is how HID clients are matched to devices.
/// </param>
/// <param name="MajorClass">Bluetooth major device class (1 = computer, 2 = phone, 5 = peripheral...), or null when unknown.</param>
public sealed record SmartDeviceInfo(string Key, string Name, string A2dpEndpointId, string Address = null, int? MajorClass = null)
{
    public bool SupportsA2dp => !string.IsNullOrEmpty(A2dpEndpointId);

    /// <summary>
    /// True when this device can be a host for this PC. A device that can send audio (A2DP) is always a host, whatever
    /// its class says.
    /// </summary>
    public bool IsHostLike => SupportsA2dp || Managers.DeviceClass.IsHostLike(MajorClass);
}

/// <summary>A device from the "paired Bluetooth devices" list of Windows.</summary>
public sealed record PairedRecord(string Id, string Name, string ContainerId, string Address = null, int? MajorClass = null);

/// <summary>A device from the "A2DP sink endpoints" list of Windows.</summary>
public sealed record EndpointRecord(string Id, string Name, string ContainerId);
