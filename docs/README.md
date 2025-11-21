# Project Documentation & Context

**⚠️ IMPORTANT: DOCUMENTATION STATUS (2025-11-21)**

The **Source of Truth** for this project is currently located in the `docs/gemini3` directory.

* **Current Status:** Phase 1 - Brain (Signal Detection & Execution) - 90% Complete.
* **Active Roadmap:** [docs/gemini3/roadmap/README.md](gemini3/roadmap/README.md)
* **Handoff & Context:** [docs/gemini3/handoff_to_next_session.md](gemini3/handoff_to_next_session.md)

## 🛑 Outdated Documentation

The following directories contain **OUTDATED** documentation and should be referenced with caution. They reflect the previous microservices architecture and may not align with the current **Monolith/Host** architecture:

* `docs/overall_architecture/` (Legacy)
* `analyzer/docs/` (Legacy)
* `collections/docs/` (Legacy)
* `trader/docs/` (Legacy)

## 🚀 Current Architecture (Monolith)

* **Host:** `collections` project acts as the host process.
* **Integration:** `trader` logic (`TraderBot.Core`, `TraderBot.Exchanges.*`) is referenced directly as libraries.
* **Execution:** Zero-latency (<1ms) direct method calls from `SignalDetector` to `TradeExecutor`.
* **Configuration:** Single Source of Truth in `trader/src/Host/appsettings.json` (loaded by `collections`).

For the latest instructions, please refer to [docs/gemini3/handoff_to_next_session.md](gemini3/handoff_to_next_session.md).
