# Unicode escapes in StructuredContent — blocked on upstream PR

## Diagnosis (confirmed 2026-10-30)

| Path | Serializer | McpJsonRelax | Unicode |
|------|-----------|-------------|---------|
| TextContentBlock | runtime JsonSerializer | ✅ patched | ✅ clean |
| StructuredContent | source-gen JsonContext | ❌ baked at compile | ❌ \uXXXX |

Agent-notes works because it only sends TextContentBlock (no StructuredContent).
CDP sends both — text for LLMs (clean), structured for strict SDK validators (escaped).

## Impact
×2-3 tokens on Unicode payloads (Cyrillic, box-drawing, arrows) in the structured
content block that gets sent alongside the clean text. ZCode shows both to the LLM.

## Cannot fix locally
- Removing StructuredContent breaks strict MCP clients (Open Code, Antigravity, etc.)
- SDK 1.3.0-2.3.0: same source-gen behavior across versions
- McpJsonRelax patches options but source-gen doesn't re-read encoder at runtime

## Fix: upstream PR
modelcontextprotocol/csharp-sdk#626 — add encoder knob to McpJsonUtilities.
Until merged: text content carries the clean data, structured is best-effort.
