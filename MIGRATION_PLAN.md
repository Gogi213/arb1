# ПЛАН МИГРАЦИИ: Charts → Collections

**Дата начала:** 2025-11-08
**Целевая дата:** 2025-11-15 (7 дней)
**Цель:** Объединить Python charts и C# collections в единый C# проект

---

## 📊 МЕТРИКИ УСПЕХА

| Метрика | До | После | Цель |
|---------|-----|-------|------|
| Проектов | 2 | 1 | -50% |
| Процессов | 2 | 1 | -50% |
| LOC | 1120 | ~650 | -42% |
| Память | 708 MB | ~150 MB | -79% |
| Latency | 26.5ms | <20ms | -25% |
| Проблем OOM | 43 | <20 | -53% |

---

## 🎯 SPRINT 1: ИНФРАСТРУКТУРА И ПОДГОТОВКА (День 1-2)

### Задачи:

**1.1. Создать новую ветку**
```bash
git checkout -b feature/merge-charts-to-collections
```

**1.2. Добавить Polars.NET**
```bash
cd collections
dotnet add src/SpreadAggregator.Infrastructure package PolyglotDataFrame
```

**1.3. Создать структуру для Chart API**
```
src/SpreadAggregator.Api/
├── Controllers/
│   ├── DashboardController.cs
│   └── RealTimeController.cs
├── Services/
│   ├── ChartDataService.cs
│   └── ParquetReaderService.cs
└── Models/
    ├── ChartDataDto.cs
    └── OpportunityDto.cs
```

**1.4. Настроить ASP.NET Core в Program.cs**
- Добавить CORS
- Настроить WebSocket endpoints
- Добавить Controllers

### Тесты Sprint 1:

```csharp
// Test 1: Polars.NET установлен корректно
[Fact]
public void PolarsNet_CanLoadParquetFile()
{
    var testFile = "test.parquet";
    var df = PolarsDataFrame.ReadParquet(testFile);
    Assert.NotNull(df);
}

// Test 2: ASP.NET Core запускается
[Fact]
public async Task AspNetCore_StartsSuccessfully()
{
    var app = CreateTestApp();
    await app.StartAsync();
    Assert.True(app.Services != null);
    await app.StopAsync();
}

// Test 3: CORS настроен
[Fact]
public async Task Cors_AllowsRequests()
{
    var client = CreateTestClient();
    var response = await client.GetAsync("/health");
    Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
}
```

**Критерий завершения Sprint 1:**
- ✅ Polars.NET установлен
- ✅ ASP.NET Core запускается
- ✅ CORS работает
- ✅ Все 3 теста зелёные

---

## 🎯 SPRINT 2: PARQUET ЧТЕНИЕ + CHART DATA (День 3-4)

### Задачи:

**2.1. Портировать ParquetReaderService**

Создать `ParquetReaderService.cs`:
```csharp
public class ParquetReaderService
{
    private readonly string _dataLakePath;

    public async Task<ChartData> LoadAndProcessPair(
        string symbol,
        string exchange1,
        string exchange2)
    {
        // Load parquet files for both exchanges
        var df1 = await LoadExchangeData(exchange1, symbol);
        var df2 = await LoadExchangeData(exchange2, symbol);

        // AsOf join (equivalent to Python join_asof)
        var merged = df1.JoinAsof(df2, on: "timestamp", strategy: "backward");

        // Calculate spread
        var result = merged
            .WithColumn("spread", (col("bid_a") / col("bid_b") - 1) * 100)
            .Filter(col("spread").IsNotNull());

        return ToChartData(result);
    }
}
```

**2.2. Портировать OpportunityFilter**

```csharp
public class OpportunityFilterService
{
    public List<Opportunity> GetFilteredOpportunities()
    {
        var statsFile = GetLatestStatsFile();
        var df = PolarsDataFrame.ReadCsv(statsFile);

        return df
            .Filter(col("opportunity_cycles_040bp") > 40)
            .Sort(["symbol", "exchange1"])
            .Select(["symbol", "exchange1", "exchange2"])
            .ToObjects<Opportunity>();
    }
}
```

