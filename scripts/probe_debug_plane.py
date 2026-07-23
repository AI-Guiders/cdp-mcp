#!/usr/bin/env python3
"""Dogfood cdp_debug / cdp_work debug_* against deployed CdpMcp."""
from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

EXE = Path(r"D:\cdp-mcp\CdpMcp.exe")
BASE_CFG = Path(r"D:\cdp-mcp\cdp-mcp.toml")
PROJ = Path(
    r"D:\Experiments\PersonalCursorFolder\Financial\software\open\cdp-mcp\tools\_doc_dogfood"
)
SRC = PROJ / "BrokenProbe.cs"


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


def call(proc, mid: int, name: str, arguments: dict) -> dict:
    send(
        proc,
        {
            "jsonrpc": "2.0",
            "id": mid,
            "method": "tools/call",
            "params": {"name": name, "arguments": arguments},
        },
    )
    while True:
        msg = recv(proc)
        if msg.get("id") == mid:
            return msg
        # notifications


def text_of(msg: dict) -> str:
    r = msg.get("result") or {}
    parts = r.get("content") or []
    return "\n".join(p.get("text", "") for p in parts if isinstance(p, dict))


def main() -> int:
    if not SRC.is_file():
        print("missing", SRC)
        return 2
    # pick a stable line near Main
    line = 1
    for i, raw in enumerate(SRC.read_text(encoding="utf-8").splitlines(), 1):
        if "Main" in raw or "Console" in raw:
            line = i
            break

    tmp = Path(tempfile.mkdtemp(prefix="cdp-dbg-probe-"))
    cfg = tmp / "cdp-mcp.toml"
    text = BASE_CFG.read_text(encoding="utf-8")
    wit = (tmp / "probe.witdb").as_posix()
    text += f"\n\n[intent_workspace]\ndatabase_path = \"{wit}\"\n"
    cfg.write_text(text, encoding="utf-8")

    proc = subprocess.Popen(
        [str(EXE), "--config", str(cfg)],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        cwd=str(EXE.parent),
        env=os.environ.copy(),
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
                    "clientInfo": {"name": "dbg-probe", "version": "0.1"},
                },
            },
        )
        init = recv(proc)
        ver = ((init.get("result") or {}).get("serverInfo") or {}).get("version")
        instr = (init.get("result") or {}).get("instructions") or ""
        print("version", ver)
        print("instr_has_cdp_debug", "cdp_debug" in instr)

        send(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        send(
            proc,
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": {}},
        )
        listed = recv(proc)
        names = [t["name"] for t in ((listed.get("result") or {}).get("tools") or [])]
        print("has_cdp_debug", "cdp_debug" in names)
        print("has_cdp_work", "cdp_work" in names)

        open_r = call(proc, 3, "cdp_open", {"path": str(PROJ)})
        print("open_ok", "error" not in open_r)
        print(text_of(open_r)[:400])

        # Prefer native plane; fall back to work escape
        tool = "cdp_debug" if "cdp_debug" in names else "cdp_work"
        args = (
            {"op": "bp_add", "path": str(SRC), "line": line}
            if tool == "cdp_debug"
            else {"op": "debug_bp_add", "path": str(SRC), "line": line}
        )
        add_r = call(proc, 4, tool, args)
        add_text = text_of(add_r)
        print("bp_add_tool", tool)
        print(add_text[:1200])
        ok = '"ok": true' in add_text or '"ok":true' in add_text
        print("bp_add_ok", ok)

        list_args = {"op": "bp_list"} if tool == "cdp_debug" else {"op": "debug_bp_list"}
        list_r = call(proc, 5, tool, list_args)
        print(text_of(list_r)[:800])
        return 0 if ok else 1
    finally:
        proc.kill()


if __name__ == "__main__":
    sys.exit(main())
