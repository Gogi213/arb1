# Build and run Performance Monitor
Write-Host "Building Performance Monitor..." -ForegroundColor Cyan
dotnet build tools/PerformanceMonitor/PerformanceMonitor.csproj

Write-Host "`nStarting Performance Monitor..." -ForegroundColor Green
Write-Host "Press Ctrl+C to stop`n" -ForegroundColor Yellow

dotnet run --project tools/PerformanceMonitor/PerformanceMonitor.csproj
