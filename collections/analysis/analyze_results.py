"""Quick analysis of opportunities_simple.parquet"""
import polars as pl
from pathlib import Path

df = pl.read_parquet("analysis/opportunities_simple.parquet")

output = []
output.append("=== OVERVIEW ===")
output.append(f"Total opportunities: {len(df):,}")
output.append(f"\nColumns: {df.columns}")

output.append(f"\n=== BASIC STATS ===")
output.append(f"Profit range: {df['potential_profit_pct'].min():.3f}% - {df['potential_profit_pct'].max():.3f}%")
output.append(f"Mean profit: {df['potential_profit_pct'].mean():.3f}%")
output.append(f"Median profit: {df['potential_profit_pct'].median():.3f}%")

output.append(f"\n=== BY LEADER ===")
leader_stats = df.group_by("leader").agg([
    pl.len().alias("count"),
    pl.col("potential_profit_pct").mean().alias("avg_profit"),
    pl.col("potential_profit_pct").max().alias("max_profit")
]).sort("count", descending=True)
output.append(str(leader_stats))

output.append(f"\n=== BY SYMBOL (Top 10) ===")
symbol_stats = df.group_by("symbol").agg([
    pl.len().alias("count"),
    pl.col("potential_profit_pct").mean().alias("avg_profit"),
    pl.col("potential_profit_pct").max().alias("max_profit")
]).sort("count", descending=True).head(10)
output.append(str(symbol_stats))

output.append(f"\n=== TOP 20 BEST ===")
top20 = df.sort("potential_profit_pct", descending=True).head(20)
output.append(str(top20.select(["symbol", "leader", "follower", "potential_profit_pct"])))

output.append(f"\n=== PROFIT DISTRIBUTION ===")
bins = [0.1, 0.2, 0.3, 0.5, 1.0, 2.0, 100.0]
for i in range(len(bins)-1):
    count = len(df.filter(
        (pl.col("potential_profit_pct") >= bins[i]) &
        (pl.col("potential_profit_pct") < bins[i+1])
    ))
    output.append(f"{bins[i]:.1f}% - {bins[i+1]:.1f}%: {count:,} opportunities")

output.append(f"\n=== SAMPLE DATA ===")
output.append(str(df.head(5)))

# Save to file
with open("analysis/RESULTS.txt", "w", encoding="utf-8") as f:
    f.write("\n".join(output))

print("Analysis saved to analysis/RESULTS.txt")

# Also print key metrics
print("\n=== KEY FINDINGS ===")
print(f"Total: {len(df):,} opportunities")
print(f"Avg profit: {df['potential_profit_pct'].mean():.3f}%")
print(f"Median: {df['potential_profit_pct'].median():.3f}%")
print(f"Best: {df['potential_profit_pct'].max():.3f}%")
print(f"\nLeader breakdown:")
for row in leader_stats.iter_rows():
    print(f"  {row[0]}: {row[1]:,} opportunities, avg {row[2]:.3f}%")
