# Domain card: IdeWebcamChannel (sense plane)

- id: `webcam`
- organ: `cdp_webcam` / `go=webcam_desk`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `IdeWebcamChannel` is `partial` by capture verb.
- Partials: Core (Handle/Glass/Scene) · Cam (Frame/Burst) · Av · Audio (mic+Whisper) · Screen (GDI+Analyze) · Window (PrintWindow HWND) · Ocr (+helpers).
- In-proc via `AIGuiders.WebcamMcp.Shared` + OpenCv — not parked Cursor webcam-mcp guest.
- Glass latch via `PublishGlass` / `RememberGlass` (flat CIDE chrome, not EICAS).
- `op=window` = one HWND (not virtual screen); `op=screen` = region/desktop burst.
- Thin peel: `maximize=true|enlarge=true` → ShowWindow max → PrintWindow → restore placement (not a new organ).

## Entry

- `IdeWebcamChannel.Handle` / `HandleJson` · Meta `cdp_webcam`
- Window: `op=window_list` · `op=window process=|title=|hwnd=` · `maximize=true`

## Antipatterns

- Re-inlining capture/OCR switch past soft-warn.
- Parking sense ops back into external MCP guest as default habitat.
- Using `op=screen` when the need is a specific top-level window (Telegram bleed).
- New maximize/shot product organ — peel stays on webcam.
- Dropping iconic/minimized HWNDs on tiny off-screen rect (`h<40`) while Process.MainWindowTitle still names Glass — Face SoftOrgan shot protocol dies overnight.
- `Opt` String-only on `hwnd=` JSON Number — numeric go_args silently ignored → ambiguous.

## last_ship

- **2026-08-07 webcam iconic Face SoftOrgan** — lived: Glass minimized (IsIconic, 159×27 @ -25600) dropped from window_list; process=/title= → No matching; numeric hwnd Opt String-only → ambiguous. Ship: placement normal rect for iconic · hwnd direct TryDescribeHwnd · process MainWindowHandle fallback · Opt Number coerce. Tests WebcamWindowMaximizeTests 3/3. Dual hard build_utc=2026-08-06T21:55:59Z. Dogfood maximize PrintWindow title=`CDP GlassCockpit · Windows` + `cdp_see` evidence `cascade-ide/tmp-glass-shots/sat-eve-webcam-iconic-face-20260807-0057.png`. SoftFL-safe (Face residual ≠ SoftFL invent).
- 0.5.560: `op=window maximize=true` — max→shot→restore peel; live Glass dogfood; VL #65
- 0.5.444: `IdeWebcamChannel.Window` — PrintWindow HWND snap + window_list
