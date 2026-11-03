import datetime
import json
import logging
import os

from mitmproxy import http

# -- Edit these defaults or set the env vars --
# Each mitmproxy run gets its own folder under LOGZ_MITMPROXY_DIR, named
# YYYYMMDDTHHMMSSfff (created lazily on the first logged event):
#
#   <LOGZ_MITMPROXY_DIR>/
#     latest.txt              name of the newest run folder
#     20260729T131502123/
#       index.log             one JSON object per line (event: request |
#                             response | error), correlated by flow "id"
#       bodies/<id>.req|.res  bodies, never inline in the index
LOG_ROOT = os.path.abspath(os.environ.get("LOGZ_MITMPROXY_DIR", "mitmproxy-logs"))
MAX_BODY = int(os.environ.get("LOGZ_MITMPROXY_MAX_BODY", 1 << 20))  # 1 MiB

_run_dir = None


def load(loader) -> None:
    if os.environ.get("LOGZ_MITMPROXY_FILE"):
        logging.warning(
            "mitmproxy_log_addon: LOGZ_MITMPROXY_FILE is no longer used; "
            f"set LOGZ_MITMPROXY_DIR instead (logging to {LOG_ROOT})"
        )


def _ensure_run_dir() -> str:
    global _run_dir
    if _run_dir is None:
        now = datetime.datetime.now()
        name = now.strftime("%Y%m%dT%H%M%S") + f"{now.microsecond // 1000:03d}"
        run_dir = os.path.join(LOG_ROOT, name)
        os.makedirs(os.path.join(run_dir, "bodies"), exist_ok=True)
        with open(os.path.join(LOG_ROOT, "latest.txt"), "w", encoding="utf-8") as f:
            f.write(name + "\n")
        _run_dir = run_dir
    return _run_dir


def _now() -> str:
    return datetime.datetime.now().isoformat(timespec="milliseconds")


def _emit(event: dict) -> None:
    run_dir = _ensure_run_dir()
    with open(os.path.join(run_dir, "index.log"), "a", encoding="utf-8") as f:
        f.write(json.dumps(event, ensure_ascii=False) + "\n")


def _add_body(event: dict, flow_id: str, side: str, message) -> None:
    if message.raw_content is None:
        event["body_note"] = "not captured (streamed)"
        return
    try:
        # strict=False falls back to raw bytes on a broken Content-Encoding
        # instead of raising ValueError.
        body = message.get_content(strict=False) or b""
    except Exception as e:
        event["body_note"] = f"decode failed: {e}"
        body = message.raw_content
    event["body_bytes"] = len(body)
    if not body:
        return
    if b"\x00" in body[:1024]:
        event["binary"] = True
    if len(body) > MAX_BODY:
        event["truncated"] = True
        body = body[:MAX_BODY]
    path = os.path.join(_ensure_run_dir(), "bodies", f"{flow_id}.{side}")
    with open(path, "wb") as f:
        f.write(body)
    event["body_file"] = f"bodies/{flow_id}.{side}"


def _headers(message) -> list:
    return [[k, v] for k, v in message.headers.items(multi=True)]


def _peer(conn):
    peer = getattr(conn, "peername", None)
    return f"{peer[0]}:{peer[1]}" if peer else None


def request(flow: http.HTTPFlow) -> None:
    event = {
        "ts": _now(),
        "id": flow.id,
        "event": "request",
        "method": flow.request.method,
        "url": flow.request.pretty_url,
        "http_version": flow.request.http_version,
        "headers": _headers(flow.request),
    }
    client = _peer(flow.client_conn)
    if client:
        event["client"] = client
    _add_body(event, flow.id, "req", flow.request)
    _emit(event)


def response(flow: http.HTTPFlow) -> None:
    if flow.response is None:
        return
    event = {
        "ts": _now(),
        "id": flow.id,
        "event": "response",
        "method": flow.request.method,
        "url": flow.request.pretty_url,
        "status": flow.response.status_code,
        "http_version": flow.response.http_version,
        "headers": _headers(flow.response),
    }
    content_type = flow.response.headers.get("content-type")
    if content_type:
        event["content_type"] = content_type
    server = getattr(flow.server_conn, "peername", None)
    if server:
        event["server_ip"] = server[0]
    started = flow.request.timestamp_start
    if started:
        if flow.response.timestamp_start:
            event["ttfb_ms"] = round((flow.response.timestamp_start - started) * 1000)
        if flow.response.timestamp_end:
            event["elapsed_ms"] = round((flow.response.timestamp_end - started) * 1000)
    _add_body(event, flow.id, "res", flow.response)
    _emit(event)


def error(flow: http.HTTPFlow) -> None:
    _emit({
        "ts": _now(),
        "id": flow.id,
        "event": "error",
        "method": flow.request.method,
        "url": flow.request.pretty_url,
        "message": flow.error.msg if flow.error else "unknown",
    })
