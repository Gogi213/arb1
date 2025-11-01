using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TraderBot.Core;
using TraderBot.Exchanges.Bybit;
using TraderBot.Exchanges.GateIo;

namespace TraderBot.Host
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // --- Spread Listener Setup ---
            var spreadListenerUrl = configuration.GetValue<string>("SpreadListenerUrl");
            if (string.IsNullOrEmpty(spreadListenerUrl))
            {
                FileLogger.LogOther("SpreadListenerUrl is not configured in appsettings.json. Exiting.");
                return;
            }
            
            var listener = new SpreadListener(spreadListenerUrl);
            var decisionMaker = new DecisionMaker();
            decisionMaker.Subscribe(listener);

            FileLogger.LogOther("Starting spread listener. Waiting for profitable spread...");
            await listener.StartAsync(CancellationToken.None); // Now we await it, as it's the main loop

            FileLogger.LogOther("Spread listener finished. Program exiting.");
        }
    }
}
