# PROPOSAL-2025-0002: Fix WebSocket Race Condition

## 1. Compact Diagnostic
The application logs are flooded with `System.ObjectDisposedException` originating from the `FleckWebSocketServer`. This is caused by a race condition in the `BroadcastRealtimeAsync` method. The code checks if a socket `IsAvailable` and then attempts to `Send` a message to it. However, because the `Select` statement is lazily evaluated, the client can disconnect between the check and the actual send operation. This results in an attempt to write to a disposed network stream, throwing the exception and spamming the logs.

## 2. Proposed Change
I propose to make the `BroadcastRealtimeAsync` method in `FleckWebSocketServer.cs` more robust by handling potential exceptions that occur when a client disconnects during a broadcast.

**File to be modified:** `src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs`

**Diff:**
```diff
--- a/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs
+++ b/src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs
@@ -44,12 +44,29 @@
      }
  
      public Task BroadcastRealtimeAsync(string message)
      {
-         lock (_lock)
-         {
-             var tasks = _allSockets.Where(s => s.IsAvailable).Select(s => s.Send(message));
-             return Task.WhenAll(tasks);
-         }
+        List<IWebSocketConnection> sockets;
+        lock (_lock)
+        {
+            // Create a snapshot of the list to avoid holding the lock during I/O
+            sockets = _allSockets.ToList();
+        }
+
+        var tasks = sockets.Select(async s =>
+        {
+            try
+            {
+                // Re-check IsAvailable right before sending
+                if (s.IsAvailable)
+                {
+                    await s.Send(message);
+                }
+            }
+            catch (ObjectDisposedException)
+            {
+                // This is expected if the socket was closed between the snapshot and the send. Ignore to prevent log spam.
+            }
+        });
+
+        return Task.WhenAll(tasks);
      }
  
      public void Dispose()

```

## 3. Rationale
The current implementation is not thread-safe against client disconnections, leading to log flooding and potential instability. The proposed change introduces a `try-catch` block to gracefully handle `ObjectDisposedException`. This is a standard pattern for handling scenarios where a resource can be disposed of by another thread. This will make the WebSocket server resilient and stop the log spam, allowing for meaningful analysis of other potential issues.

## 4. Risk Assessment
- **Risk:** Very Low. The change is localized to error handling within the WebSocket server and does not affect any core business logic. It makes the system more stable.
- **Mitigation:** The change is self-contained. The `catch` block specifically targets the expected `ObjectDisposedException`, so other, unexpected errors will still surface.

## 5. Testing Plan
1. Approve and apply the change.
2. Run the application.
3. Connect one or more WebSocket clients.
4. Repeatedly connect and disconnect clients while the application is running.
5. Monitor the `logs/logs.txt` file and the console output.
6. **Expected Result:** The `ObjectDisposedException` spam should be gone. The server should continue to operate normally without crashing or flooding the logs.

## 6. Rollback Steps
1. Revert the changes in `src/SpreadAggregator.Infrastructure/Services/FleckWebSocketServer.cs` to the previous version of the `BroadcastRealtimeAsync` method.