**2.3. Создать DashboardController**

```csharp
[ApiController]
[Route("api")]
public class DashboardController : ControllerBase
{
    private readonly ParquetReaderService _parquetReader;
    private readonly OpportunityFilterService _opportunityFilter;

    [HttpGet("dashboard_data")]
    public async IAsyncEnumerable<ChartData> GetDashboardData()
    {
        var opportunities = _opportunityFilter.GetFilteredOpportunities();

        foreach (var opp in opportunities)
        {
            var chartData = await _parquetReader.LoadAndProcessPair(
                opp.Symbol, opp.Exchange1, opp.Exchange2);

            if (chartData != null)
                yield return chartData;
        }
    }
}
```

### Тесты Sprint 2:

```csharp
// Test 4: Parquet файлы читаются корректно
[Fact]
public async Task ParquetReader_LoadsExchangeData()
{
    var service = new ParquetReaderService(_dataLakePath);
    var df = await service.LoadExchangeData("binance", "BTC/USDT");

    Assert.NotNull(df);
    Assert.True(df.Columns.Contains("timestamp"));
    Assert.True(df.Columns.Contains("BestBid"));
}

// Test 5: AsOf join работает
[Fact]
public async Task ParquetReader_AsOfJoinWorks()
{
    var service = new ParquetReaderService(_dataLakePath);
    var chartData = await service.LoadAndProcessPair("BTC/USDT", "binance", "okx");

    Assert.NotNull(chartData);
    Assert.True(chartData.Timestamps.Count > 0);
    Assert.True(chartData.Spreads.Count > 0);
}

// Test 6: Opportunity filter работает
[Fact]
public void OpportunityFilter_ReturnsFilteredList()
{
    var service = new OpportunityFilterService(_statsPath);
    var opportunities = service.GetFilteredOpportunities();

    Assert.NotEmpty(opportunities);
    Assert.All(opportunities, opp => Assert.True(opp.OpportunityCycles > 40));
}

// Test 7: Dashboard endpoint возвращает NDJSON
[Fact]
public async Task DashboardController_ReturnsNdjson()
{
    var client = CreateTestClient();
    var response = await client.GetAsync("/api/dashboard_data");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType.MediaType);
}
```

**Критерий завершения Sprint 2:**
- ✅ Parquet чтение работает (Polars.NET)
- ✅ AsOf join эквивалентен Python версии
- ✅ Dashboard endpoint возвращает данные
- ✅ Все 4 новых теста зелёные (4/7 total)

---

## 🎯 SPRINT 3: REAL-TIME DATA + WEBSOCKET (День 5-6)

### Задачи:

**3.1. Интегрировать RollingWindow с Chart API**

Модифицировать `RollingWindowService.cs`:
```csharp
public class RollingWindowService : IDisposable
{
    // ... existing code ...

    public ChartData? JoinRealtimeWindows(string symbol, string exchange1, string exchange2)
    {
        var window1 = GetWindowData(exchange1, symbol);
        var window2 = GetWindowData(exchange2, symbol);

        if (window1 == null || window2 == null ||
            window1.Spreads.Count == 0 || window2.Spreads.Count == 0)
            return null;

        // Convert to DataFrames
        var df1 = ToDataFrame(window1.Spreads);
        var df2 = ToDataFrame(window2.Spreads);

        // AsOf join
        var merged = df1.JoinAsof(df2, on: "timestamp", strategy: "backward");

        // Calculate spread
        var result = merged.WithColumn("spread", (col("bid_a") / col("bid_b") - 1) * 100);

        return ToChartData(result, symbol, exchange1, exchange2);
    }
}
```

**3.2. Создать Real-Time WebSocket endpoint**

