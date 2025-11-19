using Microsoft.AspNetCore.Mvc;

namespace SpreadAggregator.Presentation.Controllers;

public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        string html = @"
<!doctype html>
<html>
<head>
    <title>Arbot Dashboard</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            margin: 20px;
            background: #f5f5f5;
        }
        h1 {
            color: #333;
        }
        .status {
            padding: 10px;
            margin: 10px 0;
            border-radius: 5px;
            font-weight: bold;
        }
        .status.connecting { background: #ffc107; color: #000; }
        .status.connected { background: #4caf50; color: white; }
        .status.error { background: #f44336; color: white; }
        .status.closed { background: #9e9e9e; color: white; }
        .data {
            border: 1px solid #ccc;
            padding: 10px;
            max-height: 400px;
            overflow-y: scroll;
            background: white;
        }
    </style>
</head>
<body>
    <h1>Spread Aggregator Dashboard</h1>
    <div id='status' class='status connecting'>Connecting to WebSocket...</div>
    <div id='data' class='data'>Waiting for data...</div>
    <script>
        const statusEl = document.getElementById('status');
        const dataEl = document.getElementById('data');

        const ws = new WebSocket('ws://35.200.79.203:8181/ws/realtime_charts');

        ws.onopen = () => {
            console.log('WebSocket connected');
            statusEl.textContent = 'Connected to WebSocket!';
            statusEl.className = 'status connected';
            dataEl.innerHTML = '';
        };

        ws.onmessage = (e) => {
            dataEl.innerHTML += '<pre>' + e.data.replace(/\n/g, '<br>') + '</pre>';
            dataEl.scrollTop = dataEl.scrollHeight;
        };

        ws.onerror = (err) => {
            console.log('WebSocket error', err);
            statusEl.textContent = 'WebSocket error - check console';
            statusEl.className = 'status error';
        };

        ws.onclose = () => {
            console.log('WebSocket closed');
            statusEl.textContent = 'WebSocket closed';
            statusEl.className = 'status closed';
        };
    </script>
</body>
</html>";
        return Content(html, "text/html");
    }
}
