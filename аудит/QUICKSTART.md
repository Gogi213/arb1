# QUICKSTART - ARB1 PROJECT

Быстрый старт для запуска и использования ARB1 Arbitrage Trading System.

---

## ⚡ БЫСТРЫЙ ЗАПУСК (30 секунд)

### 1. Запустить приложение

**Windows:**
```powershell
cd "c:\visual projects\arb1\collections\src\SpreadAggregator.Presentation"
dotnet run
```

### 2. Открыть в браузере

```
http://localhost:5000/index.html
```

**Готово!** 🎉

---

## 📊 ЧТО ВЫ УВИДИТЕ

### Dashboard (http://localhost:5000/index.html)

**Основные элементы:**
- 📈 **График spread opportunities** - исторические данные из parquet
- ⚡ **Real-time обновления** - через WebSocket (обновления каждую секунду)
- 📊 **uPlot интерактивные графики** - zoom, pan, tooltips
- 🔄 **Автоматическая загрузка** - данные загружаются при открытии страницы

**Доступные пары (примеры):**
- BTCUSDT: Binance vs Bybit
- ETHUSDT: Binance vs GateIo
- ICPUSDT: Bybit vs GateIo
- ...и другие торговые пары

---

## 🔌 API ENDPOINTS

### Health Check
```bash
curl http://localhost:5000/api/health
```

**Ответ:**
```json
{"status": "healthy"}
```

### Historical Data (NDJSON)
```bash
curl "http://localhost:5000/api/dashboard_data?symbol=BTCUSDT&exchange1=Binance&exchange2=Bybit"
```

**Формат ответа:**
```json
{"timestamps": [...], "spreads": [...], "upperBands": [...], "lowerBands": [...]}
```

### Real-time WebSocket

**JavaScript пример:**
```javascript
const ws = new WebSocket('ws://localhost:5000/ws/realtime_charts');

ws.onmessage = (event) => {
  const data = JSON.parse(event.data);
  console.log('Real-time data:', data);
};
```

---

## 📁 ГДЕ ХРАНЯТСЯ ДАННЫЕ

### BidAsk Логи (для latency analysis)
```
collections/logs/bidask_YYYYMMDD_HHMMSS.log
collections/logs/bidask_ICPUSDT_YYYYMMDD_HHMMSS.log
```

**Формат CSV:**
```csv
LocalTimestamp,ServerTimestamp,Exchange,Symbol,BestBid,BestAsk,SpreadPercentage
2025-11-09 12:34:56.789,N/A,Binance,BTCUSDT,50000.10,50001.20,0.0022
```

### Parquet Market Data
```
data/market_data/
  exchange=Binance/
    symbol=BTCUSDT/
      date=2025-11-09/
        hour=12/
          spreads-34-56.1234567.parquet
```

---

## 🛠️ НАСТРОЙКА (опционально)

### appsettings.json

**Изменить DataLake путь:**
```json
{
  "DataLake": {
    "Path": "C:\\custom\\path\\to\\data"
  }
}
```

**Изменить BidAsk log директорию:**
```json
{
  "Logging": {
    "BidAskLogDirectory": "C:\\custom\\logs"
  }
}
```

**Настроить биржи:**
```json
{
  "ExchangeSettings": {
    "Exchanges": {
      "Binance": {
        "VolumeFilter": {
          "MinUsdVolume": 100000,
          "MaxUsdVolume": 50000000
        }
      }
    }
  }
}
```

---

## 🔍 TROUBLESHOOTING

### Приложение не запускается

**Проверьте порт 5000:**
```powershell
netstat -ano | findstr :5000
```

**Если порт занят, измените в Program.cs или appsettings.json:**
```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5001"
      }
    }
  }
}
```

### Dashboard не показывает данные

**Проверьте наличие parquet файлов:**
```powershell
dir "c:\visual projects\arb1\data\market_data" /s
```

**Если файлов нет:**
- Подождите несколько минут - данные записываются батчами по 1000 записей
- Проверьте консоль на ошибки подключения к биржам

### Real-time не обновляется

**Откройте консоль браузера (F12):**
- Должно быть сообщение: `WebSocket connected`
- Проверьте Network tab → WS → должен быть активный WebSocket

**Проверьте last update на странице:**
- Должно обновляться каждую секунду
- Если задержка >10 секунд - перезапустите приложение

---

## 📊 МОНИТОРИНГ

### Проверка памяти

**Windows:**
```powershell
# Task Manager -> Details -> найдите SpreadAggregator.Presentation.exe
# Смотрите на Memory column
```

**dotnet-counters:**
```bash
dotnet tool install --global dotnet-counters
dotnet-counters monitor --process-id <PID>
```

### Анализ BidAsk latency

**PowerShell:**
```powershell
# Импорт CSV и анализ
Import-Csv "collections\logs\bidask_20251109_120000.log" |
  Select LocalTimestamp, Exchange, Symbol, BestBid, BestAsk |
  Format-Table
```

**Excel/Python:**
- Откройте CSV в Excel
- Постройте график LocalTimestamp по символам
- Анализируйте gaps между обновлениями

---

## 🚀 PRODUCTION DEPLOYMENT

### Build Release

```bash
cd collections/src/SpreadAggregator.Presentation
dotnet publish -c Release -o ../../publish
```

### Запуск в production

**Windows Service:**
```powershell
sc.exe create "ARB1Service" binPath="C:\path\to\publish\SpreadAggregator.Presentation.exe"
sc.exe start "ARB1Service"
```

**Linux systemd:**
```bash
# Create /etc/systemd/system/arb1.service
[Unit]
Description=ARB1 Arbitrage Trading System

[Service]
WorkingDirectory=/opt/arb1/publish
ExecStart=/usr/bin/dotnet /opt/arb1/publish/SpreadAggregator.Presentation.dll
Restart=always

[Install]
WantedBy=multi-user.target
```

---

## 📚 ДОПОЛНИТЕЛЬНЫЕ РЕСУРСЫ

**Полная документация:**
- [README.md](./README.md) - индекс всей документации
- [01_EXECUTIVE_SUMMARY.md](./01_EXECUTIVE_SUMMARY.md) - executive summary
- [CHANGELOG.md](./CHANGELOG.md) - история всех изменений

**Техническая информация:**
- [02_CRITICAL_FIXES_COMPLETED.md](./02_CRITICAL_FIXES_COMPLETED.md) - OOM fixes
- [03_MIGRATION_COMPLETE.md](./03_MIGRATION_COMPLETE.md) - детали миграции
- [04_ARCHITECTURE_ANALYSIS.md](./04_ARCHITECTURE_ANALYSIS.md) - архитектурный анализ

---

## 💡 СОВЕТЫ

1. **Оставляйте приложение запущенным 24/7** - данные собираются непрерывно
2. **Мониторьте BidAsk логи** - они помогут выявить latency issues
3. **Проверяйте dashboard каждый день** - ищите интересные spread opportunities
4. **Делайте backup parquet файлов** - они содержат ценные исторические данные
5. **Анализируйте ICPUSDT отдельный лог** - он содержит детальные данные для конкретной пары

---

**Версия:** v1.1-optimized (2025-11-09)
**Статус:** Production Ready ✅
