using System.Net.WebSockets;
using System.Text;

var uri = new Uri("ws://localhost:5000/ws/realtime_charts");
using var ws = new ClientWebSocket();
await ws.ConnectAsync(uri, CancellationToken.None);
Console.WriteLine("Connected!");

var buffer = new byte[1024 * 4];
while (ws.State == WebSocketState.Open)
{
    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    if (result.MessageType == WebSocketMessageType.Close) break;
    // Console.WriteLine("Received message"); // Don't spam console
}
