using OmniBlu.Models;

namespace OmniBlu.Managers;

/// <summary>
/// Joins the two Windows lists (paired devices and A2DP endpoints) into one list of devices.
/// The two lists use different IDs for the same device, but they share the container ID.
/// </summary>
public static class DeviceMerger
{
    public static List<SmartDeviceInfo> Merge(IEnumerable<PairedRecord> paired, IEnumerable<EndpointRecord> endpoints)
    {
        var endpointList = endpoints.ToList();
        var usedEndpoints = new HashSet<string>();
        var result = new List<SmartDeviceInfo>();
        var keys = new HashSet<string>();

        foreach (var device in paired)
        {
            var endpoint = FindEndpoint(endpointList, device.ContainerId);
            if (endpoint != null)
            {
                usedEndpoints.Add(endpoint.Id);
            }

            var key = string.IsNullOrEmpty(device.ContainerId) ? device.Id : device.ContainerId;
            if (keys.Add(key))
            {
                result.Add(new SmartDeviceInfo(
                    key,
                    NameOrDefault(device.Name, endpoint?.Name),
                    endpoint?.Id,
                    BluetoothAddress.Normalize(device.Address),
                    device.MajorClass));
            }
        }

        // An endpoint with no matching paired device still gets its own entry. Dropping it would hide a usable device.
        foreach (var endpoint in endpointList)
        {
            if (usedEndpoints.Contains(endpoint.Id))
            {
                continue;
            }

            var key = string.IsNullOrEmpty(endpoint.ContainerId) ? endpoint.Id : endpoint.ContainerId;
            if (keys.Add(key))
            {
                result.Add(new SmartDeviceInfo(key, NameOrDefault(endpoint.Name, null), endpoint.Id));
            }
        }

        return result;
    }

    private static EndpointRecord FindEndpoint(List<EndpointRecord> endpoints, string containerId)
    {
        if (string.IsNullOrEmpty(containerId))
        {
            return null;
        }

        return endpoints.FirstOrDefault(e => string.Equals(e.ContainerId, containerId, StringComparison.OrdinalIgnoreCase));
    }

    private static string NameOrDefault(string name, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "Unknown device" : fallback;
    }
}
