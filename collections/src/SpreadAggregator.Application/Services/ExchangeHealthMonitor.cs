using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SpreadAggregator.Application.Services;

/// <summary>
/// Task 0.5: Monitors exchange connection health via heartbeats
/// Prevents silent data gaps by detecting disconnections
/// </summary>
public interface IExchangeHealthMonitor
{
    void ReportHeartbeat(string exchange);
    ExchangeHealth GetHealth(string exchange);
    IReadOnlyDictionary<string, ExchangeHealth> GetAllHealth();
}

public enum ExchangeHealth { Healthy, Degraded, Dead }

public class ExchangeHealthMonitor : IExchangeHealthMonitor, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new();
    private readonly Timer _healthCheckTimer;
    private readonly ILogger<ExchangeHealthMonitor> _logger;
    private const int TimeoutSeconds = 30;

    public ExchangeHealthMonitor(ILogger<ExchangeHealthMonitor> logger)
    {
        _logger = logger;
        _healthCheckTimer = new Timer(CheckHealth, null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public void ReportHeartbeat(string exchange)
    {
        _lastHeartbeat[exchange] = DateTime.UtcNow;
    }

    public ExchangeHealth GetHealth(string exchange)
    {
        if (!_lastHeartbeat.TryGetValue(exchange, out var lastSeen))
            return ExchangeHealth.Dead;

        var age = DateTime.UtcNow - lastSeen;
        if (age.TotalSeconds < TimeoutSeconds) return ExchangeHealth.Healthy;
        if (age.TotalSeconds < TimeoutSeconds * 2) return ExchangeHealth.Degraded;
        return ExchangeHealth.Dead;
    }

    public IReadOnlyDictionary<string, ExchangeHealth> GetAllHealth()
    {
        return _lastHeartbeat.Keys.ToDictionary(
            exchange => exchange,
            exchange => GetHealth(exchange)
        );
    }

    private void CheckHealth(object? state)
    {
        foreach (var (exchange, health) in GetAllHealth())
        {
            if (health != ExchangeHealth.Healthy)
            {
                _logger.LogWarning("Exchange {Exchange} is {Health}", exchange, health);
                // TODO: Trigger reconnect in OrchestrationService
            }
        }
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
    }
}
