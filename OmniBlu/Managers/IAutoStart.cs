namespace OmniBlu.Managers;

/// <summary>Starts OmniBlu together with Windows. Windows keeps the choice, so this reads it back each time.</summary>
public interface IAutoStart
{
    /// <summary>False on platforms where this is not implemented.</summary>
    bool IsSupported { get; }

    /// <summary>Why the last <see cref="Apply"/> failed, for the user. Null when it did not fail.</summary>
    string LastError { get; }

    /// <summary>The mode that is set now. <see cref="StartupMode.Off"/> when none is set or when it cannot be read.</summary>
    StartupMode Read();

    /// <summary>Sets the mode for the program that is running now. Returns false when it could not be saved.</summary>
    bool Apply(StartupMode mode);
}
