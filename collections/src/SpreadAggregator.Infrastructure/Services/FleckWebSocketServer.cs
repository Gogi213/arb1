using Fleck;
using SpreadAggregator.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpreadAggregator.Infrastructure.Services;

public class FleckWebSocketServer : Application.Abstractions.IWebSocketServer, IDisposable
{
    private readonly WebSocketServer _server;
    private readonly List<IWebSocketConnection> _allSockets;
    private readonly object _lock = new object();

    public FleckWebSocketServer(string location)
    {
        _server = new WebSocketServer(location);
        _allSockets = new List<IWebSocketConnection>();
    }

    public void Start()
    {
        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                lock (_lock)
                {
                    Console.WriteLine($"[Fleck] Client connected: {socket.ConnectionInfo.ClientIpAddress}");
                    _allSockets.Add(socket);
                }
            };
            socket.OnClose = () =>
            {
                lock (_lock)
                {
                    _allSockets.Remove(socket);
                    Console.WriteLine($"[Fleck] Client disconnected.");
                }
            };
        });
    }

    public Task BroadcastRealtimeAsync(string message)
    {
        List<IWebSocketConnection> socketsSnapshot;
        lock (_lock)
        {
           // Take a snapshot to avoid holding the lock during I/O operations
           socketsSnapshot = _allSockets.ToList();
        }

        var tasks = new List<Task>();
        foreach (var socket in socketsSnapshot)
        {
            try
            {
                if (socket.IsAvailable)
                    tasks.Add(socket.Send(message));
            }
            catch (ObjectDisposedException)
            {
                // Expected exception if socket closed during broadcast. Ignore.
            }
        }
        return Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}