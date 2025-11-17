namespace TraderBot.Host
{
    /// <summary>
    /// Конфигурация для конкретной биржи и торговой пары.
    /// </summary>
    public class ExchangeConfig
    {
        /// <summary>
        /// Название биржи (например, "Bybit" или "GateIo"). Должно совпадать с названием, используемым в проекте `collections`.
        /// </summary>
        public string ExchangeName { get; set; }
        /// <summary>
        /// API ключ для доступа к бирже.
        /// </summary>
        public string ApiKey { get; set; }
        /// <summary>
        /// Секретный ключ API для доступа к бирже.
        /// </summary>
        public string ApiSecret { get; set; }
        /// <summary>
        /// Торговая пара (символ), по которой будет работать бот (например, "BTC-USDT").
        /// </summary>
        public string Symbol { get; set; }
        /// <summary>
        /// Сумма для одной сделки.
        /// </summary>
        public decimal Amount { get; set; }
        /// <summary>
        /// Продолжительность торгового цикла в минутах.
        /// </summary>
        public int DurationMinutes { get; set; }
    }
}