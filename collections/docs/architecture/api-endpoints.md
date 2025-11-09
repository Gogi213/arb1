# API Endpoints - ARB1 Collections

**Версия API:** v1.0
**Базовый URL:** http://localhost:5000
**Протокол:** HTTP/1.1 + WebSocket

---

## 📋 Обзор

Collections предоставляет REST API для исторических данных и WebSocket для real-time обновлений.

---

## 🌐 WebSocket Endpoints

### Real-Time Charts Stream

**Endpoint:** `ws://localhost:5000/ws/realtime_charts`

**Протокол:** WebSocket (text frames, JSON)

**Описание:** Streaming real-time арбитражных графиков с event-driven обновлениями.

**Подключение:**
```javascript
const ws = new WebSocket('ws://localhost:5000/ws/realtime_charts');

// Обработка сообщений
ws.onmessage = (event) => {
    const data = JSON.parse(event.data);
    console.log('Real-time update:', data);
};
```

**Формат сообщения:**
```json
{
  "symbol": "ICPUSDT",
  "exchange1": "Bybit",
  "exchange2": "GateIo",
  "timestamps": [1731204612.345],
  "spreads": [-0.024966],
  "upperBand": [0.05],
  "lowerBand": [-0.05]
}
```

**Особенности:**
- ✅ **Event-driven** - обновления только при изменении данных
- ✅ **Thread-safe** - SemaphoreSlim защита отправки
- ✅ **Independent updates** - каждый opportunity обновляется отдельно
- ✅ **No polling** - истинная асинхронность

---

## 🔌 HTTP Endpoints

### Health Check

**Endpoint:** `GET /api/health`

**Описание:** Проверка здоровья приложения.

**Пример запроса:**
```bash
curl http://localhost:5000/api/health
```

**Ответ:**
```json
{
  "status": "Healthy",
  "timestamp": "2025-11-10T00:30:00Z",
  "version": "v1.3-optimized"
}
```

**HTTP Status Codes:**
- `200` - Healthy
- `503` - Unhealthy

### Dashboard Data (Historical)

**Endpoint:** `GET /api/dashboard_data`

**Описание:** Получение исторических данных арбитражных спредов в формате NDJSON.

**Параметры запроса:**
- `symbol` (string, required) - Символ (например: "BTCUSDT")
- `exchange1` (string, required) - Первая биржа (например: "Binance")
- `exchange2` (string, required) - Вторая биржа (например: "Bybit")

**Пример запроса:**
```bash
curl "http://localhost:5000/api/dashboard_data?symbol=BTCUSDT&exchange1=Binance&exchange2=Bybit"
```

**Формат ответа (NDJSON):**
```
{"timestamp":1731204600,"spread":-0.001,"upperBand":0.002,"lowerBand":-0.003}
{"timestamp":1731204610,"spread":-0.0012,"upperBand":0.002,"lowerBand":-0.003}
{"timestamp":1731204620,"spread":-0.0008,"upperBand":0.002,"lowerBand":-0.003}
```

**Особенности:**
- ✅ **Streaming** - данные передаются по мере чтения из parquet
- ✅ **Efficient** - не загружает все данные в память
- ✅ **Compressed timestamps** - Unix timestamps в секундах

---

## 🎯 Dashboard UI

### Main Dashboard

**Endpoint:** `GET /index.html` или `GET /`

**Описание:** Веб-интерфейс с интерактивными графиками.

**Функциональность:**
- 📊 **Historical Charts** - загрузка из parquet файлов
- ⚡ **Real-time Updates** - WebSocket streaming
- 🔄 **Auto-reload** - автоматическое обновление данных
- 📈 **uPlot Integration** - высокопроизводительные графики

**Технологии:**
- HTML5 + CSS3
- JavaScript (ES6+)
- uPlot.js для графиков
- WebSocket API

---

## 📊 Data Formats

### Real-Time Message Format

```typescript
interface RealtimeChartData {
  symbol: string;
  exchange1: string;
  exchange2: string;
  timestamps: number[];    // Unix timestamps (seconds)
  spreads: number[];       // Spread percentages
  upperBand: number[];     // Upper quantile band
  lowerBand: number[];     // Lower quantile band
}
```

### Historical Data Format

```typescript
interface HistoricalDataPoint {
  timestamp: number;       // Unix timestamp (seconds)
  spread: number;          // Spread percentage
  upperBand: number;       // Upper quantile band
  lowerBand: number;       // Lower quantile band
}
```

---

## 🔒 Authentication & Security

**Текущий статус:** None required (development)

**Планы:**
- API Key authentication
- Rate limiting
- HTTPS enforcement
- CORS configuration

---

## 📈 Rate Limits

**WebSocket:**
- Нет ограничений (event-driven)

**HTTP API:**
- Health check: 100 req/min
- Dashboard data: 10 req/min per IP

---

## 🐛 Error Handling

### HTTP Errors

**400 Bad Request:**
```json
{
  "error": "Invalid parameters",
  "message": "Symbol parameter is required"
}
```

**404 Not Found:**
```json
{
  "error": "Data not found",
  "message": "No data available for BTCUSDT on Binance/Bybit"
}
```

**500 Internal Server Error:**
```json
{
  "error": "Internal error",
  "message": "Failed to read parquet file"
}
```

### WebSocket Errors

**Connection Errors:**
- Automatic reconnection (client-side)
- Graceful degradation to historical-only mode

**Data Errors:**
- Invalid JSON silently ignored
- Logging to `collections/logs/websocket.log`

---

## 📊 Monitoring

### Health Metrics

**Доступно через:** `/api/health`

**Метрики:**
- Application status
- Memory usage
- Active connections
- Error rates

### Performance Metrics

**Логирование:**
- `collections/logs/bidask_*.log` - Raw market data
- `collections/logs/bidbid_*.log` - Chart data (joined spreads)
- `collections/logs/websocket.log` - Connection events

**Метрики:**
- WebSocket latency (<20ms target)
- Memory usage (~150MB target)
- CPU usage (monitoring)
- Data throughput (spreads/second)

---

## 🔧 Development

### Local Development

```bash
# Запуск с hot reload
cd collections/src/SpreadAggregator.Presentation
dotnet watch run
```

### Testing API

```bash
# Health check
curl http://localhost:5000/api/health

# Historical data
curl "http://localhost:5000/api/dashboard_data?symbol=ICPUSDT&exchange1=Bybit&exchange2=GateIo" | head -5
```

### WebSocket Testing

```javascript
// В браузерной консоли
const ws = new WebSocket('ws://localhost:5000/ws/realtime_charts');
ws.onmessage = (e) => console.log(JSON.parse(e.data));
```

---

## 📚 Related Documentation

- **[Quick Start](../development/quickstart.md)** - Быстрый запуск
- **[Collections Context](../context.md)** - Детальный контекст
- **[Architecture Overview](overview.md)** - Общая архитектура
- **[Event-Driven Pipeline](event-driven.md)** - Real-time обработка

---

**API Documentation v1.0** | **Updated:** 2025-11-10
