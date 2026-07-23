#!/usr/bin/env python3
import json, subprocess, threading, time
from pathlib import Path

root = Path(r"D:\Experiments\Personal Cursor Folder\Financial\software\open\lsp-lang\fixtures\mini")
path = root / "src" / "a.py"
text = (
    'def greet(name: str) -> str:\n'
    '    return f"hello {name}"\n\n\n'
    "def main() -> None:\n"
    "    x: List[int] = []\n"
    '    print(greet("cdp"))\n'
)
uri = path.as_uri()
root_uri = root.as_uri()

proc = subprocess.Popen(
    ["pyright-langserver", "--stdio"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
)
published: dict = {}
responses: dict = {}


def send(msg):
    body = json.dumps(msg).encode()
    assert proc.stdin
    proc.stdin.write(f"Content-Length: {len(body)}\r\n\r\n".encode() + body)
    proc.stdin.flush()


def read_msg():
    headers = {}
    assert proc.stdout
    while True:
        line = proc.stdout.readline()
        if line in (b"\r\n", b"\n"):
            break
        k, v = line.decode().split(":", 1)
        headers[k.strip().lower()] = v.strip()
    n = int(headers["content-length"])
    return json.loads(proc.stdout.read(n))


def reader():
    while True:
        try:
            m = read_msg()
        except Exception:
            return
        if m.get("method") == "textDocument/publishDiagnostics":
            published[m["params"]["uri"]] = m["params"]["diagnostics"]
            print("PUB", len(m["params"]["diagnostics"]), flush=True)
        elif "id" in m and "method" not in m:
            responses[m["id"]] = m
            print("RESP", m.get("id"), type(m.get("result")).__name__, flush=True)


t = threading.Thread(target=reader, daemon=True)
t.start()
send(
    {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": {
            "processId": None,
            "rootUri": root_uri,
            "capabilities": {
                "textDocument": {
                    "codeAction": {
                        "codeActionLiteralSupport": {
                            "codeActionKind": {
                                "valueSet": ["", "quickfix", "refactor", "source"]
                            }
                        }
                    },
                    "publishDiagnostics": {},
                }
            },
            "workspaceFolders": [{"uri": root_uri, "name": "mini"}],
        },
    }
)
time.sleep(0.8)
send({"jsonrpc": "2.0", "method": "initialized", "params": {}})
send(
    {
        "jsonrpc": "2.0",
        "method": "textDocument/didOpen",
        "params": {
            "textDocument": {
                "uri": uri,
                "languageId": "python",
                "version": 1,
                "text": text,
            }
        },
    }
)
for _ in range(50):
    if any(published.values()):
        break
    time.sleep(0.1)
diags = next(iter(published.values()), [])
print("DIAGS", len(diags), flush=True)
send(
    {
        "jsonrpc": "2.0",
        "id": 3,
        "method": "textDocument/codeAction",
        "params": {
            "textDocument": {"uri": uri},
            "range": {
                "start": {"line": 5, "character": 7},
                "end": {"line": 5, "character": 11},
            },
            "context": {"diagnostics": diags},
        },
    }
)
for _ in range(40):
    if 3 in responses:
        break
    time.sleep(0.1)
print("CODEACTION", json.dumps(responses.get(3), ensure_ascii=False)[:3000], flush=True)
proc.kill()
