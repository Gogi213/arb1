# PROPOSAL-2025-0058: Обеспечить запуск WebSocket-сервера

## Диагностика
Приложение `trader` не может подключиться к `SpreadAggregator` (`collections`), потому что `FleckWebSocketServer` в `SpreadAggregator` не запускается. Метод `Start()` для `IWebSocketServer` никогда не вызывается, в результате чего порт `8181` остается закрытым.

## Предлагаемое изменение
Нужно внедрить `IWebSocketServer` в `OrchestrationService` и вызвать его метод `Start()` в начале `StartAsync`.

**Файл для изменения:** `collections/src/SpreadAggregator.Application/Services/OrchestrationService.cs`

```diff
<<<<<<< SEARCH
:start_line:23
-------
    private readonly ILogger<OrchestrationService> _logger;
    private readonly SpreadCalculator _spreadCalculator;
    private readonly ChannelReader<MarketData> _marketDataChannelReader;
    private readonly IDataWriter _dataWriter;
    private readonly IConfiguration _configuration;

    public OrchestrationService(
        IEnumerable<IExchangeClient> exchangeClients,
        ILogger<OrchestrationService> logger,
        SpreadCalculator spreadCalculator,
        ChannelReader<MarketData> marketDataChannelReader,
        IDataWriter dataWriter,
        IConfiguration configuration)
    {
        _exchangeClients = exchangeClients.ToList();
        _logger = logger;
        _spreadCalculator = spreadCalculator;
        _marketDataChannelReader = marketDataChannelReader;
        _dataWriter = dataWriter;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
=======
    private readonly ILogger<OrchestrationService> _logger;
    private readonly SpreadCalculator _spreadCalculator;
    private readonly ChannelReader<MarketData> _marketDataChannelReader;
    private readonly IDataWriter _dataWriter;
    private readonly IConfiguration _configuration;
    private readonly IWebSocketServer _webSocketServer;

    public OrchestrationService(
        IEnumerable<IExchangeClient> exchangeClients,
        ILogger<OrchestrationService> logger,
        SpreadCalculator spreadCalculator,
        ChannelReader<MarketData> marketDataChannelReader,
        IDataWriter dataWriter,
        IConfiguration configuration,
        IWebSocketServer webSocketServer)
    {
        _exchangeClients = exchangeClients.ToList();
        _logger = logger;
        _spreadCalculator = spreadCalculator;
        _marketDataChannelReader = marketDataChannelReader;
        _dataWriter = dataWriter;
        _configuration = configuration;
        _webSocketServer = webSocketServer;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _webSocketServer.Start();
>>>>>>> REPLACE
```

## Обоснование
Это изменение гарантирует, что WebSocket-сервер будет запущен вместе с основным оркестрирующим сервисом, что позволит `trader` успешно подключаться. `OrchestrationService` является логичным "владельцем" сервера, так как он управляет всем потоком данных, которые этот сервер должен транслировать.

## Оценка рисков
Риск минимален. Изменение только добавляет вызов запуска необходимого сервиса в правильном месте.

## План тестирования
1.  Запустить проект `SpreadAggregator.Presentation`.
2.  Убедиться, что в консоли появилось сообщение `[Fleck] Client connected...` после запуска `trader`.
3.  Проверить, что `trader` успешно подключается и начинает получать данные (логи `trader` не должны содержать ошибку `Connection failed`).

## План отката
Отменить изменения в файле `OrchestrationService.cs`.