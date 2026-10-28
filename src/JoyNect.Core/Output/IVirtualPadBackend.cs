namespace JoyNect.Core.Output;

/// <summary>One virtual controller that Windows (and therefore games) can see.</summary>
public interface IVirtualPad : IDisposable
{
    /// <summary>Player slot this pad occupies, 1 based.</summary>
    int Slot { get; }

    /// <summary>Human readable description shown in the user interface.</summary>
    string Description { get; }

    bool IsConnected { get; }

    /// <summary>Publishes a new state. Safe to call from any thread.</summary>
    void Submit(in Xbox360PadState state);

    /// <summary>Raised when a game asks for rumble; arguments are the large and small motor magnitudes.</summary>
    event Action<byte, byte>? RumbleRequested;

    /// <summary>Raised when the driver reports which pad indicator light to use.</summary>
    event Action<byte>? LedRequested;
}

/// <summary>
/// Creates virtual pads.
/// </summary>
/// <remarks>
/// An interface rather than a direct call into the driver because the pad backend is the one part of
/// JoyNect that depends on an end-of-life kernel driver: if it ever needs replacing (a user mode
/// backend, a different bus), only this implementation changes.
/// </remarks>
public interface IVirtualPadBackend : IDisposable
{
    string Name { get; }

    /// <summary>Whether pads can be created right now.</summary>
    DriverStatus Status { get; }

    /// <summary>Creates a pad for a player slot, or returns null when the driver refuses.</summary>
    IVirtualPad? CreatePad(int slot, string description, out string? error);
}

/// <summary>A backend that accepts states and does nothing: used by tests and monitor only mode.</summary>
public sealed class NullPadBackend : IVirtualPadBackend
{
    public string Name => "none";

    public DriverStatus Status { get; } = new()
    {
        Available = false,
        Summary = "Virtual pads are disabled; controllers are read but not presented to games.",
    };

    public IVirtualPad? CreatePad(int slot, string description, out string? error)
    {
        error = "Virtual pads are disabled.";
        return null;
    }

    public void Dispose()
    {
    }
}

/// <summary>A fake pad for tests, recording everything it was told.</summary>
public sealed class NullPad : IVirtualPad
{
    public NullPad(int slot, string description)
    {
        Slot = slot;
        Description = description;
    }

    public int Slot { get; }

    public string Description { get; }

    public bool IsConnected { get; private set; } = true;

    public Xbox360PadState LastState { get; private set; } = Xbox360PadState.Neutral;

    public int SubmitCount { get; private set; }

    public event Action<byte, byte>? RumbleRequested;

    public event Action<byte>? LedRequested;

    public void Submit(in Xbox360PadState state)
    {
        LastState = state;
        SubmitCount++;
    }

    public void Disconnect() => IsConnected = false;

    public void SimulateRumble(byte large, byte small) => RumbleRequested?.Invoke(large, small);

    public void SimulateLed(byte led) => LedRequested?.Invoke(led);

    public void Dispose() => IsConnected = false;
}
