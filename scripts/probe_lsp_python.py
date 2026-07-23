#!/usr/bin/env python3
"""Dogfood CDP LSP surface against fixtures/mini (pyright)."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

EXE = Path(os.environ.get("CDP_MCP_EXE", r"D:\cdp-mcp\CdpMcp.exe"))
CONFIG = Path(os.environ.get("CDP_MCP_CONFIG", r"D:\cdp-mcp\cdp-mcp.toml"))
FIXTURE = Path(
    os.environ.get(
        "CDP_LSP_FIXTURE",
        r"D:\Experiments\Personal Cursor Folder\Financial\software\open\lsp-lang\fixtures\mini",
    )
)
A_PY = FIXTURE / "src" / "a.py"
B_PY = FIXTURE / "src" / "b.py"


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


def tool_text(resp: dict) -> str:
    content = (resp.get("result") or {}).get("content") or []
    if not content:
        err = resp.get("error")
        return json.dumps(err or resp, ensure_ascii=False)
    return content[0].get("text") or ""


def main() -> int:
    if not EXE.is_file():
        print(f"missing exe: {EXE}", file=sys.stderr)
        return 2
    if not A_PY.is_file():
        print(f"missing fixture: {A_PY}", file=sys.stderr)
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
    out: dict = {"ok": True, "fixture": str(FIXTURE)}
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
                    "clientInfo": {"name": "lsp-py-probe", "version": "0.1"},
                },
            },
        )
        init = recv(proc)
        out["server"] = (init.get("result") or {}).get("serverInfo")
        send(proc, {"jsonrpc": "2.0", "method": "notifications/initialized"})

        tid = 2

        def call(name: str, arguments: dict) -> str:
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
        out["health_version"] = None
        try:
            hj = json.loads(health)
            out["health_version"] = (hj.get("runtime") or {}).get("version")
            out["lsp_presets"] = (hj.get("lsp") or {}).get("presets")
        except json.JSONDecodeError:
            out["health_raw"] = health[:500]
            out["ok"] = False

        open_r = call("cdp_open", {"path": str(FIXTURE)})
        out["open"] = open_r[:800]

        # greet starts at line 1, column 5 (1-based) in a.py
        defs = call(
            "go_to_definition",
            {"file_path": str(B_PY), "line": 3, "column": 5, "language": "python"},
        )
        out["go_to_definition"] = defs[:1200]

        diags = call(
            "get_diagnostics",
            {"file_path": str(A_PY), "language": "python"},
        )
        out["get_diagnostics"] = diags[:1200]

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
        out["rename_symbol"] = rename[:1500]

        actions = call(
            "code_actions",
            {"file_path": str(A_PY), "line": 1, "column": 5, "language": "python"},
        )
        out["code_actions"] = actions[:1500]

        # Apply first action only if list non-empty and has inline edit (apply=false first)
        try:
            aj = json.loads(actions)
            items = aj.get("actions") or aj.get("code_actions") or []
            if items:
                apply_preview = call(
                    "apply_code_action",
                    {"action_index": 0, "apply": False, "language": "python"},
                )
                out["apply_code_action_preview"] = apply_preview[:1200]
            else:
                out["apply_code_action_preview"] = "no_actions"
        except json.JSONDecodeError:
            out["apply_code_action_preview"] = "parse_failed"

        health2 = call("cdp_health", {})
        try:
            h2 = json.loads(health2)
            out["lsp_sessions"] = (h2.get("lsp") or {}).get("sessions")
        except json.JSONDecodeError:
            pass

        # Soft checks
        if "lsp_locations" not in defs and "locations" not in defs:
            out["ok"] = False
            out["error"] = "go_to_definition unexpected shape"
        if "workspace_edit" not in rename and "changes" not in rename and "document_changes" not in rename:
            # still ok if schema key differs
            if "rename_symbol" not in rename and "error" in rename.lower():
                out["ok"] = False
                out["error"] = out.get("error") or "rename_symbol failed"

        print(json.dumps(out, ensure_ascii=False, indent=2))
        return 0 if out["ok"] else 1
    finally:
        proc.kill()
        err = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
        if err.strip():
            print("--- stderr ---", file=sys.stderr)
            print(err[-3000:], file=sys.stderr)


if __name__ == "__main__":
    raise SystemExit(main())
