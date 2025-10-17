"""Output handler for arbitrage opportunities."""
import orjson
from pathlib import Path
from datetime import datetime
from typing import List
from .types import ArbitrageOpportunity


class OutputHandler:
    """Handles output of arbitrage opportunities to file and console."""

    def __init__(self, output_file: str, console_output: bool = True):
        self.output_file = Path(output_file)
        self.console_output = console_output
        self._ensure_file_exists()

    def _ensure_file_exists(self) -> None:
        """Create output file if it doesn't exist."""
        if not self.output_file.exists():
            self.output_file.touch()

    def write(self, opportunities: List[ArbitrageOpportunity]) -> None:
        """Write opportunities to file and optionally console."""
        if not opportunities:
            return

        # Write to file (JSONL format)
        with open(self.output_file, 'ab') as f:
            for opp in opportunities:
                # Add ISO timestamp
                output_data = {
                    'timestamp_iso': datetime.utcfromtimestamp(opp['timestamp']).isoformat() + 'Z',
                    **opp
                }
                json_line = orjson.dumps(output_data) + b'\n'
                f.write(json_line)

        # Console output
        if self.console_output:
            for opp in opportunities:
                self._print_opportunity(opp)

    def _print_opportunity(self, opp: ArbitrageOpportunity) -> None:
        """Print opportunity to console in readable format."""
        time_str = datetime.utcfromtimestamp(opp['timestamp']).strftime('%H:%M:%S')
        print(f"\n{'='*80}")
        print(f"[{time_str}] ARBITRAGE OPPORTUNITY: {opp['symbol']}")
        print(f"{'='*80}")
        print(f"  BUY  on {opp['buy_exchange']:10} @ {opp['buy_price']:.8f} (spread: {opp['buy_spread_pct']:.2f}%)")
        print(f"  SELL on {opp['sell_exchange']:10} @ {opp['sell_price']:.8f} (spread: {opp['sell_spread_pct']:.2f}%)")
        print(f"  PROFIT: {opp['profit_pct']:.2f}%")
        print(f"{'='*80}")

    def get_stats(self) -> dict:
        """Get statistics about found opportunities."""
        if not self.output_file.exists():
            return {'total_opportunities': 0}

        count = 0
        with open(self.output_file, 'rb') as f:
            for _ in f:
                count += 1

        return {
            'total_opportunities': count,
            'output_file': str(self.output_file)
        }
