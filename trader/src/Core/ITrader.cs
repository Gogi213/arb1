using System.Threading.Tasks;

namespace TraderBot.Core
{
    public interface ITrader
    {
        Task<decimal> StartAsync(string symbol, decimal amount, int durationMinutes, ArbitrageCycleState state);
    }
}