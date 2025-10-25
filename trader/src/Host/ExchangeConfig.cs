namespace TraderBot.Host
{
    public class ExchangeConfig
    {
        public string ExchangeName { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string Symbol { get; set; }
        public decimal Amount { get; set; }
        public int DurationMinutes { get; set; }
    }
}