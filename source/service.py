"""Internal asset compiler. Expose only to the Orleans service, without a public port."""
import argparse
import json
import logging
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from registry import Registry, load_material_definitions

MAX_REQUEST = 24_000_000


class Handler(BaseHTTPRequestHandler):
    registry = None

    def reply(self, status, value):
        data = json.dumps(value, separators=(',', ':'), allow_nan=False).encode()
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path == '/health':
            self.reply(200, {'ok': True, 'version': 1})
        elif self.path == '/shapes':
            self.reply(200, self.registry.list_shapes())
        else:
            self.reply(404, {'error': 'Unknown endpoint'})

    def do_POST(self):
        if self.path != '/prepare':
            self.reply(404, {'error': 'Unknown endpoint'})
            return
        try:
            size = int(self.headers.get('Content-Length', '-1'))
            if not 0 < size <= MAX_REQUEST:
                self.reply(413, {'error': 'Invalid or excessive request size'})
                return
            request = json.loads(self.rfile.read(size))
            self.reply(200, self.registry.prepare(request))
        except (ValueError, KeyError, TypeError, IndexError) as error:
            self.reply(422, {'error': str(error)})
        except Exception:
            logging.exception('Asset compilation failed')
            self.reply(500, {'error': 'Asset compilation failed; check the service log'})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--data', default='/data')
    parser.add_argument('--materials', default='/app/materialdefs.nqdef')
    parser.add_argument('--host', default='0.0.0.0')
    parser.add_argument('--port', type=int, default=8097)
    args = parser.parse_args()
    Handler.registry = Registry(args.data, load_material_definitions(args.materials))
    ThreadingHTTPServer((args.host, args.port), Handler).serve_forever()


if __name__ == '__main__':
    main()
