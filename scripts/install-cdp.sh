#!/usr/bin/env bash
# Install CDP from GitHub Release zip (no clone, no build). Bash port of Install-Cdp.ps1.
# Requires: curl, unzip, git, python3 (JSON merge). No PowerShell.
set -euo pipefail

ROOT=""
CDP_SOURCE=""
RELEASE_REPO="AI-Guiders/cdp-mcp"
RELEASE_TAG="latest"
RUNTIME=""
KB_PUBLIC_REPO="https://github.com/AI-Guiders/kb-public.git"
HOST_ADAPTER="cursor"
UPGRADE=0
SKIP_KB=0
FORCE_DOWNLOAD=0
WHATIF=0

usage() {
  cat <<'EOF'
Usage: install-cdp.sh [options]
  --root PATH          Install root (default: ~/.local/share/AIGuiders on Linux)
  --runtime RID        linux-x64 | osx-x64 | osx-arm64 (auto-detect default)
  --host-adapter NAME  cursor | claude | vscode | windsurf | antigravity | none
  --release-tag TAG    GitHub release tag (default: latest)
  --cdp-source PATH    Local published folder (maintainers)
  --upgrade            Preserve existing *.toml under cdp/
  --skip-kb-clone      Do not clone kb-public
  --force-download     Re-download even if binary exists
  --what-if            Print actions only
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --root) ROOT="$2"; shift 2 ;;
    --runtime) RUNTIME="$2"; shift 2 ;;
    --host-adapter) HOST_ADAPTER="$2"; shift 2 ;;
    --release-tag) RELEASE_TAG="$2"; shift 2 ;;
    --cdp-source) CDP_SOURCE="$2"; shift 2 ;;
    --upgrade) UPGRADE=1; shift ;;
    --skip-kb-clone) SKIP_KB=1; shift ;;
    --force-download) FORCE_DOWNLOAD=1; shift ;;
    --what-if) WHATIF=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

default_root() {
  case "$(uname -s)" in
    Darwin) printf '%s' "$HOME/Library/Application Support/AIGuiders" ;;
    Linux)
      local xdg="${XDG_DATA_HOME:-$HOME/.local/share}"
      printf '%s' "$xdg/AIGuiders" ;;
    *) echo "Unsupported OS for CDP install." >&2; exit 1 ;;
  esac
}

detect_runtime() {
  if [[ -n "$RUNTIME" ]]; then echo "$RUNTIME"; return; fi
  case "$(uname -s)" in
    Darwin)
      if [[ "$(uname -m)" == arm64 ]]; then echo osx-arm64; else echo osx-x64; fi ;;
    Linux)
      if [[ "$(uname -m)" == aarch64 ]]; then
        echo "linux-arm64 is not shipped yet. Pass --cdp-source or build with -r linux-arm64." >&2
        exit 1
      fi
      echo linux-x64 ;;
    *) echo "Unsupported OS for CDP install." >&2; exit 1 ;;
  esac
}

bin_name() { [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* ]] && echo CdpMcp.exe || echo CdpMcp; }

[[ -z "$ROOT" ]] && ROOT="$(default_root)"
RID="$(detect_runtime)"
BIN="$(bin_name)"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATE="$SCRIPT_DIR/cdp-mcp.toml.example"

