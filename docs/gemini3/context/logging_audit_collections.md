# Performance Impact Assessment: Logging & I/O в Collections

## 🎯 ЦЕЛ

Ь: Найти все источники фризов из-за логов/I/O

---

## 📋 ПОЛНЫЙ СПИСОК (место/что делает/зачем)

### 🔴 КРИТИЧЕСКИЕ (HOT PATH - вызываются тысячи раз/сек)

#### 1. OrchestrationService.cs
**Локация:** `Application/Services/OrchestrationService.cs`

| Строка | Что делает | Частота | Зачем надо | Действие |
|--------|------------|---------|------------|----------|
| 127 | `Console.WriteLine` - No client found | Rare (error) | Debug | ✅ KEEP (error only) |
| 140 | `Console.WriteLine` - Exchange stopped | Rare | Info | ✅ KEEP |
| 144-145 | `Console.WriteLine` - FATAL error | Rare | Critical | ✅ KEEP |
| 160 | `Console.WriteLine` - Started tasks | Once | Info | ✅ KEEP |
| 182 | `Console.WriteLine` - Received tickers | Per exchange startup | Info | ✅ KEEP |
| 194 | `Console.WriteLine` - Symbols passed filter | Per exchange startup | Info | ✅ KEEP |
| 198 | `Console.WriteLine` - Adding ticker subscription | Per exchange | Info | ✅ KEEP |
| 202 | `Console.WriteLine` - Adding trade subscription | Per exchange | Info | ✅ KEEP |
| 205 | `Console.WriteLine` - Awaiting tasks | Per exchange | Info | ✅ KEEP |
| 209 | `Console.WriteLine` - All tasks completed | Per exchange | Info | ✅ KEEP |
| 275 | `Console.WriteLine` - Orchestration-WARN channel full | **HOT PATH** ~2500/sec | **Channel overflow warning** | ⚠️ **REDUCE** (rate limit!) |
| 279 | `Console.WriteLine` - Rolling window channel full | **HOT PATH** ~2500/sec | **Channel overflow warning** | ⚠️ **REDUCE** (rate limit!) |

**VERDICT:** Lines 275, 279 - в HOT PATH (ProcessExchange callback)! При overflow может вызывать 2500 Console.WriteLine/sec!

---

#### 2. ParquetDataWriter.cs
**Локация:** `Infrastructure/Services/ParquetDataWriter.cs`

| Строка | Что делает | Частота | Зачем надо | Действие |
|--------|------------|---------|------------|----------|
| 166 | `Console.WriteLine` - Starting to record | Once | Info | ✅ KEEP |
| 222 | `// Console.WriteLine` - Wrote spread records | Commented | N/A | ✅ OK (commented) |
| 224 | `Console.WriteLine` - ERROR | On error | Critical | ✅ KEEP |
| 253 | `// Console.WriteLine` - Wrote trade records | Commented | N/A | ✅ OK |
| 255 | `Console.WriteLine` - ERROR | On error | Critical | ✅ KEEP |
| 264 | `Console.WriteLine` - Error processing data | On error | Critical | ✅ KEEP |
| 279 | `Console.WriteLine` - Wrote spread records | **Per flush** ~1/min | Info | ⚠️ **OPTIONAL** (можно убрать) |
| 287 | `Console.WriteLine` - Wrote trade records | **Per flush** ~1/min | Info | ⚠️ **OPTIONAL** |
| 337 | `Console.WriteLine` - Flushed buffers | **Per flush** ~1/min | Info | ⚠️ **OPTIONAL** |

**VERDICT:** Print на каждый flush (раз в минуту) - не критично, но можно убрать для cleaner output.

---

#### 3. FleckWebSocketServer.cs
**Локация:** `Infrastructure/Services/FleckWebSocketServer.cs`

