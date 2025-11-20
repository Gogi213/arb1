# Phase 1.5 - Live Validation Setup

## ✅ CODE COMPLETE - BUILD SUCCESS

**Status:** Ready for live test  
**Tests:** 23/23 passing ✅  
**Build:** No errors ✅

---

## 🔑 STEP 1: Add API Keys

Edit `collections/src/SpreadAggregator.Presentation/appsettings.json`

Add these sections at the end (before closing `}`):

```json
  "Gate": {
    "ApiKey": "YOUR_GATE_API_KEY_HERE",
    "SecretKey": "YOUR_GATE_SECRET_KEY_HERE"
  },
  "Bybit": {
    "ApiKey": "YOUR_BYBIT_API_KEY_HERE",
    "SecretKey": "YOUR_BYBIT_SECRET_KEY_HERE"
  },
  "Arbitrage": {
    "MinDeviationThreshold": 0.10,
    "EntryThreshold": 0.35,
    "ExitThreshold": 0.05
  }
```

---

## 🚀 STEP 2: Run Collections

```bash
cd c:\visual projects\arb1\collections\src\SpreadAggregator.Presentation
dotnet run
```

**Expected output:**

```
[DeviationCalculator] Monitoring Gate vs Bybit...
[SignalDetector] Waiting for entry signals (threshold: 0.35%)...
[WebSocket] Broadcasting spreads to ws://localhost:8181
```

---

## 📊 STEP 3: Wait for Signal

**Monitor console output for:**

```
[SignalDetector] ENTRY SIGNAL - BTC_USDT: deviation +0.40%
[TradeExecutor] ENTRY SIGNAL - BUY BTC_USDT on Gate (deviation: 0.40%)
[Gate] Placing market BUY order: BTC_USDT, amount: 6
[TradeExecutor] ✅ ORDER FILLED: 0.00012 BTC_USDT at avg price 50000
```

---

## ✅ STEP 4: Validate

**Check:**

1. Gate.io account - $6 USDT order executed?
2. Position opened?
3. No errors in console?
4. WebSocket broadcast working? (check browser ws://localhost:8181)

---

## 📝 Next Steps (After Successful Test)

1. Monitor for exit signal (deviation → 0)
2. Validate P&L
3. Mark Task 1.5 as COMPLETE
4. Phase 1 → 100% DONE! 🎉

---

**Implementation Files:**

- `Application/Services/Trading/` - exchange integration
- `Application/Services/TradeExecutor.cs` - real order execution  
- `Presentation/Program.cs` - DI wiring

**Estimate:** Wait for signal (10 min - 2h)  
**Order size:** $6 USDT (test)  
**Exit:** Manual or automatic when deviation converges
