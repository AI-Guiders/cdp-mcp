# Domain card: IdeWebcamChannel (sense plane)

- id: `webcam`
- organ: `cdp_webcam` / `go=webcam_desk`
- product: `#CDP`

## Invariants

- Soft-warn FileLinesWarn=400; `IdeWebcamChannel` is `partial` by capture verb.
- Partials: Core (Handle/Glass/Scene) · Cam (Frame/Burst) · Av · Audio (mic+Whisper) · Screen (GDI+Analyze) · Ocr (+helpers).
- In-proc via `AIGuiders.WebcamMcp.Shared` + OpenCv — not parked Cursor webcam-mcp guest.
- Glass latch via `PublishGlass` / `RememberGlass` (flat CIDE chrome, not EICAS).

## Entry

- `IdeWebcamChannel.Handle` / `HandleJson` · Meta `cdp_webcam`

## Antipatterns

- Re-inlining capture/OCR switch past soft-warn.
- Parking sense ops back into external MCP guest as default habitat.

## last_ship

- soft-warn peel: Cam/Av/Audio/Screen/Ocr @ 0.5.395; Audio~313 · Core~176
