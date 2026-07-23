#!/usr/bin/env python3
"""Dogfood LSP py on 0.5.108+: open, dirty type-error, diagnostics, rename, code_actions."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

EXE = Path(os.environ.get("CDP_MCP_EXE", r"D:\cdp-mcp\CdpMcp.exe"))
CONFIG = Path(os.environ.get("CDP_MCP_CONFIG", r"D:\cdp-mcp\cdp-mcp.toml"))
FIXTURE = Path(
    r"D:\Experiments\Personal Cursor Folder\Financial\software\open\lsp-lang\fixtures\mini"
)
A_PY = FIXTURE / "src" / "a.py"
B_PY = FIXTURE / "src" / "b.py"
DIRTY = (
    'def greet(name: str) -> str:\n'
    '    return f"hello {name}"\n\n\n'
    "def main() -> None:\n"
    '    x: int = "bad"\n'
    '    print(greet("cdp"))\n'
)


def send(proc, msg):
    body = json.dumps(msg, ensure_ascii=False).encode("utf-8")
    proc.stdin.write(f"Content-Length: {len(body)}\r\n\r\n".encode("ascii") + body)
    proc.stdin.flush()


def recv(proc):
    headers = {}
    while True:
        line = proc.stdout.readline()
        if not line:
            raise RuntimeError("EOF")
        if line in (b"\r\n", b"\n"):
            break
        k, v = line.decode("ascii").split(":", 1)
        headers[k.strip().lower()] = v.strip()
    n = int(headers["content-length"])
    return json.loads(proc.stdout.read(n).decode("utf-8"))


def tool_text(resp):
    content = (resp.get("result") or {}).get("content") or []
    if not content:
        return json.dumps(resp.get("error") or resp, ensure_ascii=False)
    return content[0].get("text") or ""


def main():
    orig = A_PY.read_text(encoding="utf-8")
    args = [str(EXE)]
    if CONFIG.is_file():
        args += ["--config", str(CONFIG)]
    proc = subprocess.Popen(
        args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=str(EXE.parent)
    )
    assert proc.stdin and proc.stdout
    out = {"ok": True}
    try:
        send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "lsp-py-108", "version": "0.1"},
                },
            },
        )
        init = recv(proc)
        out["server"] = (init.get("result") or {}).get("serverInfo")
        send(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        tid = 2

        def call(name, arguments):
            nonlocal tid
            tid += 1
            send(
                proc,
                {
                    "jsonrpc": "2.0",
                    "id": tid,
                    "method": "tools/call",
                    "params": {"name": name, "arguments": arguments},
                },
            )
            return tool_text(recv(proc))

        h = json.loads(call("cdp_health", {}))
        out["version"] = (h.get("runtime") or {}).get("version")
        out["open"] = call("cdp_open", {"path": str(FIXTURE)})[:400]

        edit = call(
            "cdp_buffer",
            {
                "op": "edit",
                "edit_op": "set_text",
                "path": str(A_PY),
                "text": DIRTY,
                "flush": False,
                "diagnose": False,
                "allow_shrink": True,
            },
        )
        out["edit_ok"] = '"ok": true' in edit or '"ok":true' in edit

        defs = call(
            "go_to_definition",
            {"file_path": str(B_PY), "line": 3, "column": 5, "language": "python"},
        )
        out["definition"] = defs[:600]

        diags = call("get_diagnostics", {"file_path": str(A_PY), "language": "python"})
        out["diagnostics"] = diags[:1200]
        try:
            dj = json.loads(diags)
            n = len(dj.get("diagnostics") or [])
            out["diag_count"] = n
            if n == 0:
                out["ok"] = False
                out["error"] = "expected diagnostics on type error"
        except Exception as ex:
            out["ok"] = False
            out["error"] = f"diag parse: {ex}"

        rename = call(
            "rename_symbol",
            {
                "file_path": str(A_PY),
                "line": 1,
                "column": 5,
                "new_name": "greet_renamed",
                "apply": False,
                "language": "python",
            },
        )
        out["rename"] = rename[:800]

        actions = call(
            "code_actions",
            {"file_path": str(A_PY), "line": 6, "column": 14, "language": "python"},
        )
        out["code_actions"] = actions[:1200]
        try:
            aj = json.loads(actions)
            acts = aj.get("actions") or []
            out["action_count"] = len(acts)
            if acts:
                preview = call(
                    "apply_code_action",
                    {"action_index": 0, "apply": False, "language": "python"},
                )
                out["apply_preview"] = preview[:800]
        except Exception:
            pass

        call("cdp_buffer", {"op": "close", "path": str(A_PY), "flush": False})
        if A_PY.read_text(encoding="utf-8") != orig:
            A_PY.write_text(orig, encoding="utf-8")
            out["ok"] = False
            out["error"] = (out.get("error") or "") + "; disk dirty"

        print(json.dumps(out, ensure_ascii=False, indent=2))
        return 0 if out["ok"] else 1
    finally:
        proc.kill()
        err = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
        if err.strip():
            print("--- stderr ---", file=sys.stderr)
            print(err[-2500:], file=sys.stderr)


if __name__ == "__main__":
    raise SystemExit(main())
