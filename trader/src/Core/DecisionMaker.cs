using System;

namespace TraderBot.Core
{
    public class DecisionMaker
    {
        private bool _isCycleInProgress = false;

        public void Subscribe(SpreadListener listener)
        {
            listener.OnProfitableSpreadDetected += HandleProfitableSpread;
        }

        private void HandleProfitableSpread(string direction, decimal spread)
        {
            if (_isCycleInProgress)
            {
                FileLogger.LogOther($"[DecisionMaker] Profitable spread detected ({direction}: {spread:F2}%), but a cycle is already in progress. Ignoring.");
                return;
            }

            _isCycleInProgress = true;
            FileLogger.LogOther($"[DecisionMaker] Profitable spread detected! Direction: {direction}, Spread: {spread:F2}%. Starting arbitrage cycle...");

            // TODO:
            // 1. Get Orchestrator/Traders via DI
            // 2. Start the correct trader based on 'direction'
            // 3. Wait for the cycle to complete and set _isCycleInProgress = false;
        }
    }
}