```csharp
[Route("ws/realtime_charts")]
public class RealTimeController : ControllerBase
{
    private readonly RollingWindowService _rollingWindow;
    private readonly OpportunityFilterService _opportunityFilter;

    [HttpGet]
    public async Task HandleWebSocket()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            await StreamRealtimeData(webSocket);
        }
    }

    private async Task StreamRealtimeData(WebSocket webSocket)
    {
        while (webSocket.State == WebSocketState.Open)
        {
            var opportunities = _opportunityFilter.GetFilteredOpportunities();
            var chartDataList = new List<ChartData>();

            foreach (var opp in opportunities)
            {
                var chartData = _rollingWindow.JoinRealtimeWindows(
                    opp.Symbol, opp.Exchange1, opp.Exchange2);

                if (chartData != null)
                    chartDataList.Add(chartData);
            }

            var json = JsonSerializer.Serialize(chartDataList);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

            await Task.Delay(200); // 200ms update interval
        }
    }
}
```

**3.3. Удалить промежуточный WebSocket (Charts:8002)**

Клиенты теперь подключаются напрямую к Collections:8181

**3.4. Обновить FleckWebSocketServer**

Добавить поддержку прямых клиентских подключений (не только от Charts)

### Тесты Sprint 3:

```csharp
// Test 8: RollingWindow join работает корректно
[Fact]
public void RollingWindow_JoinsRealtimeWindows()
{
    var service = CreateRollingWindowWithTestData();
    var chartData = service.JoinRealtimeWindows("BTC/USDT", "binance", "okx");

    Assert.NotNull(chartData);
    Assert.Equal("BTC/USDT", chartData.Symbol);
    Assert.True(chartData.Spreads.Count > 0);
}

// Test 9: WebSocket endpoint принимает подключения
[Fact]
public async Task RealTimeController_AcceptsWebSocketConnection()
{
    var client = CreateWebSocketClient();
    await client.ConnectAsync(new Uri("ws://localhost:8181/ws/realtime_charts"), CancellationToken.None);

    Assert.Equal(WebSocketState.Open, client.State);
    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
}

// Test 10: WebSocket отправляет данные каждые 200ms
[Fact]
public async Task RealTimeController_SendsDataPeriodically()
{
    var client = CreateWebSocketClient();
    await client.ConnectAsync(new Uri("ws://localhost:8181/ws/realtime_charts"), CancellationToken.None);

    var buffer = new byte[8192];
    var receiveTask1 = client.ReceiveAsync(buffer, CancellationToken.None);
    var result1 = await receiveTask1;
    Assert.True(result1.Count > 0);

    var stopwatch = Stopwatch.StartNew();
    var receiveTask2 = client.ReceiveAsync(buffer, CancellationToken.None);
    var result2 = await receiveTask2;
    stopwatch.Stop();

    Assert.True(stopwatch.ElapsedMilliseconds >= 200);
    Assert.True(stopwatch.ElapsedMilliseconds < 300);
}

// Test 11: Клиенты подключаются напрямую (без Charts)
[Fact]
public async Task DirectClientConnection_Works()
{
    var client = CreateWebSocketClient();
    await client.ConnectAsync(new Uri("ws://localhost:8181/ws/realtime_charts"), CancellationToken.None);

    var buffer = new byte[8192];
    var result = await client.ReceiveAsync(buffer, CancellationToken.None);
    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
    var chartData = JsonSerializer.Deserialize<List<ChartData>>(json);

    Assert.NotNull(chartData);
    Assert.NotEmpty(chartData);
}
```

**Критерий завершения Sprint 3:**
- ✅ RollingWindow интегрирован с Chart API
- ✅ Real-time WebSocket работает
- ✅ Клиенты подключаются напрямую (Charts удалён из цепочки)
- ✅ Все 4 новых теста зелёные (11/11 total)

---

## 🎯 SPRINT 4: CLEANUP + OPTIMIZATION (День 7)

### Задачи:

**4.1. Удалить charts/ проект**
```bash
rm -rf charts/
```

**4.2. Обновить документацию**
- README.md - обновить архитектуру
- MIGRATION_PLAN.md - отметить как завершённый
- аудит/FIXES_APPLIED.md - добавить миграцию

