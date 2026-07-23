#!/usr/bin/env python3
"""Minimal stdio MCP probe for CdpMcp: initialize, tools/list, cdp_tools, cdp_context."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXE = Path(os.environ.get("CDP_MCP_EXE", r"D:\cdp-mcp\CdpMcp.exe"))
CONFIG = Path(os.environ.get("CDP_MCP_CONFIG", r"D:\cdp-mcp\cdp-mcp.toml"))


def send(proc: subprocess.Popen, msg: dict) -> None:
    body = json.dumps(msg, ensure_ascii=False).encode("utf-8")
    header = f"Content-Length: {len(body)}\r\n\r\n".encode("ascii")
    proc.stdin.write(header + body)
    proc.stdin.flush()


def recv(proc: subprocess.Popen) -> dict:
    headers: dict[str, str] = {}
    while True:
        line = proc.stdout.readline()
        if not line:
            raise RuntimeError("EOF from CdpMcp")
        if line in (b"\r\n", b"\n"):
            break
        k, v = line.decode("ascii").split(":", 1)
        headers[k.strip().lower()] = v.strip()
    n = int(headers["content-length"])
    raw = proc.stdout.read(n)
    return json.loads(raw.decode("utf-8"))


def main() -> int:
    if not EXE.is_file():
        print(f"missing exe: {EXE}", file=sys.stderr)
        return 2
    args = [str(EXE)]
    if CONFIG.is_file():
        args += ["--config", str(CONFIG)]
    proc = subprocess.Popen(
        args,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=str(EXE.parent),
    )
    assert proc.stdin and proc.stdout
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
                    "clientInfo": {"name": "cdp-probe", "version": "0.1"},
                },
            },
        )
        init = recv(proc)
        send(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        send(proc, {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}})
        listed = recv(proc)
        tools = listed.get("result", {}).get("tools", [])
        names = [t["name"] for t in tools]
        meta = [n for n in names if n.startswith("cdp_")]
        domain = [n for n in names if not n.startswith("cdp_")]

        def call(tid: int, name: str, arguments: dict) -> dict:
            send(
                proc,
                {
                    "jsonrpc": "2.0",
                    "id": tid,
                    "method": "tools/call",
                    "params": {"name": name, "arguments": arguments},
                },
            )
            return recv(proc)

        health = call(3, "cdp_health", {})
        caps = call(4, "cdp_capabilities", {})
        short = call(5, "cdp_tools", {"phase": "explore", "object": "kb", "intent": "cite"})
        ctx = call(6, "cdp_context", {"phase": "act", "object": "task", "intent": "change"})
        send(proc, {"jsonrpc": "2.0", "id": 7, "method": "tools/list", "params": {}})
        listed2 = recv(proc)
        names2 = [t["name"] for t in listed2.get("result", {}).get("tools", [])]

        out = {
            "ok": True,
            "server": init.get("result", {}).get("serverInfo"),
            "list_tools_count": len(names),
            "meta": meta,
            "domain_sample": domain[:15],
            "after_context_count": len(names2),
            "after_context_has_tk_task_upsert": "tk_task_upsert" in names2,
            "health_preview": (health.get("result") or {}).get("content", [{}])[0].get("text", "")[:400],
            "caps_preview": (caps.get("result") or {}).get("content", [{}])[0].get("text", "")[:400],
            "tools_preview": (short.get("result") or {}).get("content", [{}])[0].get("text", "")[:600],
            "context_preview": (ctx.get("result") or {}).get("content", [{}])[0].get("text", "")[:300],
        }
        # Shortlist must be << full seed (~50); meta=5 + shortlist.
        if len(names) > 45:
            out["ok"] = False
            out["error"] = f"ListTools too large: {len(names)}"
        if len(meta) != 5:
            out["ok"] = False
            out["error"] = f"expected 5 meta tools, got {meta}"
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
