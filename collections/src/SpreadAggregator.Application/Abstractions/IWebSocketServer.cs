using System.Threading.Tasks;

namespace SpreadAggregator.Application.Abstractions;

/// <summary>
/// Defines the contract for a WebSocket server.
/// </summary>
public interface IWebSocketServer
{
    /// <summary>
    /// Starts the WebSocket server.
    /// </summary>
    void Start();

    /// <summary>
    /// Broadcasts a message to all connected real-time clients.
    /// </summary>
    /// <param name="message">The message to send.</param>
    Task BroadcastRealtimeAsync(string message);
}