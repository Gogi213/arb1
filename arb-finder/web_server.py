"""Simple HTTP server for arbitrage dashboard."""
import http.server
import socketserver
import json
import os
from pathlib import Path
from urllib.parse import urlparse


class ArbitrageDashboardHandler(http.server.SimpleHTTPRequestHandler):
    """Custom handler for serving dashboard and API."""

    def do_GET(self):
        """Handle GET requests."""
        parsed_path = urlparse(self.path)

        if parsed_path.path == '/':
            # Serve dashboard.html
            self.path = '/dashboard.html'
            return http.server.SimpleHTTPRequestHandler.do_GET(self)

        elif parsed_path.path == '/api/opportunities':
            # Serve JSON data from situations.jsonl
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()

            opportunities = self.read_opportunities()
            self.wfile.write(json.dumps(opportunities).encode())

        else:
            # Serve other files normally
            return http.server.SimpleHTTPRequestHandler.do_GET(self)

    def read_opportunities(self, max_records=100):
        """Read last N opportunities from situations.jsonl."""
        jsonl_path = Path('situations.jsonl')

        if not jsonl_path.exists():
            return []

        opportunities = []
        try:
            with open(jsonl_path, 'rb') as f:
                # Read last N lines efficiently
                lines = []
                f.seek(0, os.SEEK_END)
                file_size = f.tell()

                # Read backwards to get last lines
                buffer_size = 8192
                position = file_size

                while position > 0 and len(lines) < max_records:
                    read_size = min(buffer_size, position)
                    position -= read_size
                    f.seek(position)
                    chunk = f.read(read_size)
                    lines = chunk.split(b'\n') + lines

                # Parse last N valid JSON lines
                for line in lines[-max_records:]:
                    if line.strip():
                        try:
                            opp = json.loads(line)
                            opportunities.append(opp)
                        except json.JSONDecodeError:
                            continue

        except Exception as e:
            print(f"Error reading opportunities: {e}")

        return opportunities

    def log_message(self, format, *args):
        """Suppress log messages."""
        pass


def main():
    """Start the web server."""
    PORT = 8080

    print("=" * 80)
    print("ARBITRAGE DASHBOARD SERVER")
    print("=" * 80)
    print(f"Server running on http://localhost:{PORT}")
    print(f"Dashboard: http://localhost:{PORT}/")
    print(f"API: http://localhost:{PORT}/api/opportunities")
    print("=" * 80)
    print("\nPress Ctrl+C to stop\n")

    with socketserver.TCPServer(("", PORT), ArbitrageDashboardHandler) as httpd:
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\n\nServer stopped")


if __name__ == "__main__":
    main()
