# Диаграмма последовательности процесса "Leg 1" (Gate.io -> Bybit)

```mermaid
sequenceDiagram
    actor User
    participant Host as "Program.cs"
    participant AT as "ArbitrageTrader"
    participant TT as "TrailingTrader"
    participant Gate as "GateIoExchange (WS)"
    participant Bybit as "BybitExchange"
    participant BybitPrivate as "Bybit WS (Private)"
    participant BybitTrade as "Bybit WS (Trade)"

    User->>Host: Запуск приложения
    Host->>Gate: InitializeAsync()
    Host->>Bybit: InitializeAsync()
    Bybit->>BybitPrivate: Connect() & Auth()
    Bybit->>BybitTrade: Connect() & Auth()
    
    Host->>AT: new ArbitrageTrader(Gate, Bybit)
    Host->>AT: StartAsync(...)

    AT->>TT: new TrailingTrader(Gate)
    AT->>Bybit: SubscribeToOrderUpdatesAsync(HandleSellOrderUpdate)
    Bybit->>BybitPrivate: subscribe("order")

    AT->>TT: StartAsync(...)
    TT->>Gate: SubscribeToOrderBookUpdatesAsync()
    
    loop Трейлинг на Gate.io
        Gate-->>TT: OrderBook Update
        TT->>TT: CalculateTargetPrice()
        alt Ордер не существует
            TT->>Gate: PlaceOrderAsync(LIMIT BUY)
        else Ордер существует и цена изменилась
            TT->>Gate: ModifyOrderAsync()
        end
    end

    Gate-->>TT: Order Update: FILLED
    TT->>AT: OnOrderFilled(filledOrder)

    AT->>AT: HandleBuyOrderFilled(filledOrder)
    AT->>Bybit: PlaceOrderAsync(MARKET SELL)
    Bybit->>BybitTrade: order.create(...)
    BybitTrade-->>Bybit: OrderId

    BybitPrivate-->>Bybit: Order Update: FILLED
    Bybit->>AT: HandleSellOrderUpdate(sellOrder)
    
    AT->>AT: Рассчитать End-to-End Latency
    AT->>AT: CleanupAndSignalCompletionAsync()
    AT->>Host: arbitrageCycleTcs.SetResult(true)
    Host->>User: Вывод в консоль: "LEG 1 ... finished"