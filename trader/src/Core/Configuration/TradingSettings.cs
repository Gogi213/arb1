namespace TraderBot.Core.Configuration
{
    public class TradingSettings
    {
        /// <summary>
        /// Minimum spread percentage required to consider a trade (e.g., 0.25 for 0.25%).
        /// </summary>
        public decimal SpreadThreshold { get; set; } = 0.25m;

        /// <summary>
        /// Maximum age of market data in seconds before it is considered stale.
        /// </summary>
        public double MaxDataAgeSeconds { get; set; } = 7.0;

        /// <summary>
        /// Delay in milliseconds to wait after a buy fill before executing the sell order.
        /// Used in ConvergentTrader.
        /// </summary>
        public int SellDelayMilliseconds { get; set; } = 5000;

        /// <summary>
        /// Offset in Quote Asset (e.g., USDT) to dig into the order book for liquidity.
        /// Used in TrailingTrader to calculate target price.
        /// </summary>
        public decimal TrailingLiquidityOffset { get; set; } = 25.0m;
    }
}
