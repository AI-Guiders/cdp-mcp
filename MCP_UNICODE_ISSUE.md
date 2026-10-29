# Known: Unicode escapes in structured content despite McpJsonRelax

## Symptom
`\u00B7` (·), `\u250C` (┌), `\u2192` (→) in tool responses — structured content path.
Text content (via CdpJson) is clean. Knowledge MCP (agent-notes) is clean.

## Root cause (suspected)
MCP SDK 2.2.0: `StreamServerTransport` may create its own `JsonSerializerOptions`
bypassing both `McpJsonUtilities.DefaultOptions` and `JsonContext.Default.Options`.
The reflection patch in `McpJsonRelax` targets both, but one path escapes.

## Fix path
1. Dump the actual options the transport serializes with (add debug log)
2. If a third instance exists, patch it in `EnsureRelaxed()`
3. Or: intercept at the `CallToolResult` construction — serialize StructuredContent
   to string with relaxed options, pass as text content alongside structured

## Status
Cosmetic (data is correct, display shows `\uXXXX`). Priority: low.
