# Phase 5: Unified Control Plane (Web UI)

**Status:** ⚪ Not Started  
**Goal:** Single dashboard for all trading operations.

---

## Tasks

### 5.1: Next.js Dashboard

Tech stack: Next.js + TypeScript + TailwindCSS

### 5.2: Real-time Updates

WebSocket connection to `/ws/status` for live:
- P&L chart
- Signal history
- Order log
- Exchange health

### 5.3: Strategy Config UI

Adjust parameters without code changes:
- Stop-loss thresholds
- Signal strength filters
- Cooldown periods

---

## Example Dashboard

```tsx
export default function Dashboard() {
  const [signals, setSignals] = useState([]);
  
  useEffect(() => {
    const ws = new WebSocket('ws://localhost:5000/ws/signals');
    ws.onmessage = (e) => setSignals(JSON.parse(e.data));
  }, []);

  return (
    <div className="grid grid-cols-3 gap-4">
      <PnLChart />
      <SignalList signals={signals} />
      <OrderHistory />
    </div>
  );
}
```

---

[← Prev: Auto-Pilot](phase-4-autopilot.md) | [Back to Roadmap](README.md) | [Next: Monolith →](phase-6-monolith.md)
