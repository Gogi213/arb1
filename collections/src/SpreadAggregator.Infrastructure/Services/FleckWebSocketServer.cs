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
        lock (_lock)
        {
            var tasks = _allSockets.Where(s => s.IsAvailable).Select(s => s.Send(message));
            return Task.WhenAll(tasks);
        }
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}