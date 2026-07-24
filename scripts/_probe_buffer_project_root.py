#!/usr/bin/env python3
"""Dogfood: relative cdp_buffer path= → ProjectRoot after cdp_open (not process cwd/home)."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

EXE = Path(r"D:\cdp-mcp\CdpMcp.exe")
CFG = Path(r"D:\cdp-mcp\cdp-mcp.toml")
ROOT = Path(r"D:\Experiments\agent-notes")
REL = "_dogfood_path_root_v0115.txt"


def send(proc: subprocess.Popen, msg: dict) -> None:
    body = json.dumps(msg, ensure_ascii=False).encode("utf-8")
    proc.stdin.write(f"Content-Length: {len(body)}\r\n\r\n".encode("ascii") + body)
    proc.stdin.flush()


def recv(proc: subprocess.Popen) -> dict:
    headers: dict[str, str] = {}
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


def tool_text(resp: dict) -> str:
    return resp["result"]["content"][0]["text"]


def main() -> int:
    for p in (ROOT / REL, Path.home() / REL):
        if p.exists():
            p.unlink()

    proc = subprocess.Popen(
        [str(EXE), "--config", str(CFG)],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        cwd=str(Path.home()),  # intentional: old bug used this as resolve base
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
                    "clientInfo": {"name": "path-root-dog", "version": "0.1"},
                },
            },
        )
        init = recv(proc)
        ver = (init.get("result") or {}).get("serverInfo") or {}
        print("serverInfo", ver)
        send(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})

        send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 2,
                "method": "tools/call",
                "params": {"name": "cdp_open", "arguments": {"path": str(ROOT)}},
            },
        )
        print("open_ok", "agent-notes" in tool_text(recv(proc)))

        send(
            proc,
            {
                "jsonrpc": "2.0",
                "id": 3,
                "method": "tools/call",
                "params": {
                    "name": "cdp_buffer",
                    "arguments": {
                        "op": "create",
                        "path": REL,
                        "text": "ok-0.5.115\n",
                        "overwrite": True,
                    },
                },
            },
        )
        created = json.loads(tool_text(recv(proc)))
        meta = created["meta"]["path"]
        print("RESOLVED", meta)
        under_root = Path(meta).resolve().is_relative_to(ROOT.resolve())
        under_home = Path(meta).resolve().is_relative_to(Path.home().resolve())
        print("PASS", under_root and not under_home)
        return 0 if under_root and not under_home else 1
    finally:
        proc.kill()
        for p in (ROOT / REL, Path.home() / REL):
            if p.exists():
                p.unlink()


if __name__ == "__main__":
    raise SystemExit(main())
