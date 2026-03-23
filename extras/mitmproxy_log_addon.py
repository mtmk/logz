import datetime
from mitmproxy import http

import os
# ── Edit this default or set LOGZ_MITMPROXY_FILE env var ──
LOG_FILE = os.environ.get("LOGZ_MITMPROXY_FILE", "mitmproxy-requests.log")


def request(flow: http.HTTPFlow):
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        ts = datetime.datetime.now().isoformat(timespec="seconds")
        f.write(f"\n{'='*80}\n")
        f.write(f"[{ts}] {flow.request.method} {flow.request.pretty_url}\n")
        for k, v in flow.request.headers.items():
            f.write(f"  > {k}: {v}\n")
        if flow.request.content and len(flow.request.content) < 10000:
            try:
                f.write(f"  BODY: {flow.request.content.decode('utf-8', errors='replace')}\n")
            except Exception:
                f.write(f"  BODY: <{len(flow.request.content)} bytes>\n")


def response(flow: http.HTTPFlow):
    if flow.response is None:
        return
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        ts = datetime.datetime.now().isoformat(timespec="seconds")
        f.write(f"[{ts}] <- {flow.response.status_code} ({len(flow.response.content or b'')} bytes)\n")
        for k, v in flow.response.headers.items():
            f.write(f"  < {k}: {v}\n")
        if flow.response.content and len(flow.response.content) < 10000:
            try:
                f.write(f"  BODY: {flow.response.content.decode('utf-8', errors='replace')}\n")
            except Exception:
                f.write(f"  BODY: <{len(flow.response.content)} bytes>\n")
