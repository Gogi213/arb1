using Fleck;
using SpreadAggregator.Application.Abstractions;
using SpreadAggregator.Application.Services;
using SpreadAggregator.Domain.Entities;
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
    private readonly Func<OrchestrationService> _orchestrationServiceFactory;

    public FleckWebSocketServer(string location, Func<OrchestrationService> orchestrationServiceFactory)
    {
        _server = new WebSocketServer(location);
        _allSockets = new List<IWebSocketConnection>();
        _orchestrationServiceFactory = orchestrationServiceFactory;
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

                // Send all symbol info on connect
                var orchestrationService = _orchestrationServiceFactory();
                var allSymbols = orchestrationService.AllSymbolInfo;
                var wrapper = new WebSocketMessage { MessageType = "AllSymbolInfo", Payload = allSymbols };
                var message = System.Text.Json.JsonSerializer.Serialize(wrapper);
                socket.Send(message);
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