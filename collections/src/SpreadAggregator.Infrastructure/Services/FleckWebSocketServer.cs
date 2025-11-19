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
    private readonly System.Threading.Timer _cleanupTimer;

    public FleckWebSocketServer(string location, Func<OrchestrationService> orchestrationServiceFactory)
    {
        _server = new WebSocketServer(location);
        _allSockets = new List<IWebSocketConnection>();
        _orchestrationServiceFactory = orchestrationServiceFactory;

        // PROPOSAL-2025-0095: Dead connection cleanup every 5 minutes
        _cleanupTimer = new System.Threading.Timer(CleanupDeadConnections, null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
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
            if (socket.IsAvailable)
            {
                try
                {
                    tasks.Add(socket.Send(message));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fleck] Error sending to socket: {ex.Message}. Client might have disconnected.");
                }
            }
        }
        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// PROPOSAL-2025-0095: Cleanup dead WebSocket connections
    /// Removes connections that are closed but not properly removed from list
    /// </summary>
    private void CleanupDeadConnections(object? state)
    {
        lock (_lock)
        {
            var deadConnections = _allSockets
                .Where(s => !s.IsAvailable)
                .ToList();

            foreach (var socket in deadConnections)
            {
                _allSockets.Remove(socket);
            }

            if (deadConnections.Count > 0)
            {
                Console.WriteLine($"[Fleck] Cleaned up {deadConnections.Count} dead connections. Active: {_allSockets.Count}");
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}