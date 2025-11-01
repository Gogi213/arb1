namespace SpreadAggregator.Domain.Entities
{
    public class WebSocketMessage
    {
        public required string MessageType { get; init; }
        public required object Payload { get; init; }
    }
}