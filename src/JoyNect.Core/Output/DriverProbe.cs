using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace JoyNect.Core.Output;

/// <summary>Result of checking whether virtual gamepads can be created right now.</summary>
public sealed record DriverStatus
{
    public required bool Available { get; init; }

    public required string Summary { get; init; }

    /// <summary>Actionable next step when <see cref="Available"/> is false.</summary>
    public string? Remediation { get; init; }

    public string? Error { get; init; }

    public bool TestPadCreated { get; init; }

    public static DriverStatus Ok(string summary, bool testPadCreated = false) =>
        new() { Available = true, Summary = summary, TestPadCreated = testPadCreated };
}

/// <summary>
/// Checks whether the ViGEmBus kernel driver is present and usable.
/// </summary>
/// <remarks>
/// JoyNect never installs the driver: ViGEmBus is a signed kernel driver that has been
/// end-of-life since November 2023, so the app only detects it and points at the official
/// installer. Without it JoyNect still reads controllers and shows live input; it just cannot
/// present them to games.
/// </remarks>
public static class DriverProbe
{
    public const string OfficialInstallerUri = "https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0";

    public const string MissingDriverRemediation =
        "Install the ViGEmBus driver (version 1.22.0) from its official GitHub release, then start " +
        "JoyNect again. The installer needs administrator approval. Without the driver JoyNect still " +
        "reads your controllers and shows live input, but games cannot see them.";

    public static DriverStatus Probe(ILogger? logger = null, bool createTestPad = false)
    {
        logger ??= NullLogger.Instance;
        ViGEmClient? client = null;

        try
        {
            // The managed client connects to the bus from its constructor: there is no Connect().
            client = new ViGEmClient();

            var summary = "ViGEmBus is available: virtual Xbox 360 controllers can be created.";
            var testPadCreated = false;

            if (createTestPad)
            {
                var pad = client.CreateXbox360Controller();
                try
                {
                    pad.Connect();
                    testPadCreated = true;
                    summary += " A test pad was created and removed successfully.";
                }
                finally
                {
                    TryDisconnect(pad.Disconnect, logger);
                }
            }

            return DriverStatus.Ok(summary, testPadCreated);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ViGEmBus probe failed");
            var issue = ClassifyFailure(ex);
            return new DriverStatus
            {
                Available = false,
                Summary = issue.Summary,
                Remediation = issue.Remediation,
                // Do not echo raw exception text into the UI/diagnostic report: it can contain
                // internal paths or module names. The full exception is already logged above.
                Error = null,
            };
        }
        finally
        {
            try
            {
                client?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "ViGEm client teardown after probe failed");
            }
        }
    }

    private static void TryDisconnect(Action disconnect, ILogger logger)
    {
        try
        {
            disconnect();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Temporary test pad could not be disconnected cleanly");
        }
    }

    /// <summary>
    /// Maps a driver failure onto a user-facing explanation.
    /// </summary>
    /// <remarks>
    /// The client library exposes one exception type per native <c>VIGEM_ERROR</c>, so classification
    /// is type based. The message check at the end is a safety net for future client versions that
    /// might collapse these into a single type.
    /// </remarks>
    public static (string Summary, string Remediation) ClassifyFailure(Exception ex)
    {
        switch (ex)
        {
            case VigemBusNotFoundException:
                return (
                    "The ViGEmBus driver is not installed, so virtual Xbox 360 controllers cannot be created.",
                    MissingDriverRemediation);

            case VigemBusVersionMismatchException:
                return (
                    "The installed ViGEmBus driver is older than the client library expects.",
                    "Update ViGEmBus to version 1.22.0 from its official GitHub release, then restart JoyNect.");

            case VigemBusAccessFailedException:
            case VigemBusInvalidHandleException:
                return (
                    "The ViGEmBus driver refused access to JoyNect.",
                    "Close other controller tools (DS4Windows, BetterJoy, x360ce) that may be holding the " +
                    "driver, then try again. If it keeps happening, reboot Windows.");

            case VigemNoFreeSlotException:
                return (
                    "ViGEmBus has no free virtual controller slot left.",
                    "Disconnect some controllers or close other tools that create virtual pads, then try again.");

            case VigemAlreadyConnectedException:
                return (
                    "Another program has already claimed this virtual pad.",
                    "Close other controller tools and try again.");

            case VigemNotSupportedException:
                return (
                    "This version of ViGEmBus does not support the requested operation.",
                    "Reinstall ViGEmBus 1.22.0 from its official GitHub release.");
        }

        // Safety net for client versions that report every failure as a plain exception.
        var message = ex.Message;
        if (Contains(message, "BUS_NOT_FOUND"))
        {
            return (
                "The ViGEmBus driver is not installed, so virtual Xbox 360 controllers cannot be created.",
                MissingDriverRemediation);
        }

        if (Contains(message, "VERSION_MISMATCH"))
        {
            return (
                "The installed ViGEmBus driver is older than the client library expects.",
                "Update ViGEmBus to version 1.22.0 from its official GitHub release, then restart JoyNect.");
        }

        if (Contains(message, "ACCESS"))
        {
            return (
                "The ViGEmBus driver refused access to JoyNect.",
                "Close other controller tools that may be holding the driver, then try again.");
        }

        return (
            "JoyNect could not talk to the ViGEmBus driver.",
            "Reinstall ViGEmBus 1.22.0 from its official GitHub release if the problem continues, and " +
            "include JoyNect's diagnostic report when asking for help.");
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
