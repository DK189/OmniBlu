using System.Diagnostics;

namespace OmniBlu.Managers;

/// <summary>
/// Starts a new copy of the program and lets the old copy end first. The new copy gets <c>--wait-pid N</c> and waits for
/// process N to exit before it takes the single-instance lock, the shortcut and the Bluetooth radio.
/// </summary>
public static class ProcessRestart
{
    /// <summary>The argument that tells a new copy which process to wait for.</summary>
    public const string WaitArgument = "--wait-pid";

    /// <summary>Gives the process id after <c>--wait-pid</c> (or <c>--wait-pid=N</c>), or 0 when there is none.</summary>
    public static int ParseWaitPid(IEnumerable<string> args)
    {
        if (args == null)
        {
            return 0;
        }

        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            string value = null;
            if (string.Equals(list[i], WaitArgument, StringComparison.OrdinalIgnoreCase) && i + 1 < list.Count)
            {
                value = list[i + 1];
            }
            else if (list[i].StartsWith(WaitArgument + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = list[i][(WaitArgument.Length + 1)..];
            }

            if (value != null)
            {
                return int.TryParse(value, out var pid) && pid > 0 ? pid : 0;
            }
        }

        return 0;
    }

    /// <summary>Starts <paramref name="exePath"/> and tells it to wait for the current process to end.</summary>
    public static void StartAndWait(string exePath) => Start(exePath, Environment.ProcessId);

    /// <summary>Starts <paramref name="exePath"/> and tells it to wait for process <paramref name="waitPid"/>.</summary>
    public static void Start(string exePath, int waitPid)
    {
        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
        };
        info.ArgumentList.Add(WaitArgument);
        info.ArgumentList.Add(waitPid.ToString());
        Process.Start(info)?.Dispose();
    }

    /// <summary>Waits until the process has exited, at most <paramref name="timeoutMs"/> milliseconds. Returns at once when it is gone.</summary>
    public static void WaitForExit(int pid, int timeoutMs)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(timeoutMs);
        }
        catch (ArgumentException)
        {
            // No such process: it has already ended.
        }
    }
}
