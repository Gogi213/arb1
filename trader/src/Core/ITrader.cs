using System.Threading.Tasks;

namespace TraderBot.Core
{
    public interface ITrader
    {
        Task StartAsync(string symbol, decimal amount, int durationMinutes);
    }
}