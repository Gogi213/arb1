# How to Run This Project

This project consists of two independent services that must be run in separate terminals from the project root (`c:/visual projects/arb1`).

## 1. Run the `collections` Service

This service collects market data.

```bash
dotnet run --project collections/src/SpreadAggregator.Presentation/SpreadAggregator.Presentation.csproj
```

## 2. Run the `charts` Service

This service provides the web dashboard. **Use this exact command.** Do not use `python charts/run_charts.py`.

```bash
uvicorn charts.server:app --host 127.0.0.1 --port 8002
```

This method ensures that `uvicorn` has full control over the application's lifecycle and prevents port conflicts.