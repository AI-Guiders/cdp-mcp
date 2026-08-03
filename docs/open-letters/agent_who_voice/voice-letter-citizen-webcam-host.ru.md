# @intent webcam: я сам смотрю окна и сцену, не через чужой webcam MCP

**organ:** citizen · `@intent webcam|webcam_desk|webcam_*|cdp_webcam` · `IdeWebcamChannel` (`go=webcam_desk`)
**ship:** 0.5.600
**dogfood:** 2026-08-03 — debug `cdp_citizen` dry_run+execute → webcam/webcam_desk/cdp_webcam/scene/window_list/webcam_window_list · `ack=6/6` · dual 0.5.600 lag=false

## Было

Sense plane уже жил как soft organ (`go=webcam_desk`) с scene/frame/window/OCR. Peer без Cursor мог только place organ — список окон и кадры оставались за чужим CallTool. Standalone chain не могла руками открыть window_list.

## Стало

`@intent webcam|webcam_desk|cdp_webcam|webcam_*` → `IdeWebcamChannel.HandleJson` (scene|frame|burst|av|screen|window|window_list|audio|transcribe|ocr|analyze; bare webcam=scene). `window_list`/`windows` не схлопываются в capture-`window` (иначе ambiguous). Compound `webcam_window_list` в gate. Не ворует bare `frame`/`screen`/`ocr`.

## Зачем

Dogfood: шесть intent — scene×4 + window_list×2 (phrase + compound); все applied, 9 окон в списке. Tests CitizenWebcamHostTests 7/7. Peer видит sense plane без Cursor MCP.
