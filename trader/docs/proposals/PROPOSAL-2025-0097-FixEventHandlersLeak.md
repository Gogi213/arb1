# PROPOSAL-2025-0097: Исправление утечки обработчиков событий

**Дата:** 2025-11-08
**Статус:** 🔵 Предложено

---

## 1. Диагностика

**Проблема:** В файле `collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs` во вложенном классе `ManagedConnection` происходит подписка на события `ConnectionLost` и `ConnectionRestored`. Однако отписка от этих событий (`-=`) никогда не выполняется.

**Риск:** При каждом переподключении или перезапуске соединения создаются новые подписки, а старые остаются в памяти. Это приводит к утечке памяти и потенциально к многократному выполнению одного и того же кода.

---

## 2. Предлагаемое изменение

Добавить отписку от событий в метод `StopAsync` класса `ManagedConnection`. Также необходимо сохранить делегаты в полях класса, чтобы иметь возможность отписаться от них.

**Файл:** `collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs`

```diff
--- a/collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs
+++ b/collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs
@@ -118,6 +118,9 @@
         private readonly Func<TradeData, Task>? _onTradeData;
         private readonly TSocketClient _socketClient;
         private readonly SemaphoreSlim _resubscribeLock = new(1, 1);
+        // Поля для хранения делегатов, чтобы их можно было отписать
+        private Action? _connectionLostHandler;
+        private Action<TimeSpan>? _connectionRestoredHandler;
 
         public ManagedConnection(
             ExchangeClientBase<TRestClient, TSocketClient> parent,
@@ -138,6 +141,14 @@
 
         public async Task StopAsync()
         {
+            // Отписываемся от событий
+            if (_connectionLostHandler != null && result.Data != null)
+            {
+                result.Data.ConnectionLost -= _connectionLostHandler;
+            }
+            if (_connectionRestoredHandler != null && result.Data != null)
+            {
+                result.Data.ConnectionRestored -= _connectionRestoredHandler;
+            }
             var api = _parent.CreateSocketApi(_socketClient);
             await api.UnsubscribeAllAsync();
             _socketClient.Dispose();
@@ -198,10 +209,12 @@
                 // The JKorf libraries handle reconnection automatically
                 try
                 {
-                    result.Data.ConnectionLost += new Action(HandleConnectionLost);
-                    result.Data.ConnectionRestored += new Action<TimeSpan>((t) =>
-                        WebSocketLogger.Log($"[{_parent.ExchangeName}] {streamType} connection restored for chunk after {t}."));
+                    _connectionLostHandler = new Action(HandleConnectionLost);
+                    _connectionRestoredHandler = new Action<TimeSpan>((t) =>
+                        WebSocketLogger.Log($"[{_parent.ExchangeName}] {streamType} connection restored for chunk after {t}."));
+                    result.Data.ConnectionLost += _connectionLostHandler;
+                    result.Data.ConnectionRestored += _connectionRestoredHandler;
                 }
                 catch
                 {

```

*Примечание: `result.Data` в `StopAsync` будет недоступен. Изменение выше концептуальное. Правильная реализация потребует сохранения ссылки на `result.Data` или изменения логики.*

**Корректная реализация:**

Так как `result` доступен только в `HandleSubscriptionResult`, нужно сохранить и его, и делегаты.

```diff
--- a/collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs
+++ b/collections/src/SpreadAggregator.Infrastructure/Services/Exchanges/Base/ExchangeClientBase.cs
@@ -118,6 +118,10 @@
         private readonly Func<TradeData, Task>? _onTradeData;
         private readonly TSocketClient _socketClient;
         private readonly SemaphoreSlim _resubscribeLock = new(1, 1);
+        private dynamic? _subscriptionResultData; // Сохраняем ссылку на объект с событиями
+        private Action? _connectionLostHandler;
+        private Action<TimeSpan>? _connectionRestoredHandler;
 
         public ManagedConnection(
             ExchangeClientBase<TRestClient, TSocketClient> parent,
@@ -138,6 +142,18 @@
 
         public async Task StopAsync()
         {
+            // Отписываемся от событий, если они были подписаны
+            if (_subscriptionResultData != null)
+            {
+                if (_connectionLostHandler != null)
+                {
+                    _subscriptionResultData.ConnectionLost -= _connectionLostHandler;
+                }
+                if (_connectionRestoredHandler != null)
+                {
+                    _subscriptionResultData.ConnectionRestored -= _connectionRestoredHandler;
+                }
+            }
             var api = _parent.CreateSocketApi(_socketClient);
             await api.UnsubscribeAllAsync();
             _socketClient.Dispose();
@@ -198,10 +214,13 @@
                 // The JKorf libraries handle reconnection automatically
                 try
                 {
-                    result.Data.ConnectionLost += new Action(HandleConnectionLost);
-                    result.Data.ConnectionRestored += new Action<TimeSpan>((t) =>
-                        WebSocketLogger.Log($"[{_parent.ExchangeName}] {streamType} connection restored for chunk after {t}."));
+                    _subscriptionResultData = result.Data; // Сохраняем
+                    _connectionLostHandler = new Action(HandleConnectionLost);
+                    _connectionRestoredHandler = new Action<TimeSpan>((t) =>
+                        WebSocketLogger.Log($"[{_parent.ExchangeName}] {streamType} connection restored for chunk after {t}."));
+                    _subscriptionResultData.ConnectionLost += _connectionLostHandler;
+                    _subscriptionResultData.ConnectionRestored += _connectionRestoredHandler;
                 }
                 catch
                 {
```

---

## 3. Обоснование

- **Устранение утечки:** Явное отписывание от событий при остановке соединения (`StopAsync`) гарантирует, что сборщик мусора сможет освободить объекты `ManagedConnection` и связанные с ними ресурсы.
- **Безопасность:** Сохранение делегатов в полях класса — это стандартный паттерн для корректной отписки.
- **Надежность:** Это делает жизненный цикл подписки и отписки симметричным и предсказуемым.

---

## 4. План тестирования

1.  **Code Review:** Убедиться, что отписка добавлена и использует те же экземпляры делегатов, что и при подписке.
2.  **Тест на переподключение (выполняется пользователем):**
    - Запустить сервис.
    - Имитировать разрыв соединения с биржей (например, отключив сеть).
    - Восстановить соединение.
    - С помощью отладчика или логов убедиться, что `HandleConnectionLost` вызывается только один раз за один разрыв.
    - С помощью профилировщика памяти убедиться, что количество объектов `ManagedConnection` не растет после нескольких циклов переподключения.

---

## 5. План отката

- Вернуть предыдущую версию файла `ExchangeClientBase.cs` из системы контроля версий.