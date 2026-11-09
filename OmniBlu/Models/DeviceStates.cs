namespace OmniBlu.Models;

/// <summary>Connection state of a device in the Available row.</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,

    /// <summary>
    /// The A2DP endpoint is started: Windows is ready to receive audio, and the phone (the host) can connect.
    /// No audio stream is open yet unless <see cref="AudioState"/> says so.
    /// </summary>
    Ready,
}

/// <summary>State of the audio stream (A2DP) of a device.</summary>
public enum AudioState
{
    Off,
    Opening,
    Streaming,
}
