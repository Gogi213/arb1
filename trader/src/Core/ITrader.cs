using System.Threading.Tasks;

namespace TraderBot.Core
{
    public interface ITrader
    {
        Task<bool> StartAsync(string symbol, decimal amount, int durationMinutes);
    }
}