| Строка | Что делает | Частота | Зачем надо | Действие |
|--------|------------|---------|------------|----------|
| 39 | `Console.WriteLine` - Client connected | Per client | Info | ✅ KEEP |
| 55 | `Console.WriteLine` - Client disconnected | Per client | Info | ✅ KEEP |
| 81 | `Console.WriteLine` - Error sending to socket | **HOT PATH?** (если client slow) | Error | ⚠️ **RISK** (может спамить!) |
| 107 | `Console.WriteLine` - Cleaned up dead connections | Per cleanup (rare) | Info | ✅ KEEP |

**VERDICT:** Line 81 - если clients медленные или disconnected, может вызываться при КАЖДОМ BroadcastRealtimeAsync (2500/sec)!

---

#### 4. SignalDetector.cs
**Локация:** `Application/Services/SignalDetector.cs`

| Строка | Что делает | Частота | Зачем надо | Действие |
|--------|------------|---------|------------|----------|
| 147 | `Console.WriteLine` - Expired signal | Per expired signal | Info | ⚠️ **HOT PATH potential** (если много signals expire) |

**VERDICT:** CleanupExpiredSignals сейчас закомментирован из hot path, но если вернём - будет вызываться при каждом ProcessDeviation!

---

### 🟡 СРЕДНИЙ ПРИОРИТЕТ (вызываются часто, но не в критическом пути)

#### 5. RealTimeController.cs
**Локация:** `Presentation/Controllers/RealTimeController.cs`

| Строка | Что делает | Частота | Зачем надо | Действие |
|--------|------------|---------|------------|----------|
| 48 | `_logger.LogInformation` - WebSocket connection established | Per client | Info | ✅ KEEP |
| 63 | `_logger.LogInformation` - Starting streaming | Per client | Info | ✅ KEEP |
| 69 | `_logger.LogInformation` - Opportunity details | **Per opportunity** (×10) | Info | ⚠️ **SPAM** (10+ lines per client!) |
| 73 | `_logger.LogInformation` - ... and N more | Per client | Info | ✅ KEEP |
| 134 | `// _logger.LogDebug` - Event-driven update | Commented | N/A | ✅ OK |
| 153 | `_logger.LogInformation` - Subscribed to window | **Per opportunity** (~100) | Info | 🔴 **SPAM!** (100 lines per client!) |
| 159 | `_logger.LogInformation` - RollingWindow has data | **Per opportunity** | Info | 🔴 **SPAM!** |
| 163 | `_logger.LogWarning` - RollingWindow NO data | **Per opportunity** | Warn | 🔴 **SPAM!** |
| 175 | `_logger.LogWarning` - Connection error | On error | Warn | ✅ KEEP |
| 179 | `_logger.LogError` - Error in streaming | On error | Error | ✅ KEEP |
| 189 | `_logger.LogInformation` - Unsubscribed | Per client | Info | ✅ KEEP |
| 203 | `_logger.LogInformation` - Connection closed | Per client | Info | ✅ KEEP |

**VERDICT:** Lines 153, 159, 163 - **SPAM 100+ lines** при каждом WebSocket connection! Не критично для performance (ILogger async), но захламляет логи.

---

#### 6. DashboardController.cs
**Локация:** `Presentation/Controllers/DashboardController.cs`

| Строка | Что делает | Частота | Зачем надо | Действие |
|--------|------------|---------|------------|----------|
| 38 | `_logger.LogInformation` - Request received | Per API call | Info | ✅ KEEP |
| 42 | `_logger.LogInformation` - Streaming data | Per API call | Info | ✅ KEEP |
| 59 | `_logger.LogError` - Error processing pair | On error | Error | ✅ KEEP |
| 78 | `_logger.LogInformation` - Finished streaming | Per API call | Info | ✅ KEEP |

**VERDICT:** OK, не в hot path.

---

#### 7. WebSocketLogger.cs
**Локация:** `Infrastructure/Services/WebSocketLogger.cs`

| Строка | Что делает | Частота | Зачем надо | Действие |
|--------|------------|---------|------------|----------|
| 29 | `Console.WriteLine` - Failed to create log directory | On error | Critical | ✅ KEEP |
| 49 | `Console.WriteLine` - Failed to write to log | On error | Error | ✅ KEEP |

