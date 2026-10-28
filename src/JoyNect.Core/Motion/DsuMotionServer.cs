using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace JoyNect.Core.Motion;

/// <summary>
/// Serves live controller motion to Cemuhook DSU clients over UDP.
/// </summary>
/// <remarks>
/// The server binds to loopback only, as the DSU specification expects, and serves every slot from one
/// socket. Requests are answered on the receive thread; the motion stream is produced by a second
/// thread at a fixed cadence. A subscription expires five seconds after the client stops renewing it,
/// because UDP has no connection to close and an emulator that has quit should not be sent packets
/// forever.
/// </remarks>
public sealed class DsuMotionServer : IDisposable
{
    public const int DefaultPort = 26760;

    public const int ClientTimeoutMilliseconds = 5_000;

    /// <summary>DSU clients typically consume 125-250 Hz; 125 Hz matches the controllers' own rate.</summary>
    public const int DefaultPollIntervalMilliseconds = 8;

    private readonly Func<IReadOnlyList<DsuSlot>> _source;
    private readonly ILogger<DsuMotionServer>? _logger;
    private readonly int _pollIntervalMilliseconds;
    private readonly uint _serverId;
    private readonly object _lifecycleGate = new();
    private readonly object _subscriptionGate = new();
    private readonly Dictionary<IPEndPoint, long> _allSlotSubscriptions = [];
    private readonly Dictionary<(IPEndPoint Client, int Slot), long> _slotSubscriptions = [];
    private readonly uint[] _packetNumbers = new uint[DsuProtocol.MaxSlots];

    private Socket? _socket;
    private Thread? _receiveThread;
    private Thread? _sendThread;
    private volatile bool _running;
    private volatile string? _lastError;
    private bool _disposed;

    public DsuMotionServer(
        Func<IReadOnlyList<DsuSlot>> source,
        ILogger<DsuMotionServer>? logger = null,
        int pollIntervalMilliseconds = DefaultPollIntervalMilliseconds)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger;
        _pollIntervalMilliseconds = Math.Clamp(pollIntervalMilliseconds, 1, 1000);
        _serverId = (uint)Random.Shared.NextInt64(1, uint.MaxValue);

