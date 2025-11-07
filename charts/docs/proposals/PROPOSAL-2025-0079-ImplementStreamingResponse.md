# PROPOSAL-2025-0079: Внедрение потоковой передачи данных для устранения сбоев по памяти

## Диагностика
Несмотря на предыдущие исправления, сервер по-прежнему аварийно завершает работу при обработке большого количества пар, что приводит к ошибке `Unexpected end of JSON input` на клиенте. Анализ логов показал, что сбой происходит после завершения всей логики обработки, в момент сериализации и отправки ответа. Это однозначно указывает на исчерпание оперативной памяти (OOM) при попытке создать в памяти гигантский JSON-объект со всеми данными.

## Предлагаемое изменение
Предлагается полностью переработать механизм ответа сервера, перейдя на потоковую передачу данных в формате **Newline Delimited JSON (NDJSON)**. Это позволит обрабатывать и отправлять данные по одной паре за раз, полностью исключая накопление результатов в памяти.

### 1. Изменения в `charts/server.py`
-   Внедрить `StreamingResponse` из FastAPI.
-   Создать асинхронный генератор `chart_data_streamer`, который будет обрабатывать по одной паре и `yield`-ить результат в виде JSON-строки с символом новой строки.
-   Изменить эндпоинт `get_dashboard_data`, чтобы он возвращал `StreamingResponse`.

```python
<<<<<<< SEARCH
:start_line:12
-------
import logging

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')

app = FastAPI()

app.add_middleware(
=======
import logging
import json
from fastapi.responses import StreamingResponse

# Configure logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')

app = FastAPI()

app.add_middleware(
>>>>>>> REPLACE
```
```python
<<<<<<< SEARCH
:start_line:133
-------
@app.get("/api/dashboard_data")
async def get_dashboard_data():
    logging.info("Received request for /api/dashboard_data")
    try:
        # Find the latest summary stats file
        analyzer_stats_dir = os.path.join(os.path.dirname(__file__), '..', 'analyzer', 'summary_stats')
        if not os.path.exists(analyzer_stats_dir):
            logging.error("Analyzer summary_stats directory not found.")
            raise HTTPException(status_code=404, detail="Analyzer summary_stats directory not found.")

        csv_files = [f for f in os.listdir(analyzer_stats_dir) if f.endswith('.csv')]
        if not csv_files:
            logging.error("No summary stats CSV files found.")
            raise HTTPException(status_code=404, detail="No summary stats CSV files found.")

        latest_file = max(csv_files, key=lambda f: os.path.getmtime(os.path.join(analyzer_stats_dir, f)))
        stats_file = os.path.join(analyzer_stats_dir, latest_file)
        logging.info(f"Using stats file: {stats_file}")

        from datetime import timedelta
        date_to_use = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")

        df_opps = pl.read_csv(stats_file)
        df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 50)
        opportunities = df_filtered.sort(['symbol', 'exchange1']).select(
            ['symbol', 'exchange1', 'exchange2']
        ).to_dicts()
        logging.info(f"Found {len(opportunities)} opportunities to process.")

        tasks = [load_and_process_pair(opp, date_to_use) for opp in opportunities]
        
        logging.info("Starting to process tasks in chunks.")
        results = await process_in_chunks(tasks, 4)
        logging.info("Finished processing tasks.")

        valid_results = [res for res in results if res is not None]
        logging.info(f"Successfully processed {len(valid_results)} pairs.")

        response_data = {"charts_data": valid_results}
        logging.info("Successfully created response data. Sending to client.")
        return response_data

    except Exception as e:
        logging.error(f"An unhandled exception occurred in get_dashboard_data: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")
=======
async def chart_data_streamer(opportunities, date_to_use):
    """Yields newline-delimited JSON strings for each chart."""
    logging.info(f"Streaming data for {len(opportunities)} opportunities.")
    processed_count = 0
    for opp in opportunities:
        try:
            chart_data = await load_and_process_pair(opp, date_to_use)
            if chart_data:
                yield json.dumps(chart_data) + '\n'
                processed_count += 1
        except Exception as e:
            logging.error(f"Error processing pair {opp.get('symbol')}: {e}", exc_info=True)
    logging.info(f"Finished streaming. Successfully sent {processed_count} chart objects.")

@app.get("/api/dashboard_data")
async def get_dashboard_data():
    logging.info("Received request for /api/dashboard_data")
    try:
        analyzer_stats_dir = os.path.join(os.path.dirname(__file__), '..', 'analyzer', 'summary_stats')
        if not os.path.exists(analyzer_stats_dir):
            logging.error("Analyzer summary_stats directory not found.")
            raise HTTPException(status_code=404, detail="Analyzer summary_stats directory not found.")

        csv_files = [f for f in os.listdir(analyzer_stats_dir) if f.endswith('.csv')]
        if not csv_files:
            logging.error("No summary stats CSV files found.")
            raise HTTPException(status_code=404, detail="No summary stats CSV files found.")

        latest_file = max(csv_files, key=lambda f: os.path.getmtime(os.path.join(analyzer_stats_dir, f)))
        stats_file = os.path.join(analyzer_stats_dir, latest_file)
        logging.info(f"Using stats file: {stats_file}")

        from datetime import timedelta
        date_to_use = (datetime.now() - timedelta(days=1)).strftime("%Y-%m-%d")

        df_opps = pl.read_csv(stats_file)
        df_filtered = df_opps.filter(pl.col('opportunity_cycles_040bp') > 50)
        opportunities = df_filtered.sort(['symbol', 'exchange1']).select(
            ['symbol', 'exchange1', 'exchange2']
        ).to_dicts()
        
        return StreamingResponse(chart_data_streamer(opportunities, date_to_use), media_type="application/x-ndjson")

    except Exception as e:
        logging.error(f"An unhandled exception occurred in get_dashboard_data: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")
>>>>>>> REPLACE
```