fetch_github_payload() {
  local rid="$1"
  local api
  if [[ "$RELEASE_TAG" == latest ]]; then
    api="https://api.github.com/repos/$RELEASE_REPO/releases/latest"
  else
    api="https://api.github.com/repos/$RELEASE_REPO/releases/tags/$RELEASE_TAG"
  fi
  echo "GitHub $api"
  [[ "$WHATIF" == 1 ]] && { echo "WhatIf: download CdpMcp-*-$rid.zip"; echo /tmp/cdp-payload-whatif; return; }
  local tag asset url zip dest
  tag="$(curl -fsSL -H 'User-Agent: install-cdp' -H 'Accept: application/vnd.github+json' "$api" | python3 -c 'import json,sys; print(json.load(sys.stdin)["tag_name"])')"
  asset="$(curl -fsSL -H 'User-Agent: install-cdp' -H 'Accept: application/vnd.github+json' "$api" | python3 -c "import json,sys,re; rid=sys.argv[1]; pat=re.compile(r'^CdpMcp-.*-'+re.escape(rid)+r'\\.zip$'); assets=json.load(sys.stdin)['assets']; m=[a for a in assets if pat.match(a['name'])]; print(m[0]['browser_download_url'] if m else '')" "$rid")"
  [[ -z "$asset" ]] && { echo "Release has no CdpMcp-*-$rid.zip" >&2; exit 1; }
  zip="$(mktemp -t cdp.XXXXXX.zip)"
  curl -fsSL -H 'User-Agent: install-cdp' -o "$zip" "$asset"
  dest="$(mktemp -d -t cdp-payload.XXXXXX)"
  unzip -q "$zip" -d "$dest"
  rm -f "$zip"
  find "$dest" -name "$BIN" -type f | head -1 | xargs dirname
}

resolve_source() {
  if [[ -n "$CDP_SOURCE" ]]; then echo "$(cd "$CDP_SOURCE" && pwd)"; return; fi
  local existing="$ROOT/cdp/$BIN"
  if [[ "$FORCE_DOWNLOAD" == 0 && -f "$existing" ]]; then dirname "$existing"; return; fi
  fetch_github_payload "$RID"
}

merge_mcp_json() {
  local target="$1" command="$2" config="$3"
  [[ "$WHATIF" == 1 ]] && { echo "WhatIf: merge cdp into $target"; return; }
  mkdir -p "$(dirname "$target")"
  python3 - "$target" "$command" "$config" <<'PY'
import json, os, sys
path, cmd, cfg = sys.argv[1:4]
data = {"mcpServers": {}}
if os.path.isfile(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
data.setdefault("mcpServers", {})
data["mcpServers"]["cdp"] = {"command": cmd, "args": ["--config", cfg]}
with open(path, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2)
    f.write("\n")
print(f"Merged mcp key 'cdp' → {path}")
PY
}

cursor_mcp_path() { echo "$HOME/.cursor/mcp.json"; }
claude_config_path() {
  case "$(uname -s)" in
    Darwin) echo "$HOME/Library/Application Support/Claude/claude_desktop_config.json" ;;
    *) echo "$HOME/.config/Claude/claude_desktop_config.json" ;;
  esac
}
windsurf_mcp_path() { echo "$HOME/.codeium/windsurf/mcp_config.json"; }
antigravity_mcp_path() {
  local shared="$HOME/.gemini/config/mcp_config.json"
  local legacy="$HOME/.gemini/antigravity/mcp_config.json"
  if [[ -f "$shared" ]]; then echo "$shared"
  elif [[ -f "$legacy" ]]; then echo "$legacy"
  else echo "$shared"
  fi
}

write_host_snippets() {
  local snippets_dir="$1" command="$2" config="$3"
  [[ "$WHATIF" == 1 ]] && { echo "WhatIf: write host-snippets under $snippets_dir"; return; }
  mkdir -p "$snippets_dir"
  python3 - "$snippets_dir" "$command" "$config" <<'PY'
import json, os, sys
snippets_dir, cmd, cfg = sys.argv[1:4]
payload = {"mcpServers": {"cdp": {"command": cmd, "args": ["--config", cfg]}}}
text = json.dumps(payload, indent=2) + "\n"
for name in ("cursor", "claude", "vscode", "windsurf", "antigravity"):
    with open(os.path.join(snippets_dir, f"{name}.mcp.json"), "w", encoding="utf-8") as f:
        f.write(text)
print(f"Wrote host-snippets under {snippets_dir}")
PY
}

