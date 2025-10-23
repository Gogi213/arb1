using System.Linq;
using System.Threading.Tasks;
using TraderBot.Core;
using TraderBot.Exchanges.Bybit;

namespace TraderBot.Host
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var exchange = new BybitExchange();
            await exchange.InitializeAsync(
                "UVSbRqLBEY30RnPaiH",
                "Fg45sn0nH4FhqZaxctj54Nf9cO03Qf9s0zds"
            );

            ITrader trader = new TrailingTrader(exchange);
            await trader.StartAsync("RECALLUSDT", 3.1m, 2);
        }
    }
}
