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

## Entry

- `IdeWebcamChannel.Handle` / `HandleJson` · Meta `cdp_webcam`
- Window: `op=window_list` · `op=window process=|title=|hwnd=`

## Antipatterns

- Re-inlining capture/OCR switch past soft-warn.
- Parking sense ops back into external MCP guest as default habitat.
- Using `op=screen` when the need is a specific top-level window (Telegram bleed).

## last_ship

- 0.5.444: `IdeWebcamChannel.Window` — PrintWindow HWND snap + window_list
