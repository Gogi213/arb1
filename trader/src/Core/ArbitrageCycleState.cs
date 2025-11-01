namespace TraderBot.Core
{
    public class ArbitrageCycleState
    {
        public decimal Leg1GateBuyFilledQuantity { get; set; }
        public decimal GateIoLeg1BuyQuantity { get; set; }

        /// <summary>
        /// The initial balance of the base asset on Gate.io before the cycle starts.
        /// Used for the final rebalancing sell in Leg 2.
        /// </summary>
        public decimal InitialGateIoBaseAssetBalance { get; set; }
    }
}