**VERDICT:** Error handling only - OK.

---

### 🟢 НИЗКИЙ ПРИОРИТЕТ (редкие вызовы или background tasks)

#### 8. ParquetReaderService.cs
**Локация:** `Infrastructure/Services/Charts/ParquetReaderService.cs`

All `_logger.Log*` calls - API endpoints only, не в hot path. ✅ OK

#### 9. OpportunityFilterService.cs
**Локация:** `Infrastructure/Services/Charts/OpportunityFilterService.cs`

All `_logger.Log*` calls - startup/API only. ✅ OK

#### 10. BidAskLogger.cs / BidBidLogger.cs
**Локация:** `Infrastructure/Services/`

Async logging через channels - ✅ OK (не блокирует hot path)

#### 11. RollingWindowService.cs
**Локация:** `Application/Services/RollingWindowService.cs`

| Строка | Что делает | Частота | Зачем надо | Действие |
|--------|------------|---------|------------|----------|
| 542, 547 | `Stopwatch.GetTimestamp()` | **HOT PATH** ~2500/sec | **Profiling?** | 🔴 **REMOVE** (overhead!) |
| 294 | `// _logger.LogDebug` - Window event | Commented | N/A | ✅ OK |
| 345 | `_logger.LogDebug` - Subscribed | Debug (disabled in prod) | Debug | ✅ OK |
| 358 | `_logger.LogDebug` - Unsubscribed | Debug | Debug | ✅ OK |
| 382 | `_logger.LogInformation` - Cleanup | Periodic | Info | ✅ OK |

**VERDICT:** **Stopwatch calls в hot path! Зачем??** Нужно проверить.

---

## 🎯 ПРИОРИТЕТНЫЙ СПИСОК ДЛЯ ИСПРАВЛЕНИЯ

### 🔴 CRITICAL (могут вызывать фризы)

1. **OrchestrationService.cs:275, 279**
   - `Console.WriteLine` при channel overflow
   - **Частота:** До 2500/sec при перегрузке
   - **Fix:** Rate-limit или убрать совсем
   - **Зачем:** Предупреждение об overflow

2. **FleckWebSocketServer.cs:81**
   - `Console.WriteLine` при ошибке отправки
   - **Частота:** До 2500/sec если client disconnected
   - **Fix:** Rate-limit или async log
   - **Зачем:** Error reporting

3. **RollingWindowService.cs:542, 547**
   - `Stopwatch.GetTimestamp()` в hot path
   - **Частота:** ~2500/sec
   - **Fix:** Удалить (или только в DEBUG)
   - **Зачем:** Profiling??? (непонятно)

### 🟡 MEDIUM (не критично, но спамят)

4. **RealTimeController.cs:153, 159, 163**
   - `_logger.LogInformation` для каждой opportunity
   - **Частота:** 100+ lines per WebSocket client
   - **Fix:** Убрать или LogDebug
   - **Зачем:** Debugging subscriptions

5. **ParquetDataWriter.cs:279, 287, 337**
   - `Console.WriteLine` на каждый flush
   - **Частота:** ~1/min
   - **Fix:** Убрать или только errors
   - **Зачем:** Progress reporting

---

## 📊 СТАТИСТИКА

**TOTAL Console.WriteLine:** 50+ вызовов  
**TOTAL ILogger calls:** 60+ вызовов  
**В HOT PATH (>100/sec):** 5 calls  
**CRITICAL (могут вызывать freeze):** 3 sources

---

## ⚡ РЕКОМЕНДАЦИИ

1. ✅ **Убрать Stopwatch из RollingWindowService** - зачем profiling в production?
2. ✅ **Rate-limit Console.WriteLine в OrchestrationService** - при overflow не спамить
3. ✅ **Rate-limit FleckWebSocketServer errors** - если client disconnected
4. ⚠️ **Reduce logging в RealTimeController** - 100 lines per client это много
5. ⚠️ **Optional: убрать ParquetDataWriter progress logs** - cleaner output
