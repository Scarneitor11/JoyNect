using JoyNect.Core.Motion;
using Microsoft.Extensions.Logging;

namespace JoyNect.App.Services;

/// <summary>
/// Applies the motion-server settings to the real UDP server.
/// </summary>
/// <remarks>
/// Kept out of <see cref="AppState"/> so the server's threads and bind failures have one owner. The
/// page only ever sees a snapshot, which means it cannot accidentally start or stop the server from a
/// paint pass.
/// </remarks>
internal sealed class MotionServerHost : IDisposable
{
    private readonly DsuMotionServer _server;

    public MotionServerHost(DsuSlotSource source, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _server = new DsuMotionServer(source.Read, loggerFactory.CreateLogger<DsuMotionServer>());
        Status = new MotionServerSnapshot
        {
            Enabled = false,
            Running = false,
            Port = DsuMotionServer.DefaultPort,
        };
    }

    public MotionServerSnapshot Status { get; private set; }

    /// <summary>Starts, stops or rebinds the server to match the current settings.</summary>
    public MotionServerSnapshot Apply(bool enabled, int port)
    {
        if (!enabled)
        {
            _server.Stop();
            Status = new MotionServerSnapshot
            {
                Enabled = false,
                Running = false,
                Port = port,
            };

            return Status;
        }

        var running = _server.Start(port);
        Status = new MotionServerSnapshot
        {
            Enabled = true,
            Running = running,
            Port = running ? _server.Port : port,
            Error = running ? null : _server.LastError ?? "The motion server could not start.",
        };

        return Status;
    }

    public void Dispose() => _server.Dispose();
}
