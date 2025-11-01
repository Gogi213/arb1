# PROPOSAL-2025-0003: Fix WebSocket Race Condition (Simple)

**This proposal supersedes and simplifies `PROPOSAL-2025-0002` based on KISS/YAGNI principles.**

## 1. Compact Diagnostic
The application logs are flooded with `System.ObjectDisposedException` from the `FleckWebSocketServer`. This is caused by a race condition where a client disconnects between the moment we check if the socket is available and the moment we actually send data. This is expected behavior in a high-traffic async environment, but the resulting exceptions are creating log noise.

## 2. Proposed Change (KISS)
I propose the simplest possible fix: iterate through a snapshot of the sockets and wrap each `Send` operation in a `try-catch` block. This directly prevents the exception from being logged, solving the problem without adding unnecessary complexity.

**File to be modified:** `src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs`

**Diff:**
```diff
--- a/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs
+++ b/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs
@@ -46,11 +46,26 @@
  
      public Task BroadcastRealtimeAsync(string message)
      {
+         List<IWebSocketConnection> socketsSnapshot;
          lock (_lock)
          {
-             var tasks = _allSockets.Where(s => s.IsAvailable).Select(s => s.Send(message));
-             return Task.WhenAll(tasks);
+            // Take a snapshot to avoid holding the lock during I/O operations
+            socketsSnapshot = _allSockets.ToList();
          }
+ 
+         var tasks = new List<Task>();
+         foreach (var socket in socketsSnapshot)
+         {
+             try
+             {
+                 if (socket.IsAvailable)
+                     tasks.Add(socket.Send(message));
+             }
+             catch (ObjectDisposedException)
+             {
+                 // Expected exception if socket closed during broadcast. Ignore.
+             }
+         }
+         return Task.WhenAll(tasks);
      }
  
      public void Dispose()

```

## 3. Rationale
This approach is the most direct way to solve the problem. It acknowledges that `ObjectDisposedException` is a normal, expected event in this context and simply suppresses it. It avoids more complex async patterns, adhering strictly to the "Keep It Simple, Stupid" principle.

## 4. Risk Assessment
- **Risk:** Extremely Low. The change is a local, defensive programming pattern that only affects error handling for a specific, known exception. It makes the system more robust.

## 5. Testing Plan
1. Approve and apply the change.
2. Run the application and connect/disconnect WebSocket clients.
3. **Expected Result:** The `ObjectDisposedException` spam in the logs will disappear.

## 6. Rollback Steps
1. Revert the `BroadcastRealtimeAsync` method in `src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs` to its original state.