# Barbotine Development Sprints

Полная документация разработки multi-pair arbitrage bot с интеграцией C# SpreadAggregator.

## Архитектурное решение

**Принципы:**
- KISS (Keep It Simple, Stupid)
- YAGNI (You Aren't Gonna Need It)
- **C# проект НЕ ТРОГАЕМ вообще**
- C# = Data Provider (WebSocket сервер)
- Python = Trading Engine + Decision Maker

**Режим работы:** Только fake-money (симуляция), никакой реальной торговли

---

## SPRINT 1: WebSocket Client

**Цель:** Подключиться к C# WebSocket и получать данные в реальном времени

**Файл:** `ws_client.py`

**Ключевые особенности:**
- C# отправляет данные в **PascalCase** ("Fields", "Data")
- Формат данных: колоночный (columnar)
- Нужна функция `parse_spread_data()` для конвертации в Python dicts
- WebSocket URL: `ws://127.0.0.1:8181`

**Основные компоненты:**

```python
def parse_spread_data(raw_message: str) -> List[Dict]:
    """
    Парсит JSON от C# (PascalCase) в Python dicts

    Входной формат от C#:
    {
        "Fields": ["symbol", "exchange", "bestBid", "bestAsk"],
        "Data": [
            ["BTC/USDT", "Binance", 42500.5, 42501.2],
            ...
        ]
    }

    Выходной формат:
    [
        {"symbol": "BTC/USDT", "exchange": "Binance", "bestBid": 42500.5, "bestAsk": 42501.2},
        ...
    ]
    """
    # Обрабатываем и PascalCase и camelCase
    fields = message.get('Fields') or message.get('fields')
    data_rows = message.get('Data') or message.get('data')
```

**Тестирование:**
- Запустить C# проект: `cd src/SpreadAggregator.Presentation && dotnet run`
- Запустить клиент: `python ws_client.py`
- Ожидается: "✓ Connected to ws://127.0.0.1:8181" и получение 613+ пар

**Известные проблемы и решения:**
1. **Unicode на Windows:** Добавить `sys.stdout.reconfigure(encoding='utf-8')`
2. **PascalCase keys:** Поддержка обоих вариантов через `get('Fields') or get('fields')`

---

## SPRINT 2: Arbitrage Analyzer

**Цель:** Анализ кросс-биржевых арбитражных возможностей

**Файл:** `arbitrage_analyzer.py`

**Логика:**
1. Группировать данные по symbol
2. Для каждого symbol найти:
   - **min_ask** (самый низкий ask) → где покупать
   - **max_bid** (самый высокий bid) → где продавать
3. Рассчитать profit: `(max_bid - min_ask) / min_ask * 100`
4. Сортировать по прибыльности (descending)

**Структура данных:**

```python
@dataclass
class ArbitrageOpportunity:
    symbol: str                 # "BTC/USDT"
    min_ask: float             # 42500 (покупаем здесь)
    min_ask_exchange: str      # "Binance"
    max_bid: float             # 42520 (продаем здесь)
    max_bid_exchange: str      # "BingX"
    profit_pct: float          # 0.047%
    exchange_count: int        # 5 (на скольких биржах торгуется)
```

**Пример использования:**

```python
analyzer = ArbitrageAnalyzer()
opportunities = analyzer.analyze(spread_data)  # Список отсортирован по profit_pct
```

**Тестирование:**
- Запустить: `python arbitrage_analyzer.py`
- Проверить корректность расчета профита
- Проверить сортировку (самый прибыльный первый)

---

## SPRINT 3: Filter Engine

**Цель:** Динамическая фильтрация возможностей по критериям

**Файл:** `filter_engine.py`

**Фильтры:**
1. **min_exchanges** (>=3) - пара должна быть минимум на N биржах
2. **min_profit_pct** (>=0.5%) - прибыль должна быть >= X%

**Ключевые особенности:**
- Динамическое обновление фильтров через `update_config()`
- Статистика прохождения фильтров
- Конфигурация через dataclass

```python
@dataclass
class FilterConfig:
    min_exchanges: int = 3
    min_profit_pct: float = 0.5

filter_engine = FilterEngine(FilterConfig(min_exchanges=3, min_profit_pct=0.5))
filtered = filter_engine.filter(opportunities)

# Динамическое обновление
filter_engine.update_config(min_exchanges=4, min_profit_pct=0.6)
```

**Статистика:**
```python
stats = filter_engine.get_statistics(opportunities)
# {'total': 100, 'passed': 15, 'pass_rate': 15.0}
```

---

## SPRINT 4: Balance Manager

**Цель:** Управление капиталом для мультипарной торговли

**Файл:** `balance_manager.py`

**Проблема:** Как распределить ограниченный капитал между несколькими парами?

**Решение:**
- Весь доступный капитал выделяется новой паре
- При старте новой пары: `available_usdt` → разделяется между биржами
- Для каждой биржи: 50% USDT конвертируется в криптовалюту

**Структура данных:**
```python
pair_balances = {
    "BTC/USDT": {
        "Binance": {"usdt": 2500, "crypto": 0.05},
        "BingX": {"usdt": 2500, "crypto": 0.05}
    }
}
```

**Основные методы:**

```python
# 1. Выделить капитал для пары
allocation = balance_manager.allocate_for_pair('BTC/USDT', ['Binance', 'BingX'])

# 2. Купить начальную позицию
balance_manager.set_initial_crypto('BTC/USDT', 'Binance', 0.05, 42500)

# 3. Выполнить арбитражную сделку
balance_manager.execute_trade(
    symbol='BTC/USDT',
    buy_exchange='Binance',
    sell_exchange='BingX',
    amount=0.01,
    buy_price=42500,
    sell_price=42520,
    fees={'Binance': {'base': 0.001, 'quote': 0.001}, ...}
)

# 4. Получить балансы
balances = balance_manager.get_pair_balances('BTC/USDT')

# 5. Рассчитать общую стоимость портфеля
total_value = balance_manager.get_total_value({'BTC/USDT': 42510})
```

**ВАЖНОЕ ИСПРАВЛЕНИЕ:**

В `set_initial_crypto()` нужно ВЫЧИТАТЬ стоимость покупки из USDT:

```python
def set_initial_crypto(self, symbol, exchange, crypto_amount, purchase_price):
    cost = crypto_amount * purchase_price
    self.pair_balances[symbol][exchange]['crypto'] = crypto_amount
    self.pair_balances[symbol][exchange]['usdt'] -= cost  # КРИТИЧНО!
```

Без этого балансы не сходятся!

---

## SPRINT 5: Multi-Pair Trader

**Цель:** Управление торговлей нескольких пар одновременно

**Файл:** `multi_pair_trader.py`

**Концепция:** TradingSession для каждой пары

```python
@dataclass
class TradingSession:
    symbol: str
    exchanges: List[str]
    last_trade_time: float  # Для cooldown
    trade_count: int
    total_profit: float
```

**Основные методы:**

```python
# 1. Начать торговлю парой (с автовыделением капитала)
trader.start_trading_pair(opportunity)
# - Проверяет доступность капитала
# - Выделяет капитал через balance_manager
# - Покупает начальные позиции (50% USDT → crypto)
# - Создает TradingSession

# 2. Обработать возможность
trader.process_opportunity(opportunity)
# - Проверяет cooldown (min_trade_interval)
# - Проверяет наличие crypto для продажи
# - Выполняет сделку
# - Обновляет статистику

# 3. Остановить торговлю парой
trader.stop_trading_pair('BTC/USDT', current_price)

# 4. Получить статистику
stats = trader.get_statistics()
# {
#     'total_pairs': 3,
#     'total_trades': 45,
#     'total_profit': 125.50,
#     'sessions': {
#         'BTC/USDT': {'trades': 20, 'profit': 50.0},
#         ...
#     }
# }
```

**КЛЮЧЕВОЕ ИЗМЕНЕНИЕ в Sprint 7:**

`start_trading_pair()` теперь **автоматически выделяет капитал**:

```python
def start_trading_pair(self, opportunity):
    # Проверка доступности капитала
    if self.balance_manager.available_usdt <= 0:
        return False

    # Автоматическое выделение
    allocation = self.balance_manager.allocate_for_pair(symbol, exchanges)

    # Покупка начальных позиций
    buy_price = opportunity.min_ask
    crypto_amount = usdt_per_exchange * 0.5 / buy_price
    for exchange in exchanges:
        self.balance_manager.set_initial_crypto(symbol, exchange, crypto_amount, buy_price)

    # Создание сессии
    self.active_sessions[symbol] = TradingSession(...)
    return True
```

**Параметры:**
- `mode='fake-money'` - только симуляция
- `min_trade_interval=1.0` - минимум 1 сек между сделками одной пары
- `trade_amount_pct=0.1` - торговать 10% от crypto баланса за раз

---

## SPRINT 6: TUI Interface

**Цель:** Интерфейс для мониторинга и управления

**Файл:** `barbotine_tui.py`

**Технологии:**
- **Textual** - TUI framework
- **Rich** - форматирование текста
- Async/await для WebSocket

**Структура интерфейса:**

```
┌─────────────────────────────────────────────────────────────┐
│ Barbotine Multi-Pair Arbitrage Bot - Real-time Dashboard   │
├───────────────────┬─────────────────────────────────────────┤
│ Filter Settings   │ Statistics                              │
│                   │ Active Pairs: 3                         │
│ Min Exchanges: 3  │ Total Trades: 45                        │
│ Min Profit %: 0.5 │ Total Profit: $125.50                   │
│                   │ Updates: 1234                           │
│ [Apply Filters]   │                                         │
│                   ├─────────────────────────────────────────┤
│ [Start Trading]   │ Symbol  | Profit% | Buy From | Sell To │
│                   │─────────┼─────────┼──────────┼─────────│
│                   │ BTC/USDT| +0.047% | Binance  | BingX   │
│                   │ ETH/USDT| +0.444% | Binance  | BingX   │
│                   │ ...                                     │
└───────────────────┴─────────────────────────────────────────┘
```

**3 основных компонента:**

1. **FilterPanel** (левая панель):
   - Input для min_exchanges
   - Input для min_profit_pct
   - Кнопка "Apply Filters"
   - Кнопка "Start Trading" / "Stop Trading"

2. **StatsPanel** (верх справа):
   - Active Pairs (реактивное обновление)
   - Total Trades
   - Total Profit
   - Updates (счетчик обновлений от WebSocket)

3. **DataTable** (основная область):
   - Топ-20 отфильтрованных возможностей
   - Колонки: Symbol, Profit%, Buy From, Sell To, Exchanges, Trades
   - Последняя колонка "Trades" показывает количество сделок (если пара активна)

**Reactive properties:**

```python
class StatsPanel(Static):
    total_pairs = reactive(0)
    total_trades = reactive(0)
    total_profit = reactive(0.0)
    data_updates = reactive(0)

    def watch_total_pairs(self, value: int):
        self.query_one("#stat_pairs", Label).update(f"Active Pairs: {value}")
```

**Основной цикл обработки данных:**

```python
async def listen_to_data(self):
    def on_data(parsed_data):
        # 1. Анализ
        opportunities = self.analyzer.analyze(parsed_data)

        # 2. Фильтрация
        filtered = self.filter_engine.filter(opportunities)

        # 3. Обновление таблицы
        self.update_pairs_table(filtered[:20])

        # 4. Обновление статистики
        stats = self.trader.get_statistics()
        stats_panel.total_pairs = stats['total_pairs']
        # ...

        # 5. Авто-трейдинг (Sprint 7)
        if self.auto_trading_enabled:
            # ... (см. Sprint 7)

    await self.ws_client.listen(on_data)
```

---

## SPRINT 7: Auto-Trading

**Цель:** Автоматический запуск торговли для отфильтрованных пар

**Изменения в `barbotine_tui.py`:**

**1. Добавлены флаги состояния:**
```python
self.auto_trading_enabled = False  # Торговля вкл/выкл
self.max_trading_pairs = 3  # Максимум пар одновременно
```

**2. Кнопка Start/Stop Trading:**
```python
yield Button("Start Trading", id="toggle_trading", variant="success")

def toggle_trading(self):
    self.auto_trading_enabled = not self.auto_trading_enabled
    button = self.query_one("#toggle_trading", Button)

    if self.auto_trading_enabled:
        button.label = "Stop Trading"
        button.variant = "error"  # Красная
        self.notify("Auto-trading ENABLED")
    else:
        button.label = "Start Trading"
        button.variant = "success"  # Зеленая
        self.notify("Auto-trading DISABLED")
```

**3. Логика автоматического старта:**

```python
if self.auto_trading_enabled:
    # Проверяем: можем ли добавить еще пары?
    active_count = len(self.trader.active_sessions)
    if active_count < self.max_trading_pairs:
        # Ищем топ пары, которые еще не торгуем
        for opp in filtered[:self.max_trading_pairs]:
            if opp.symbol not in self.trader.active_sessions:
                if self.trader.start_trading_pair(opp):
                    self.notify(f"Started trading {opp.symbol} ({opp.profit_pct:.2f}% profit)")
                    break  # Добавляем по одной паре за раз

    # Обрабатываем возможности для активных пар
    for opp in filtered[:10]:
        if opp.symbol in self.trader.active_sessions:
            self.trader.process_opportunity(opp)
```

**Как это работает:**

1. Пользователь нажимает "Start Trading"
2. При каждом обновлении от WebSocket (~1/сек):
   - Если активных пар < 3:
     - Находит самую прибыльную отфильтрованную пару
     - Вызывает `trader.start_trading_pair(opp)`
     - MultiPairTrader выделяет капитал и покупает начальные позиции
   - Для всех активных пар:
     - Проверяет cooldown
     - Выполняет сделки при благоприятных возможностях
3. Active Pairs, Total Trades, Total Profit обновляются в реальном времени

---

## Установка и запуск

**1. Установка зависимостей:**
```bash
cd barbotine-arbitrage-bot-main
pip install -r requirements.txt
```

**2. Запуск C# backend:**
```bash
cd ../src/SpreadAggregator.Presentation
dotnet run
```

Ждем: `Server started at ws://127.0.0.1:8181`

**3. Запуск TUI:**
```bash
cd ../../barbotine-arbitrage-bot-main
python barbotine_tui.py
```

**4. Использование:**
- Подключится автоматически к `ws://127.0.0.1:8181`
- Настроить фильтры (по желанию)
- Нажать "Apply Filters"
- Нажать "Start Trading" для включения автоматической торговли
- Нажать "Stop Trading" для остановки
- `q` для выхода

---

## Тестирование

### Unit Tests (62 теста)

Создать тестовые файлы в директории `tests/`:

**test_ws_client.py** (7 тестов):
- Парсинг PascalCase JSON
- Парсинг camelCase JSON
- Обработка пустых данных
- Обработка невалидного JSON

**test_arbitrage_analyzer.py** (10 тестов):
- Анализ возможностей
- Сортировка по прибыльности
- Обработка пар с < 2 биржами
- Расчет profit_pct

**test_filter_engine.py** (16 тестов):
- Фильтрация по min_exchanges
- Фильтрация по min_profit_pct
- Динамическое обновление конфигурации
- Статистика прохождения

**test_balance_manager.py** (17 тестов):
- Выделение капитала
- Покупка начальных позиций
- Выполнение сделок с комиссиями
- Расчет общей стоимости портфеля
- Освобождение капитала

**test_multi_pair_trader.py** (12 тестов):
- Старт торговли парой
- Остановка торговли
- Обработка возможностей
- Cooldown периоды
- Статистика

Запуск тестов:
```bash
pytest tests/ -v
```

---

## Известные проблемы и решения

### 1. AttributeError: property 'is_running' of 'BarbotineTUI' object has no setter

**Проблема:** Textual App имеет встроенное read-only свойство `is_running`

**Решение:** Переименовать в `listening`:
```python
self.listening = False  # Вместо self.is_running
```

### 2. Балансы не сходятся после покупки crypto

**Проблема:** `set_initial_crypto()` не вычитала USDT

**Решение:**
```python
cost = crypto_amount * purchase_price
self.pair_balances[symbol][exchange]['crypto'] = crypto_amount
self.pair_balances[symbol][exchange]['usdt'] -= cost  # ДОБАВИТЬ!
```

### 3. Капитал не выделяется при старте торговли

**Проблема:** Старый `start_trading_pair()` не выделял капитал автоматически

**Решение:** Добавить автоматическое выделение в метод (см. Sprint 5/7)

### 4. Unicode ошибки на Windows

**Проблема:** `UnicodeEncodeError` при выводе символов

**Решение:**
```python
if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8')
```

---

## Архитектура итогового решения

```
┌─────────────────────────────────────────────────────────────┐
│                    C# SpreadAggregator                       │
│  (8 exchanges: Binance, BingX, Bybit, Gate.io, OKX, etc.)  │
│         WebSocket Server: ws://127.0.0.1:8181               │
│         Broadcasts 613+ pairs every second                  │
└────────────────────────┬────────────────────────────────────┘
                         │ WebSocket (PascalCase JSON)
                         ▼
┌─────────────────────────────────────────────────────────────┐
│              BARBOTINE ARBITRAGE BOT (Python)               │
├─────────────────────────────────────────────────────────────┤
│  ws_client.py          → WebSocket client + parser          │
│  arbitrage_analyzer.py → Cross-exchange opportunity finder  │
│  filter_engine.py      → Dynamic filtering (exchanges, %)   │
│  balance_manager.py    → Capital allocation & management    │
│  multi_pair_trader.py  → Multi-pair trading orchestration   │
│  barbotine_tui.py      → TUI dashboard + auto-trading       │
└─────────────────────────────────────────────────────────────┘

Data Flow:
1. C# → WebSocket → ws_client.parse_spread_data()
2. parsed_data → ArbitrageAnalyzer.analyze()
3. opportunities → FilterEngine.filter()
4. filtered → BarbotineTUI.update_pairs_table()
5. IF auto_trading_enabled:
   - MultiPairTrader.start_trading_pair() (auto-allocates capital)
   - MultiPairTrader.process_opportunity() (executes trades)
6. Stats → TUI reactive updates
```

---

## Следующие шаги (опционально)

### Вариант A: CSV Logging
- Логировать все сделки в CSV файл
- Колонки: timestamp, symbol, buy_exchange, sell_exchange, amount, profit, ...

### Вариант B: Trade History Panel
- Добавить панель с последними 10-20 сделками в TUI
- Показывать время, пару, прибыль

### Вариант C: SQLite Logging
- Сохранять историю сделок в SQLite БД
- Таблицы: trades, sessions, balances_snapshot

### Вариант D: Configuration File
- config.yaml с параметрами:
  - initial_balance
  - default_filters
  - websocket_url
  - fee_rates

### Вариант E: README
- Инструкции по установке
- Примеры использования
- FAQ

---

## Статистика проекта

**Код:**
- 6 основных модулей Python
- ~1200 строк кода (без комментариев)
- 62 unit tests (все проходят ✅)

**Функциональность:**
- Подключение к 8 биржам через C# (613+ пар)
- Анализ кросс-биржевого арбитража
- Динамическая фильтрация
- Автоматическое управление капиталом
- Мультипарная торговля (до 3 пар одновременно)
- Real-time TUI dashboard
- Автоматический старт/стоп торговли

**Готовность:** ~85% (все core features работают, остается логирование и документация)

---

## Быстрый старт (для восстановления)

```bash
# 1. Восстановить файлы (уже сделано выше)
# 2. Установить зависимости
pip install -r requirements.txt

# 3. Запустить C# backend (в отдельном терминале)
cd src/SpreadAggregator.Presentation
dotnet run

# 4. Запустить TUI
cd barbotine-arbitrage-bot-main
python barbotine_tui.py

# 5. В TUI:
# - Настроить фильтры (опционально)
# - Нажать "Start Trading"
# - Наблюдать за автоматической торговлей
# - Нажать "q" для выхода
```

Готово! Проект полностью восстановлен и готов к использованию. 🚀
