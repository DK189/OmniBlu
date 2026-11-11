#if LINUX
using OmniBlu.Managers;

namespace OmniBlu.Natives;

/// <summary>Placeholder for non-Windows targets. Nothing is remembered there.</summary>
internal sealed class StubInstallState : IInstallState
{
    public string ApprovedExePath => null;

    public bool SetApprovedExePath(string path) => false;
}
#endif
