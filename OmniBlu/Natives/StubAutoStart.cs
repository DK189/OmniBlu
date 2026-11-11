#if LINUX
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Placeholder for non-Windows targets. Starting with the system is not available there.</summary>
internal sealed class StubAutoStart : IAutoStart
{
    public bool IsSupported => false;

    public string LastError => "Starting with the system is not available on this platform.";

    public StartupMode Read() => StartupMode.Off;

    public bool Apply(StartupMode mode, string exePath = null) => false;
}
#endif
