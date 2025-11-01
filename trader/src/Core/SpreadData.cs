using System;

namespace TraderBot.Core
{
    /// <summary>
    /// Represents data received from the SpreadAggregator service.
    /// </summary>
    public class SpreadData
    {
        public string Exchange { get; set; }
        public string Symbol { get; set; }
        public decimal BestBid { get; set; }
        public decimal BestAsk { get; set; }
        public DateTime Timestamp { get; set; }
    }
}