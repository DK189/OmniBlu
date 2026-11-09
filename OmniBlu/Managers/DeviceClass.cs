namespace OmniBlu.Managers;

/// <summary>
/// Rules about the Bluetooth "class of device" that Windows reports. This app turns the PC into a speaker and a
/// keyboard and mouse, so the devices that matter are the ones that act as a host: phones and computers.
/// </summary>
public static class DeviceClass
{
    /// <summary>Major class: not classified.</summary>
    public const int Miscellaneous = 0;

    /// <summary>Major class: computer (desktop, laptop, tablet).</summary>
    public const int Computer = 1;

    /// <summary>Major class: phone.</summary>
    public const int Phone = 2;

    /// <summary>Major class: "uncategorized". Used by devices that do not say what they are.</summary>
    public const int Uncategorized = 31;

    /// <summary>
    /// True when a device can be a host for this PC. An unknown class (null) counts as a host, because hiding a device
    /// that might be the phone is worse than showing one device too many.
    /// </summary>
    public static bool IsHostLike(int? majorClass) =>
        majorClass is null or Miscellaneous or Computer or Phone or Uncategorized;
}