### 2. Изменения в `charts/index.html`
-   Переписать логику `fetch`, чтобы она использовала `ReadableStream` для чтения и парсинга NDJSON.
-   Создать новую функцию `renderSingleChart` для отрисовки каждого графика по мере поступления данных.

```html
<<<<<<< SEARCH
:start_line:25
-------
        document.addEventListener('DOMContentLoaded', async () => {
            const dashboard = document.getElementById('dashboard');
            const statusEl = document.getElementById('status');

            try {
                const response = await fetch('http://127.0.0.1:8000/api/dashboard_data');
                if (!response.ok) {
                    const err = await response.json();
                    throw new Error(err.detail || 'Failed to fetch dashboard data.');
                }
                const { charts_data } = await response.json();

                if (!charts_data || charts_data.length === 0) {
                    statusEl.textContent = 'No opportunities found meeting the criteria.';
                    return;
                }

                statusEl.textContent = `Found ${charts_data.length} opportunities. Rendering charts...`;

                for (const chartData of charts_data) {
                    const container = document.createElement('div');
                    container.className = 'chart-container';

                    const title = document.createElement('h2');
                    title.textContent = `${chartData.symbol} (${chartData.exchange1} / ${chartData.exchange2})`;

                    const chartWrapper = document.createElement('div');
                    chartWrapper.className = 'chart-wrapper';

                    container.appendChild(title);
                    container.appendChild(chartWrapper);
                    dashboard.appendChild(container);

                    // uPlot expects data in a different format: [timestamps[], values[]]
                    const dataForPlot = [
                        chartData.timestamps,
                        chartData.spreads,
                        chartData.upper_band,
                        chartData.lower_band
                    ];

                    renderChart(chartWrapper, dataForPlot);
                }
                statusEl.textContent = `Dashboard loaded with ${charts_data.length} charts.`;

            } catch (error) {
                console.error('Error building dashboard:', error);
                statusEl.innerHTML = `<p style="color: red;">Error: ${error.message}</p>`;
            }
        });
=======
        function renderSingleChart(chartData) {
            const dashboard = document.getElementById('dashboard');
            
            const container = document.createElement('div');
            container.className = 'chart-container';

            const title = document.createElement('h2');
            title.textContent = `${chartData.symbol} (${chartData.exchange1} / ${chartData.exchange2})`;

            const chartWrapper = document.createElement('div');
            chartWrapper.className = 'chart-wrapper';

            container.appendChild(title);
            container.appendChild(chartWrapper);
            dashboard.appendChild(container);

            const dataForPlot = [
                chartData.timestamps,
                chartData.spreads,
                chartData.upper_band,
                chartData.lower_band
            ];

            renderChart(chartWrapper, dataForPlot);
        }

        document.addEventListener('DOMContentLoaded', async () => {
            const statusEl = document.getElementById('status');
            let chartCount = 0;

            try {
                statusEl.textContent = 'Connecting to data stream...';
                const response = await fetch('http://127.0.0.1:8000/api/dashboard_data');
                
                if (!response.ok) {
                    throw new Error(`Failed to fetch: ${response.status} ${response.statusText}`);
                }

                const reader = response.body.getReader();
                const decoder = new TextDecoder();
                let buffer = '';

                while (true) {
                    const { done, value } = await reader.read();
                    if (done) {
                        if (buffer.trim()) {
                           const chartData = JSON.parse(buffer);
                           renderSingleChart(chartData);
                           chartCount++;
                        }
                        break;
                    }

                    buffer += decoder.decode(value, { stream: true });
                    const lines = buffer.split('\n');
                    buffer = lines.pop(); // Keep the last, possibly incomplete, line

                    for (const line of lines) {
                        if (line.trim() === '') continue;
                        const chartData = JSON.parse(line);
                        renderSingleChart(chartData);
                        chartCount++;
                        statusEl.textContent = `Rendered ${chartCount} charts...`;
                    }
                }
                
                if (chartCount === 0) {
                    statusEl.textContent = 'No opportunities found meeting the criteria.';
                } else {
                    statusEl.textContent = `Dashboard loaded with ${chartCount} charts.`;
                }

            } catch (error) {
                console.error('Error building dashboard from stream:', error);
                statusEl.innerHTML = `<p style="color: red;">Error: ${error.message}</p>`;
            }
        });
>>>>>>> REPLACE
```

## Обоснование
Этот архитектурный подход является единственно верным для обработки потенциально неограниченных объемов данных. Он полностью устраняет проблему сбоев по памяти на сервере и, как бонус, улучшает UX, так как пользователь видит графики по мере их готовности, а не ждет полной загрузки.

## Оценка рисков
- **Сложность:** Реализация несколько сложнее, особенно на стороне клиента.
- **Обработка ошибок:** Ошибки в середине потока должны корректно обрабатываться. Предложенный код включает базовую обработку.

Риски минимальны по сравнению с пользой от стабильной работы приложения.

## План тестирования
1.  Перезапустить сервер `charts`.
2.  Открыть `charts/index.html` в браузере.
3.  Убедиться, что графики начинают появляться на странице один за другим.
4.  Убедиться, что в консоли разработчика нет ошибок, и все запрошенные графики в итоге отрисованы.

## План отката
1.  Отменить изменения в файлах `charts/server.py` и `charts/index.html` с помощью `git restore <file>`.