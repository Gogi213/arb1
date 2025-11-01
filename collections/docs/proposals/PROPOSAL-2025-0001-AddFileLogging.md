# PROPOSAL-2025-0001: Add File Logging

## 1. Compact Diagnostic
The application currently does not write logs to a persistent file. The file `logs/logs.txt` exists but is always empty. All log output is directed to the console, which makes it impossible to analyze application behavior after it has run, especially for diagnosing complex asynchronous issues. The user reported "some kind of chaos happened" but no logs are available to investigate.

## 2. Proposed Change
I propose to add a file logging provider to the application. This will be done by modifying the `.ConfigureLogging()` section in `src/SpreadAggregator.Presentation/Program.cs`.

I will use the built-in `AddFile` extension method available through a nuget package, which is a simple and effective way to achieve this.

**File to be modified:** `src/SpreadAggregator.Presentation/Program.cs`

**Diff:**
```diff
--- a/src/SpreadAggregator.Presentation/Program.cs
+++ b/src/SpreadAggregator.Presentation/Program.cs
@@ -38,11 +38,16 @@
                      .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);
              })
              .ConfigureLogging(logging =>
              {
+                 var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "logs.txt");
+ 
                  logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                  logging.AddFilter("BingX", LogLevel.Warning);
                  logging.AddFilter("Bybit", LogLevel.Debug);
+ 
+                 logging.AddFile(logPath, minimumLevel: LogLevel.Debug, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
              })
              .ConfigureServices((context, services) =>
              {

```

## 3. Rationale
This change is critical for system observability. Persistent logs are the primary source of truth for diagnosing bugs, race conditions, and other transient issues in a distributed HFT environment. Without them, we are effectively blind to any problems that are not immediately reproducible in a live debugging session.

## 4. Risk Assessment
- **Risk:** Low. The change only adds a logging sink. It does not alter any core application logic.
- **Mitigation:** The file path is constructed to be relative to the application's base directory, ensuring it works in different environments. The logging level is set to `Debug` to capture sufficient detail without being overly verbose in production unless needed.

## 5. Testing Plan
1. Approve and apply the change.
2. Run the application: `dotnet run --project src/SpreadAggregator.Presentation/SpreadAggregator.Presentation.csproj`
3. After a minute, stop the application.
4. Verify that the `logs/logs.txt` file is no longer empty and contains log entries from the application startup and operation.

## 6. Rollback Steps
1. Revert the changes in `src/SpreadAggregator.Presentation/Program.cs` by removing the added lines.
2. If a package was added, remove the package reference from `src/SpreadAggregator.Presentation/SpreadAggregator.Presentation.csproj`.