        // Keep the configured default visible before the first Start call; the actual bound port is
        // read back from the socket so a test (or a port of zero) still reports the truth.
        Port = DefaultPort;
    }

    /// <summary>The port currently bound, or the last requested port when stopped.</summary>
    public int Port { get; private set; }

    public bool IsRunning => _running;

    /// <summary>The reason the last start attempt failed, or null after a successful start.</summary>
    public string? LastError => _lastError;

    public int SubscriberCount
    {
        get
        {
            lock (_subscriptionGate)
            {
                return _allSlotSubscriptions.Count + _slotSubscriptions.Count;
            }
        }
    }

    /// <summary>Starts listening, or returns true if already listening on the requested port.</summary>
    public bool Start(int port)
    {
        if (port is < 0 or > 65535)
        {
            _lastError = "The motion port must be between 0 and 65535.";
            return false;
        }

        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_running && Port == port)
            {
                return true;
            }

            if (_running)
            {
                StopCore();
            }

            Port = port;
            Socket? socket = null;

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                SuppressIcmpPortUnreachable(socket);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));

                _socket = socket;
                Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
                _lastError = null;
                _running = true;

                lock (_subscriptionGate)
                {
                    _allSlotSubscriptions.Clear();
                    _slotSubscriptions.Clear();
                }

                Array.Clear(_packetNumbers);

                _receiveThread = new Thread(() => ReceiveLoop(socket))
                {
                    IsBackground = true,
                    Name = "JoyNect DSU receive",
                };

                _sendThread = new Thread(() => SendLoop(socket))
                {
                    IsBackground = true,
                    Name = "JoyNect DSU motion",
                };

                _receiveThread.Start();
                _sendThread.Start();

                _logger?.LogInformation("DSU motion server listening on 127.0.0.1:{Port}", Port);
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                socket?.Dispose();
                _running = false;
                _socket = null;
                _lastError = $"Port {port} is already in use by another application.";
                _logger?.LogWarning("DSU motion server could not bind port {Port}: already in use", port);
                return false;
            }
            catch (Exception ex)
            {
                socket?.Dispose();
                _running = false;
                _socket = null;
                _lastError = $"The motion server could not start on port {port}: {ex.Message}";
                _logger?.LogWarning(ex, "DSU motion server could not start on port {Port}", port);
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lifecycleGate)
        {
            if (!_running)
            {
                return;
            }

            StopCore();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            if (_running)
            {
                StopCore();
            }

            _disposed = true;
        }
    }

    private void StopCore()
    {
        _running = false;

        var socket = _socket;
        _socket = null;

        try
        {
            socket?.Close();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Closing the DSU socket failed");
        }

        Join(_receiveThread);
        Join(_sendThread);
        _receiveThread = null;
        _sendThread = null;

        lock (_subscriptionGate)
        {
            _allSlotSubscriptions.Clear();
            _slotSubscriptions.Clear();
        }

        Array.Clear(_packetNumbers);
    }

    private static void Join(Thread? thread)
    {
        if (thread is null || Thread.CurrentThread == thread)
        {
            return;
        }

        try
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
        catch (ThreadStateException)
        {
            // It was never started or has already exited; either way there is nothing to wait for.
        }
    }

    private void ReceiveLoop(Socket socket)
    {
        var buffer = new byte[2048];

        while (_running && ReferenceEquals(socket, _socket))
        {
            IPEndPoint sender;
            int received;

            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                received = socket.ReceiveFrom(buffer, ref remote);
                sender = (IPEndPoint)remote;
            }
            catch (SocketException) when (!_running)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                // A client that vanished can make Windows report an ICMP port-unreachable as an
                // error. The socket option below suppresses most of these; a straggler must not
                // take the server down.
                _logger?.LogDebug(ex, "DSU receive failed");
                continue;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "DSU receive failed");
                if (!_running)
                {
                    break;
                }

                continue;
            }

            if (received < DsuProtocol.HeaderSize + 4)
            {
                continue;
            }

            try
            {
                HandleRequest(buffer.AsSpan(0, received), sender, socket);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "A malformed DSU request was ignored");
            }
        }
    }

    private void HandleRequest(ReadOnlySpan<byte> datagram, IPEndPoint sender, Socket socket)
    {
        if (!DsuProtocol.TryParseRequest(datagram, out var request))
        {
            return;
        }

        switch (request.Kind)
        {
            case DsuRequestKind.Version:
                Send(socket, sender, DsuProtocol.BuildVersionResponse(_serverId));
                break;

            case DsuRequestKind.ControllerInfo:
            {
                var slots = ReadSlots();
                foreach (var requestedSlot in request.Slots)
                {
                    var slot = slots.FirstOrDefault(s => s.Slot == requestedSlot)
                        ?? DsuSlot.Disconnected(requestedSlot);
                    Send(socket, sender, DsuProtocol.BuildControllerInfo(slot, _serverId));
                }

                break;
            }

            case DsuRequestKind.PadData:
                Subscribe(request, sender, ReadSlots());
                break;
        }
    }

    private void Subscribe(DsuRequest request, IPEndPoint sender, IReadOnlyList<DsuSlot> slots)
    {
        // The endpoint is reused by ReceiveFrom, so store a snapshot of it rather than the live object.
        var client = new IPEndPoint(sender.Address, sender.Port);
        var now = Stopwatch.GetTimestamp();

        lock (_subscriptionGate)
        {
            if (request.Flags == 0)
            {
                _allSlotSubscriptions[client] = now;
                return;
            }

            if ((request.Flags & 0x01) != 0)
            {
                _slotSubscriptions[(client, request.Slot)] = now;
            }

            if ((request.Flags & 0x02) != 0 && request.Mac != 0)
            {
                foreach (var slot in slots)
                {
                    if (slot.Connected && slot.MacAddress == request.Mac)
                    {
                        _slotSubscriptions[(client, slot.Slot)] = now;
                    }
                }
            }
        }
    }

    private void SendLoop(Socket socket)
    {
        var slots = new DsuSlot?[DsuProtocol.MaxSlots];

        while (_running && ReferenceEquals(socket, _socket))
        {
            try
            {
                if (SubscriberCount > 0)
                {
                    Array.Clear(slots);
                    foreach (var slot in ReadSlots())
                    {
                        if (slot.Slot is >= 0 and < DsuProtocol.MaxSlots)
                        {
                            slots[slot.Slot] = slot;
                        }
                    }

                    for (var index = 0; index < slots.Length; index++)
                    {
                        var slot = slots[index];
                        if (slot is not { Connected: true })
                        {
                            continue;
                        }

                        var subscribers = Subscribers(index);
                        if (subscribers.Count == 0)
                        {
                            continue;
                        }

                        var packet = DsuProtocol.BuildPadData(slot, _packetNumbers[index], _serverId);
                        _packetNumbers[index]++;

                        foreach (var subscriber in subscribers)
                        {
                            Send(socket, subscriber, packet);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "A DSU motion tick failed");
            }

            if (!_running)
            {
                break;
            }

            Thread.Sleep(_pollIntervalMilliseconds);
        }
    }

    private List<IPEndPoint> Subscribers(int slot)
    {
        var endpoints = new List<IPEndPoint>();
        var seen = new HashSet<IPEndPoint>();
        var now = Stopwatch.GetTimestamp();
        var timeout = (long)(Stopwatch.Frequency * (ClientTimeoutMilliseconds / 1000.0));

        lock (_subscriptionGate)
        {
            if (_slotSubscriptions.Count > 0)
            {
                List<(IPEndPoint Client, int Slot)>? expired = null;
                foreach (var (key, lastRequest) in _slotSubscriptions)
                {
                    if (now - lastRequest > timeout)
                    {
                        (expired ??= []).Add(key);
                        continue;
                    }

                    if (key.Slot == slot && seen.Add(key.Client))
                    {
                        endpoints.Add(key.Client);
                    }
                }

                if (expired is not null)
                {
                    foreach (var key in expired)
                    {
                        _slotSubscriptions.Remove(key);
                    }
                }
            }

            if (_allSlotSubscriptions.Count > 0)
            {
                List<IPEndPoint>? expired = null;
                foreach (var (client, lastRequest) in _allSlotSubscriptions)
                {
                    if (now - lastRequest > timeout)
                    {
                        (expired ??= []).Add(client);
                        continue;
                    }

                    if (seen.Add(client))
                    {
                        endpoints.Add(client);
                    }
                }

                if (expired is not null)
                {
                    foreach (var client in expired)
                    {
                        _allSlotSubscriptions.Remove(client);
                    }
                }
            }
        }

        return endpoints;
    }

    private IReadOnlyList<DsuSlot> ReadSlots()
    {
        try
        {
            return _source() ?? [];
        }
        catch (Exception ex)
        {
            // A controller may be in the middle of connecting or disappearing. Motion is a live
            // signal, so an empty tick is better than bringing the server down.
            _logger?.LogDebug(ex, "Reading DSU motion slots failed");
            return [];
        }
    }

    private void Send(Socket socket, IPEndPoint destination, byte[] packet)
    {
        try
        {
            socket.SendTo(packet, destination);
        }
        catch (SocketException ex)
        {
            _logger?.LogDebug(ex, "Sending a DSU packet to {Client} failed", destination);
        }
        catch (ObjectDisposedException)
        {
            // The socket was closed by Stop while this packet was in flight.
        }
    }

    private static void SuppressIcmpPortUnreachable(Socket socket)
    {
        // Windows reports a connection-reset error on a UDP socket after an ICMP port-unreachable
        // from an emulator that has closed. SIO_UDP_CONNRESET turns that off; other platforms do not
        // have the option, and this app is Windows-only, but the try/catch keeps the server testable
        // on any host that can run the headless tests.
        const int SioUdpConnReset = unchecked((int)0x9800000C);
        try
        {
            socket.IOControl(SioUdpConnReset, new byte[4], null);
        }
        catch (Exception)
        {
            // Not fatal: the receive loop already tolerates ICMP-induced socket errors.
        }
    }
}
