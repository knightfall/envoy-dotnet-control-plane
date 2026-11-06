"""Exercise the installed NuGet CLI against a disposable loopback V3 registry."""
import http.server
import json
from pathlib import Path
import subprocess
import sys
import threading


def check_retry(root):
    policy = json.loads(Path(__file__).with_name('policy.json').read_text())
    stem = policy['packages'][0] + '.' + policy['version']
    package = Path(root) / 'packages' / (stem + '.nupkg')
    symbols = package.with_suffix('.snupkg')
    requests = []
    symbol_uploads = 0

    class Registry(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            base = 'http://127.0.0.1:' + str(self.server.server_port)
            index = {'version': '3.0.0', 'resources': [
                {'@id': base + '/package', '@type': 'PackagePublish/2.0.0'},
                {'@id': base + '/symbols', '@type': 'SymbolPackagePublish/4.9.0'}]}
            data = json.dumps(index).encode()
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_PUT(self):
            nonlocal symbol_uploads
            if self.headers.get('Transfer-Encoding') == 'chunked':
                while True:
                    length = int(self.rfile.readline().split(b';')[0], 16)
                    if length == 0:
                        while self.rfile.readline().strip():
                            pass
                        break
                    self.rfile.read(length)
                    self.rfile.read(2)
            else:
                self.rfile.read(int(self.headers['Content-Length']))
            if self.path.rstrip('/') == '/symbols':
                symbol_uploads += 1
                status = 201 if symbol_uploads == 1 else 409
            else:
                status = 409
            requests.append((self.path.rstrip('/'), status))
            self.send_response(status)
            self.end_headers()

        def log_message(self, *_):
            pass

    with http.server.ThreadingHTTPServer(('127.0.0.1', 0), Registry) as server:
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        source = 'http://127.0.0.1:' + str(server.server_port) + '/index.json'

        def push(path, options, expected_success):
            # HTTP and dummy credentials are confined to this loopback test server.
            result = subprocess.run(['dotnet', 'nuget', 'push', str(path), '--source', source,
                                     '--api-key', 'fixture-key', '--allow-insecure-connections', *options],
                                    capture_output=True, text=True, timeout=30)
            if (result.returncode == 0) != expected_success:
                raise RuntimeError('NuGet retry behavior differs: ' + result.stdout + result.stderr)

        try:
            push(package, ['--no-symbols', '--skip-duplicate'], True)
            push(symbols, ['--skip-duplicate'], True)
            push(symbols, ['--skip-duplicate'], True)
            push(package, ['--no-symbols'], False)
        finally:
            server.shutdown()
            thread.join()
    if requests != [('/package', 409), ('/symbols', 201), ('/symbols', 409), ('/package', 409)]:
        raise RuntimeError('NuGet package/symbol endpoints or duplicate request sequence differ: ' + repr(requests))
    print('NuGet CLI preserved separate symbol retries and rejected an unapproved package duplicate.')


if __name__ == '__main__':
    check_retry(sys.argv[1])
