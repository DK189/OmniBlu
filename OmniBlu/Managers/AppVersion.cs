using System.Globalization;
using System.Reflection;

namespace OmniBlu.Managers;

/// <summary>A version such as 1.2.3 or 1.2.3-beta.1. Versions are compared by the Semantic Versioning rules.</summary>
public sealed record AppVersion(int Major, int Minor, int Patch, string PreRelease) : IComparable<AppVersion>
{
    /// <summary>The version of the running program, for example "0.1.0". It is "0.0.0" when it is not known.</summary>
    public static string Current
    {
        get
        {
            var text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return TryParse(text, out var version) ? version.ToString() : "0.0.0";
        }
    }

    /// <summary>
    /// Reads text such as "1.2.3", "v1.2.3" or "1.2.3-beta.1+abc123". The part after "+" (build information) is ignored.
    /// </summary>
    public static bool TryParse(string text, out AppVersion version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text[0] is 'v' or 'V')
        {
            text = text[1..];
        }

        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        string preRelease = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = text[(dash + 1)..];
            text = text[..dash];
            if (preRelease.Length == 0)
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new AppVersion(major, minor, patch, preRelease);
        return true;
    }

    /// <summary>True when <paramref name="latest"/> is a higher version than <paramref name="current"/>. False when either is not a version.</summary>
    public static bool IsNewer(string latest, string current) =>
        TryParse(latest, out var a) && TryParse(current, out var b) && a.CompareTo(b) > 0;

    /// <summary>Compares by major, minor and patch; a pre-release is lower than the same version without one.</summary>
    public int CompareTo(AppVersion other)
    {
        if (other == null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        if (result != 0)
        {
            return result;
        }

        if (PreRelease == null || other.PreRelease == null)
        {
            return PreRelease == other.PreRelease ? 0 : PreRelease == null ? 1 : -1;
        }

        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftNumber = int.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out var a);
            var rightNumber = int.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out var b);
            var part = leftNumber && rightNumber
                ? a.CompareTo(b)
                : leftNumber ? -1 : rightNumber ? 1 : string.CompareOrdinal(left[i], right[i]);
            if (part != 0)
            {
                return Math.Sign(part);
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (PreRelease == null ? string.Empty : "-" + PreRelease);
}
