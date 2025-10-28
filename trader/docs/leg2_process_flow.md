# Диаграмма последовательности процесса "Leg 2" (Bybit -> Gate.io)

```mermaid
sequenceDiagram
    participant Host as "Program.cs"
    participant RAT as "ReverseArbitrageTrader"
    participant BTT as "BybitTrailingTrader"
    participant Bybit as "BybitExchange"
    participant BybitPrivate as "Bybit WS (Private)"
    participant BybitTrade as "Bybit WS (Trade)"
    participant BybitPublic as "Bybit WS (Public)"
    participant Gate as "GateIoExchange (WS)"

    Host->>Bybit: InitializeAsync()
    Bybit->>BybitPrivate: Connect() & Auth()
    Bybit->>BybitTrade: Connect() & Auth()
    Bybit->>BybitPublic: Connect()
    Host->>RAT: new ReverseArbitrageTrader(...)
    Host->>RAT: StartAsync(...)

    RAT->>BTT: new BybitTrailingTrader(Bybit)
    RAT->>Gate: SubscribeToOrderUpdatesAsync(HandleSellOrderUpdate)
    
    RAT->>BTT: StartAsync(...)
    BTT->>Bybit: SubscribeToOrderUpdatesAsync()
    Bybit->>BybitPrivate: subscribe("order")
    BTT->>Bybit: SubscribeToOrderBookAsync()
    Bybit->>BybitPublic: subscribe("orderbook...")

    loop Трейлинг на Bybit
        BybitPublic-->>Bybit: OrderBook Update
        Bybit->>BTT: onOrderBookUpdate
        BTT->>BTT: CalculateTargetPriceForBuy()
        alt Ордер не существует
            BTT->>Bybit: PlaceLimitOrderAsync()
            Bybit->>BybitTrade: order.create(...)
        else Ордер существует и цена изменилась
            BTT->>Bybit: ModifyOrderAsync()
            Bybit->>BybitTrade: order.amend(...)
        end
    end

    BybitPrivate-->>Bybit: Order Update: FILLED
    Bybit->>BTT: onOrderUpdate
    BTT->>RAT: OnOrderFilled(filledOrder)

    RAT->>RAT: HandleBuyOrderFilled(filledOrder)
    RAT->>Gate: PlaceOrderAsync(MARKET SELL)
    Gate-->>RAT: Sell OrderId

    Gate-->>RAT: Order Update: FILLED
    RAT->>RAT: HandleSellOrderUpdate(sellOrder)
    
    RAT->>AT: Рассчитать End-to-End Latency
    RAT->>AT: CleanupAndSignalCompletionAsync()
    RAT->>Host: arbitrageCycleTcs.SetResult(true)
    Host->>Host: Вывод в консоль: "Full process ... finished"