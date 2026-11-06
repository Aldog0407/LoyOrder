"""Servidor local con fallback de rutas SPA para la demo LoyOrder."""

from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class SPAHandler(SimpleHTTPRequestHandler):
    def do_GET(self):
        route = self.path.split('?', 1)[0].split('#', 1)[0]
        if route in ('/menu', '/seguimiento', '/historial', '/admin', '/admin/configuracion'):
            self.path = '/index.html'
        return super().do_GET()


if __name__ == '__main__':
    import os

    os.chdir(Path(__file__).resolve().parent)
    print('LoyOrder disponible en http://localhost:5173/menu')
    ThreadingHTTPServer(('127.0.0.1', 5173), SPAHandler).serve_forever()