**4.3. Оптимизация памяти**
- Проверить ObjectPool для DataFrames
- Убрать дублирование RollingWindow
- Проверить GC pressure

**4.4. Финальные интеграционные тесты**

### Тесты Sprint 4:

```csharp
// Test 12: Memory usage снижен
[Fact]
public async Task MemoryUsage_IsReduced()
{
    var initialMemory = GC.GetTotalMemory(true);

    // Simulate load
    await SimulateLoad(duration: TimeSpan.FromMinutes(5));

    var finalMemory = GC.GetTotalMemory(true);
    var usedMemory = (finalMemory - initialMemory) / (1024 * 1024); // MB

    Assert.True(usedMemory < 200, $"Memory usage {usedMemory}MB exceeds 200MB limit");
}

// Test 13: Latency улучшена
[Fact]
public async Task Latency_IsImproved()
{
    var stopwatch = Stopwatch.StartNew();

    var client = CreateTestClient();
    var response = await client.GetAsync("/api/dashboard_data");

    stopwatch.Stop();
    Assert.True(stopwatch.ElapsedMilliseconds < 20, $"Latency {stopwatch.ElapsedMilliseconds}ms exceeds 20ms target");
}

// Test 14: Один процесс вместо двух
[Fact]
public void SingleProcess_IsRunning()
{
    var processes = Process.GetProcesses()
        .Where(p => p.ProcessName.Contains("SpreadAggregator") || p.ProcessName.Contains("python"))
        .ToList();

    Assert.Single(processes.Where(p => p.ProcessName.Contains("SpreadAggregator")));
    Assert.Empty(processes.Where(p => p.ProcessName.Contains("python") && p.CommandLine.Contains("charts")));
}

// Test 15: E2E тест - полный flow
[Fact]
public async Task EndToEnd_FullFlowWorks()
{
    // 1. Start application
    var app = CreateTestApp();
    await app.StartAsync();

    // 2. Check health
    var client = CreateTestClient();
    var healthResponse = await client.GetAsync("/health");
    Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

    // 3. Get historical data
    var dashboardResponse = await client.GetAsync("/api/dashboard_data");
    Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);

    // 4. Connect WebSocket for real-time
    var wsClient = CreateWebSocketClient();
    await wsClient.ConnectAsync(new Uri("ws://localhost:8181/ws/realtime_charts"), CancellationToken.None);

    var buffer = new byte[8192];
    var result = await wsClient.ReceiveAsync(buffer, CancellationToken.None);
    Assert.True(result.Count > 0);

    // 5. Cleanup
    await wsClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    await app.StopAsync();
}
```

**Критерий завершения Sprint 4:**
- ✅ Charts/ проект удалён
- ✅ Документация обновлена
- ✅ Memory < 200 MB
- ✅ Latency < 20ms
- ✅ Все 4 финальных теста зелёные (15/15 total)

---

## 📈 ИТОГОВЫЕ МЕТРИКИ

После завершения всех спринтов:

| Метрика | Было | Стало | Результат |
|---------|------|-------|-----------|
| **Тестов** | 0 | 15 | ✅ 100% coverage |
| **Проектов** | 2 | 1 | ✅ -50% |
| **Процессов** | 2 | 1 | ✅ -50% |
| **LOC** | 1120 | ~650 | ✅ -42% |
| **Память** | 708 MB | <200 MB | ✅ -72% |
| **Latency** | 26.5ms | <20ms | ✅ -25% |

---

## 🚀 НАЧАЛО РАБОТЫ

```bash
# Sprint 1: Подготовка
git checkout -b feature/merge-charts-to-collections
cd collections
dotnet add package PolyglotDataFrame

# Sprint 2: Parquet + Charts
mkdir -p src/SpreadAggregator.Api/{Controllers,Services,Models}

# Sprint 3: Real-Time
# Implement WebSocket endpoints

# Sprint 4: Cleanup
rm -rf ../../charts/
git add .
git commit -m "feat: merge charts into collections"
```

**Готов начать Sprint 1?**
