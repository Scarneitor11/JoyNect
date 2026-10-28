using System.Globalization;
using JoyNect.Core.Devices;
using JoyNect.Core.Hid;
using JoyNect.Core.Protocol;

namespace JoyNect.Core.Motion;

/// <summary>
/// Turns the live controller sessions into the four slots a DSU client expects.
/// </summary>
/// <remarks>
/// This is the one place that decides what a player slot means to DSU. JoyNect numbers players 1-4;
/// DSU numbers slots 0-3, so the conversion happens here rather than being repeated by the server.
/// Motion is read through <see cref="MotionPipeline"/>, the same conversion the Motion page uses, so
/// an emulator and the on-screen readout can never disagree about units.
/// </remarks>
public sealed class DsuSlotSource
{
    private readonly Func<IReadOnlyList<SwitchControllerSession>> _sessions;
    private readonly TimeProvider _time;

    public DsuSlotSource(Func<IReadOnlyList<SwitchControllerSession>> sessions, TimeProvider? timeProvider = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Reads the active slots now, newest motion included.</summary>
    public IReadOnlyList<DsuSlot> Read()
    {
        var sessions = _sessions();
        var chosen = new DsuSlot?[DsuProtocol.MaxSlots];
        var timestamp = TimestampMicroseconds();

        foreach (var session in sessions)
        {
            if (session.State != ControllerLinkState.Ready)
            {
                continue;
            }

            var playerSlot = session.Slot;
            if (playerSlot is < 1 or > DsuProtocol.MaxSlots)
            {
                continue;
            }

            var index = playerSlot - 1;
            var hasMotion = HasMotion(session);

            // A combined Joy-Con pair puts both halves in the same player slot. Prefer the half that
            // has the IMU, so a pair still publishes motion; otherwise the first half wins.
            if (chosen[index] is { } existing && (existing.HasMotion || !hasMotion))
            {
                continue;
            }

            if (!session.TryGetLatest(out var latest))
            {
                continue;
            }

            var motion = hasMotion
                ? MotionPipeline.Convert(latest.Sample2, session.ImuCalibration, 0)
                : MotionSample.Zero;

            chosen[index] = new DsuSlot
            {
                Slot = index,
                Connected = true,
                HasMotion = hasMotion,
                Connection = ToDsuConnection(session.Connection),
                MacAddress = ParseMac(session.Info?.MacAddressText),
                Battery = ToDsuBattery(latest.Battery, latest.Charging),
                AccelerationG = motion.AccelerationG,
                AngularVelocityDps = motion.AngularVelocityDps,
                TimestampMicroseconds = timestamp,
            };
        }

        var slots = new List<DsuSlot>(DsuProtocol.MaxSlots);
        foreach (var slot in chosen)
        {
            if (slot is not null)
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    private long TimestampMicroseconds()
    {
        var frequency = _time.TimestampFrequency;
        return frequency <= 0 ? 0 : (long)(_time.GetTimestamp() * (1_000_000.0 / frequency));
    }

    private static bool HasMotion(SwitchControllerSession session) =>
        !SwitchModels.IsNsoRetro(session.Model.Model);

    private static byte ToDsuConnection(ConnectionKind connection) => connection switch
    {
        ConnectionKind.Usb => 1,
        ConnectionKind.Bluetooth => 2,
        _ => 0,
    };

    private static byte ToDsuBattery(BatteryLevel level, bool charging)
    {
        if (charging)
        {
            return level == BatteryLevel.Full ? (byte)0xEF : (byte)0xEE;
        }

        return level switch
        {
            BatteryLevel.Empty or BatteryLevel.Critical => 1,
            BatteryLevel.Low => 2,
            BatteryLevel.Medium => 3,
            BatteryLevel.Full => 5,
            _ => 0,
        };
    }

    private static ulong ParseMac(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var parts = text.Split(':', '-');
        if (parts.Length != 6)
        {
            return 0;
        }

        ulong mac = 0;
        foreach (var part in parts)
        {
            if (!byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var octet))
            {
                return 0;
            }

            mac = (mac << 8) | octet;
        }

        return mac;
    }
}
