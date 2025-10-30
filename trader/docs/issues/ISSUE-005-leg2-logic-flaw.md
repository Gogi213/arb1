# ISSUE-005: Leg 2 Fails Due to Incorrect Amount & Cross-Exchange Logic Flaw

## 1. Compact Diagnostic

During the execution on `2025-10-30`, the full arbitrage cycle failed during `Leg 2`. The investigation of `other.txt` revealed two distinct problems:

1.  **Orchestrator Bug:** The final USDT balance from `Leg 1` (`10.53 USDT`) was not passed as the input amount for `Leg 2`. Instead, `Leg 2` was initiated with a hardcoded default value of `6 USDT`.
2.  **Fundamental Logic Flaw:** After `Leg 2` successfully purchased asset `H` on Bybit, it immediately attempted to sell that same quantity on Gate.io. This operation failed with `FAILED to place market sell order` because the assets physically exist on Bybit and cannot be sold on Gate.io without an explicit (and currently non-existent) transfer operation.

## 2. Log Evidence

### Orchestrator Bug:
- `[01:05:27.394] [Arbitrage] Final USDT balance received: 10,53539497` (End of Leg 1)
- `[01:05:28.891] [BybitTrailing] Starting for HUSDT, amount=$6, depth=$25` (Start of Leg 2)

### Cross-Exchange Logic Flaw:
- `[01:06:12.118] [!!!] Bybit order 2072408369909730304 was FILLED!` (Buy on Bybit)
- `[01:06:12.285] [Y4] Balance update received. Actual available quantity: 22,961525` (Asset `H` is on Bybit)
- `[01:06:12.287] [Y5] Immediately selling 22,96 on GateIoExchange...` (Attempting to sell on the wrong exchange)
- `[01:06:12.623] [Y5] FAILED to place market sell order on GateIoExchange.` (Failure due to lack of assets on Gate.io)

## 3. Hypothesis

The system cannot function as a "closed loop" across two different exchanges without a mechanism to transfer assets between them. The current implementation attempts to sell assets on an exchange where it does not hold them, leading to a predictable failure.

The user's specified logic flow is ambiguous in its final step and needs clarification.

## 4. Next Steps

1.  Propose a fix for the orchestrator bug in `Program.cs`.
2.  In the same proposal, raise the issue of the logical flaw and ask for clarification on how to proceed, as fixing the orchestrator bug alone will not resolve the entire issue. The sell on Gate.io will still fail.