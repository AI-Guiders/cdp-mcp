# Known: Unicode escapes in structured content — awaiting upstream PR

## Symptom
`\u00B7` (·), `\u250C` (┌) in tool responses — ×2-3 context tokens on Cyrillic/box-drawing.
Knowledge MCP (agent-notes) is clean — different code path.

## Root cause
MCP C# SDK 2.2.0: source-gen `JsonContext` bakes conservative encoder at compile time.
`McpJsonRelax` reflection hack patches `JsonSerializerOptions.Encoder` at runtime,
but cached `JsonTypeInfo` doesn't re-read it. Text content path works (runtime serializer),
structured content path doesn't (source-gen serializer).

## Fix
PR pending upstream (modelcontextprotocol/csharp-sdk): add encoder knob to `McpJsonUtilities`.
Until merged: `McpJsonRelax` is the best we can do — it covers text content but not structured.

## Priority
Medium — not cosmetic (context waste), but blocked on upstream.
