using CommunityToolkit.Mvvm.ComponentModel;
using OmniBlu.Managers;
using OmniBlu.Models;

namespace OmniBlu.ViewModels;

/// <summary>One smart device shown in the window.</summary>
public partial class DeviceViewModel : ObservableObject
{
    public DeviceViewModel(SmartDeviceInfo info, string name, string icon, bool hidVerified)
    {
        Key = info.Key;
        systemName = info.Name;
        supportsA2dp = info.SupportsA2dp;
        this.name = name;
        this.icon = icon;
        this.hidVerified = hidVerified;
    }

    /// <summary>Stable identity of the device (see <see cref="SmartDeviceInfo.Key"/>).</summary>
    public string Key { get; }

    /// <summary>Name reported by Windows. <see cref="Name"/> is the name the user sees and can edit.</summary>
    [ObservableProperty]
    private string systemName;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle), nameof(StatusText), nameof(DisabledReason))]
    private bool supportsA2dp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsConnecting), nameof(CanToggle), nameof(CanOfferAudio), nameof(DragHint), nameof(HasDragHint), nameof(StatusText), nameof(ActionText))]
    private ConnectionState connectionState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanToggle), nameof(StatusText))]
    private AudioState audioState;

    /// <summary>1-based position in the Audio row. 0 means the device is not in that row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAudio), nameof(CanOfferAudio), nameof(DragHint), nameof(HasDragHint))]
    private int audioPriority;

    /// <summary>1-based position in the Controllable row. 0 means the device is not in that row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControl), nameof(DragHint), nameof(HasDragHint))]
    private int controlPriority;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHidActive), nameof(HidStatusText))]
    private HidState hidState;

    /// <summary>True once the device has connected over HID. HID support cannot be detected before that.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HidStatusText))]
    private bool hidVerified;

    public bool IsReady => ConnectionState == ConnectionState.Ready;

    public bool IsConnecting => ConnectionState == ConnectionState.Connecting;

    public bool IsBusy => IsConnecting || AudioState == AudioState.Opening;

    public bool IsAudio => AudioPriority > 0;

    public bool IsControl => ControlPriority > 0;

    public bool IsHidActive => HidState == HidState.Active;

    public bool CanToggle => SupportsA2dp && !IsBusy;

    /// <summary>True when the device is ready and can be dragged to the Audio row.</summary>
    public bool CanOfferAudio => IsReady && !IsAudio;

    /// <summary>Tells the user which rows the card can be dragged to. Empty when none.</summary>
    public string DragHint
    {
        get
        {
            var rows = new List<string>();
            if (CanOfferAudio)
            {
                rows.Add("Audio");
            }

            if (!IsControl)
            {
                rows.Add("Controllable");
            }

            return rows.Count == 0 ? string.Empty : "Drag to " + string.Join(" or ", rows);
        }
    }

    public bool HasDragHint => DragHint.Length > 0;

    /// <summary>Why the Connect button is disabled. Null when it is not.</summary>
    public string DisabledReason => SupportsA2dp
        ? null
        : "This device does not support A2DP, so it cannot send audio to this PC.";

    public string StatusText
    {
        get
        {
            if (!SupportsA2dp)
            {
                return "No A2DP support";
            }

            return ConnectionState switch
            {
                ConnectionState.Connecting => "Connecting...",
                ConnectionState.Ready => AudioState switch
                {
                    AudioState.Streaming => "Streaming audio",
                    AudioState.Opening => "Opening audio...",
                    _ => "Ready, waiting for the phone",
                },
                _ => "Not connected",
            };
        }
    }

    public string HidStatusText => HidState switch
    {
        HidState.Active => "Connected, ready to control",
        HidState.Waiting => HidVerified
            ? "Waiting for the phone"
            : "Waiting for the phone (HID not verified yet)",
        _ => "HID off",
    };

    public string ActionText => ConnectionState switch
    {
        ConnectionState.Ready => "Disconnect",
        ConnectionState.Connecting => "Connecting...",
        _ => "Connect",
    };

    /// <summary>Applies the latest facts from the operating system. Keeps the user's custom name.</summary>
    public void UpdateFrom(SmartDeviceInfo info, bool hasCustomName)
    {
        SystemName = info.Name;
        SupportsA2dp = info.SupportsA2dp;
        if (!hasCustomName)
        {
            Name = info.Name;
        }
    }
}
