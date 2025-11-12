using Microsoft.Data.Analysis;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;

namespace SpreadAggregator.Infrastructure.Services.Charts;

/// <summary>
/// Service for filtering arbitrage opportunities from analyzer stats
/// Replaces Python charts/server.py _get_filtered_opportunities()
/// </summary>
public class OpportunityFilterService
{
    private readonly string _analyzerStatsPath;
    private readonly ILogger<OpportunityFilterService> _logger;
    private readonly object _cacheLock = new();
    private List<Opportunity>? _cachedOpportunities;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    public OpportunityFilterService(string analyzerStatsPath, ILogger<OpportunityFilterService> logger)
    {
        _analyzerStatsPath = analyzerStatsPath ?? throw new ArgumentNullException(nameof(analyzerStatsPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get filtered opportunities from latest stats file
    /// Filters by opportunity_cycles_040bp >= 1
    /// Cached for 10 seconds
    /// </summary>
    public List<Opportunity> GetFilteredOpportunities()
    {
        // Check cache without lock (fast path)
        if (_cachedOpportunities != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedOpportunities;
        }

        // Refresh cache with lock
        lock (_cacheLock)
        {
            // Double-check after acquiring lock
            if (_cachedOpportunities != null && DateTime.UtcNow < _cacheExpiry)
            {
                return _cachedOpportunities;
            }

            _cachedOpportunities = LoadOpportunities();
            _cacheExpiry = DateTime.UtcNow + CacheLifetime;
            return _cachedOpportunities;
        }
    }

    private List<Opportunity> LoadOpportunities()
    {
        if (!Directory.Exists(_analyzerStatsPath))
        {
            _logger.LogError($"Analyzer stats directory not found: {_analyzerStatsPath}");
            return new List<Opportunity>();
        }

        var csvFiles = Directory.GetFiles(_analyzerStatsPath, "*.csv");
        if (csvFiles.Length == 0)
        {
            _logger.LogError("No summary stats CSV files found");
            return new List<Opportunity>();
        }

        // Get latest file by modification time
        var latestFile = csvFiles
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .First();

        _logger.LogDebug($"Using stats file: {latestFile.Name}");

        // Read and filter CSV
        var df = DataFrame.LoadCsv(latestFile.FullName);

        // Find opportunity_cycles_040bp column
        var oppCyclesCol = df.Columns.FirstOrDefault(c =>
            c.Name.ToLower().Contains("opportunity") && c.Name.ToLower().Contains("040bp"));

        if (oppCyclesCol == null)
        {
            _logger.LogError("Column 'opportunity_cycles_040bp' not found in CSV");
            return new List<Opportunity>();
        }

        // Filter and convert to DTOs
        var opportunities = new List<Opportunity>();

        for (long i = 0; i < df.Rows.Count; i++)
        {
            var oppCyclesValue = df.Rows[i][oppCyclesCol.Name];
            if (oppCyclesValue == null) continue;

            var oppCycles = Convert.ToInt32(oppCyclesValue);
            if (oppCycles >= 1)
            {
                // IMPORTANT: Normalize symbol here to match OrchestrationService format
                // CSV contains "VIRTUAL/USDT", but entire system uses "VIRTUAL_USDT"
                var rawSymbol = df.Rows[i]["symbol"]?.ToString() ?? "";
                var normalizedSymbol = rawSymbol.Replace("/", "_").Replace("-", "_").Replace(" ", "");

                opportunities.Add(new Opportunity
                {
                    Symbol = normalizedSymbol,  // Store normalized symbol (VIRTUAL_USDT)
                    Exchange1 = df.Rows[i]["exchange1"]?.ToString() ?? "",
                    Exchange2 = df.Rows[i]["exchange2"]?.ToString() ?? "",
                    OpportunityCycles = oppCycles
                });
            }
        }

        // Sort by symbol, exchange1
        opportunities = opportunities
            .OrderBy(o => o.Symbol)
            .ThenBy(o => o.Exchange1)
            .ToList();

        _logger.LogInformation($"Found {opportunities.Count} opportunities with cycles >= 1");

        return opportunities;
    }
}

/// <summary>
/// Opportunity DTO
/// </summary>
public class Opportunity
{
    public required string Symbol { get; set; }
    public required string Exchange1 { get; set; }
    public required string Exchange2 { get; set; }
    public int OpportunityCycles { get; set; }
}
