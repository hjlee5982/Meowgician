"""Unity WebGL 빌드를 로컬에서 실행하기 위한 간단한 서버.

Unity WebGL 빌드가 Brotli(.br)/Gzip(.gz) 압축으로 나온 경우, index.html을 더블클릭(file://)하거나
일반 `python -m http.server`로 열면 실행되지 않는다. 이 서버는 압축 파일에 올바른
Content-Type / Content-Encoding 헤더를 붙여서 브라우저가 바로 풀 수 있게 해 준다.

사용법:
    python serve_webgl.py [빌드_폴더] [포트]
    (기본: C:\\Users\\<user>\\Desktop\\Meowgician, 포트 8080)
    실행 후 브라우저에서 http://localhost:8080 을 연다. (Brotli는 localhost에서만 동작한다)
"""
import http.server
import mimetypes
import os
import socketserver
import sys

DEFAULT_DIR = os.path.join(os.path.expanduser("~"), "Desktop", "Meowgician")
ROOT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_DIR
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 8080

EXTRA_TYPES = {
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".data": "application/octet-stream",
    ".json": "application/json",
    ".xlsx": "application/octet-stream",
}
ENCODINGS = {".br": "br", ".gz": "gzip"}


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def _split(self, path):
        base, ext = os.path.splitext(path)
        if ext.lower() in ENCODINGS:
            return base, ENCODINGS[ext.lower()]
        return path, None

    def guess_type(self, path):
        inner, _ = self._split(path)
        ext = os.path.splitext(inner)[1].lower()
        if ext in EXTRA_TYPES:
            return EXTRA_TYPES[ext]
        return mimetypes.guess_type(inner)[0] or "application/octet-stream"

    def end_headers(self):
        translated = self.translate_path(self.path.split("?", 1)[0])
        _, encoding = self._split(translated)
        if encoding:
            self.send_header("Content-Encoding", encoding)
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


class Server(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


if __name__ == "__main__":
    if not os.path.isfile(os.path.join(ROOT, "index.html")):
        sys.exit("index.html 을 찾을 수 없습니다: " + ROOT)
    print("Serving %s" % ROOT)
    print("Open http://localhost:%d" % PORT)
    try:
        Server(("127.0.0.1", PORT), Handler).serve_forever()
    except KeyboardInterrupt:
        pass
