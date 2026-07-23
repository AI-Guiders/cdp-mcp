#!/usr/bin/env python3
"""Smoke: cold cdp_buffer set_text on csharp (no prior open) + optional diagnose."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

EXE = Path(os.environ.get("CDP_MCP_EXE", r"D:\cdp-mcp\CdpMcp.exe"))
CONFIG = Path(os.environ.get("CDP_MCP_CONFIG", r"D:\cdp-mcp\cdp-mcp.toml"))
CS = Path(
    os.environ.get(
        "CDP_CS_SMOKE",
        r"D:\Experiments\Personal Cursor Folder\Financial\software\open\lsp-lang\Cdp.Lsp.Core\LspLaunchPreset.cs",
    )
)
CSPROJ = Path(
    os.environ.get(
        "CDP_CS_PROJ",
        r"D:\Experiments\Personal Cursor Folder\Financial\software\open\lsp-lang\Cdp.Lsp.Core\Cdp.Lsp.Core.csproj",
    )
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
    original = CS.read_text(encoding="utf-8")
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
                    "clientInfo": {"name": "cs-edit-probe", "version": "0.1"},
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

        health = call("cdp_health", {})
        try:
            out["version"] = json.loads(health).get("runtime", {}).get("version")
        except Exception:
            out["health_raw"] = health[:300]

        open_r = call("cdp_open", {"path": str(CSPROJ)})
        out["open"] = open_r[:500]

        # Cold set_text: do NOT open buffer first — deadlock regression check
        mutated = original.replace("pyright-langserver", "pyright-langserver")  # no-op body change marker
        if "BuiltInDefaults" not in original:
            out["ok"] = False
            out["error"] = "unexpected fixture content"
        else:
            # Add a harmless trailing comment via set_text then restore via close without flush
            body = original.rstrip() + "\n// cdp-smoke\n"
            edit = call(
                "cdp_buffer",
                {
                    "op": "edit",
                    "edit_op": "set_text",
                    "path": str(CS),
                    "text": body,
                    "flush": False,
                    "diagnose": False,
                    "allow_shrink": True,
                },
            )
            out["edit"] = edit[:1500]
            if '"ok": true' not in edit and '"ok":true' not in edit:
                out["ok"] = False
                out["error"] = "edit not ok"

            close = call("cdp_buffer", {"op": "close", "path": str(CS), "flush": False})
            out["close"] = close[:400]

        # Disk must be unchanged
        if CS.read_text(encoding="utf-8") != original:
            out["ok"] = False
            out["error"] = "disk mutated unexpectedly"
            CS.write_text(original, encoding="utf-8")

        print(json.dumps(out, ensure_ascii=False, indent=2))
        return 0 if out["ok"] else 1
    finally:
        proc.kill()
        err = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
        if err.strip():
            print("--- stderr ---", file=sys.stderr)
            print(err[-2000:], file=sys.stderr)


if __name__ == "__main__":
    raise SystemExit(main())
