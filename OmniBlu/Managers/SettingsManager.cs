using System.Text.Json;
using OmniBlu.Models;

namespace OmniBlu.Managers;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in the local application data folder.</summary>
public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string filePath;
    private readonly object saveLock = new();

    public SettingsManager()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniBlu", "settings.json"))
    {
    }

    public SettingsManager(string filePath)
    {
        this.filePath = filePath;
        Settings = Load();
    }

    public AppSettings Settings { get; }

    /// <summary>Returns the saved customization of a device, or null when there is none.</summary>
    public DeviceSettings GetDevice(string key)
    {
        lock (saveLock)
        {
            return Settings.Devices.TryGetValue(key, out var device) ? device : null;
        }
    }

    /// <summary>Saves a custom name and icon. A null value means "use the default".</summary>
    public void SetDevice(string key, string name, string icon)
    {
        lock (saveLock)
        {
            var device = GetOrCreate(key);
            device.Name = name;
            device.Icon = icon;
            Cleanup(key, device);
            Save();
        }
    }

    /// <summary>Speed of the mouse sent to a phone in control mode (1.0 = unchanged).</summary>
    public double MouseSpeed
    {
        get
        {
            lock (saveLock)
            {
                // A hand-edited or broken value must not make the mouse unusable.
                return double.IsFinite(Settings.MouseSpeed) && Settings.MouseSpeed > 0 ? Math.Clamp(Settings.MouseSpeed, 0.1, 5) : 1.0;
            }
        }
    }

    /// <summary>True when control mode reads the mouse as raw input.</summary>
    public bool UseRawMouse
    {
        get
        {
            lock (saveLock)
            {
                return Settings.UseRawMouse;
            }
        }
    }

    /// <summary>True when every paired device is shown, not only phones and computers.</summary>
    public bool ShowAllDevices
    {
        get
        {
            lock (saveLock)
            {
                return Settings.ShowAllDevices;
            }
        }
    }

    public void SetShowAllDevices(bool show)
    {
        lock (saveLock)
        {
            Settings.ShowAllDevices = show;
            Save();
        }
    }

    /// <summary>The control shortcut. A missing or broken value in the file gives the default shortcut.</summary>
    public HotkeyCombo Hotkey
    {
        get
        {
            lock (saveLock)
            {
                return HotkeyCombo.TryParse(Settings.Hotkey, out var combo) ? combo : HotkeyCombo.Default;
            }
        }
    }

    public void SetHotkey(HotkeyCombo combo)
    {
        lock (saveLock)
        {
            Settings.Hotkey = combo.ToString();
            Save();
        }
    }

    public void SetUseRawMouse(bool use)
    {
        lock (saveLock)
        {
            Settings.UseRawMouse = use;
            Save();
        }
    }

    public void SetMouseSpeed(double speed)
    {
        lock (saveLock)
        {
            Settings.MouseSpeed = speed;
            Save();
        }
    }

    /// <summary>Remembers that the device has connected over HID at least once.</summary>
    public void SetHidVerified(string key, bool verified)
    {
        lock (saveLock)
        {
            var device = GetOrCreate(key);
            device.HidVerified = verified;
            Cleanup(key, device);
            Save();
        }
    }

    private DeviceSettings GetOrCreate(string key) =>
        Settings.Devices.TryGetValue(key, out var device) ? device : new DeviceSettings();

    /// <summary>Keeps the record only when it holds something. An empty record is the same as no record.</summary>
    private void Cleanup(string key, DeviceSettings device)
    {
        if (device.Name == null && device.Icon == null && !device.HidVerified)
        {
            Settings.Devices.Remove(key);
        }
        else
        {
            Settings.Devices[key] = device;
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(filePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath)) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // A broken settings file must not stop the app. Start with defaults instead.
            Console.Error.WriteLine($"Could not read settings from '{filePath}': {ex.Message}");
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not save settings to '{filePath}': {ex.Message}");
        }
    }
}