CDP_SRC="$(resolve_source)"
CDP_DST="$ROOT/cdp"
KB_DST="$ROOT/kb-public"
NOTES_DST="$ROOT/agent-notes"
TASK_DST="$ROOT/task-knowledge"
EXE="$CDP_DST/$BIN"

if [[ ! -f "$TEMPLATE" ]]; then
  TEMPLATE="$(mktemp -t cdp-mcp.toml.example.XXXXXX)"
  curl -fsSL -H 'User-Agent: install-cdp' \
    'https://raw.githubusercontent.com/AI-Guiders/cdp-mcp/main/scripts/cdp-mcp.toml.example' -o "$TEMPLATE"
fi

echo "Install CDP → $ROOT"
echo "  rid:    $RID"
echo "  source: $CDP_SRC"
echo "  host:   $HOST_ADAPTER"

if [[ "$WHATIF" == 0 ]]; then mkdir -p "$CDP_DST" "$NOTES_DST" "$TASK_DST"; fi
if [[ "$WHATIF" == 0 ]]; then
  rsync -a --delete --exclude ts-worker.node_modules "$CDP_SRC/" "$CDP_DST/" 2>/dev/null || {
    rm -rf "$CDP_DST"/*
    cp -a "$CDP_SRC/." "$CDP_DST/"
  }
  chmod +x "$EXE" 2>/dev/null || true
fi

if [[ "$SKIP_KB" == 0 ]]; then
  if [[ -d "$KB_DST/.git" ]]; then
    echo "kb-public exists — git pull"
    [[ "$WHATIF" == 0 ]] && git -C "$KB_DST" pull --ff-only
  else
    echo "Clone kb-public → $KB_DST"
    [[ "$WHATIF" == 0 ]] && git clone --depth 1 "$KB_PUBLIC_REPO" "$KB_DST"
  fi
fi

NOTES_TOML="$CDP_DST/agent-notes-mcp.toml"
TASK_TOML="$CDP_DST/agent-task-knowledge-mcp.toml"
CDP_TOML="$CDP_DST/cdp-mcp.toml"
CONFIG_ARG="${CDP_TOML//\\/\/}"

if [[ "$WHATIF" == 0 && "$UPGRADE" == 0 || ! -f "$CDP_TOML" ]]; then
  sed -e "s|{notesToml}|$NOTES_TOML|g" -e "s|{taskToml}|$TASK_TOML|g" "$TEMPLATE" > "$CDP_TOML"
fi

SNIPPETS_DIR="$CDP_DST/host-snippets"
write_host_snippets "$SNIPPETS_DIR" "$EXE" "$CONFIG_ARG"

case "$HOST_ADAPTER" in
  cursor) merge_mcp_json "$(cursor_mcp_path)" "$EXE" "$CONFIG_ARG"; echo "Reload MCP in Cursor." ;;
  claude) merge_mcp_json "$(claude_config_path)" "$EXE" "$CONFIG_ARG"; echo "Restart Claude Desktop." ;;
  vscode) echo "VS Code: copy host-snippets/vscode.mcp.json into user MCP settings." ;;
  windsurf)
    merge_mcp_json "$(windsurf_mcp_path)" "$EXE" "$CONFIG_ARG"
    echo "Refresh MCP in Windsurf Cascade (Manage MCPs → Refresh)."
    echo "WARN: Windsurf caps ~100 tools across all MCP servers — CDP shortlists, but heavy mounts may hit the ceiling." >&2
    ;;
  antigravity)
    merge_mcp_json "$(antigravity_mcp_path)" "$EXE" "$CONFIG_ARG"
    echo "Refresh MCP in Antigravity (MCP Store / View raw config). Path: $(antigravity_mcp_path)"
    ;;
  none|*) echo "Host none — snippets under $SNIPPETS_DIR" ;;
esac

echo "OK. Payload $EXE"
echo "  public:   $KB_DST"
echo "  personal: $NOTES_DST"
