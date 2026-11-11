---
name: cdp-ide-execution
description: Use when reading, searching, editing, building, testing, debugging, or committing code in any repository. Enforces CDP IDE tools over raw Bash grep/sed/python and enforces safe git staging.
---

# CDP IDE Execution & Git Hygiene (`cdp-mcp`)

Grounded in:
- `knowledge/domains/agent-operations/note-session-start-tool-arsenal-map.md`
- `knowledge/onboarding/agent.md` (§IV)
- `knowledge/domains/agent-operations/map-kb-three-contours.md`

## 1. Activate CDP IDE Hands First (Never Fall Back to `Bash sed/grep/cat`)

1. **In harnesses with deferred MCP tools (ZCode / Claude Code):**
   On your first code turn (or after compaction), load the core CDP tools in **one single call**:
   ```
   LocalToolSearch(query="select:mcp__cdp__cdp_open,mcp__cdp__cdp_peek,mcp__cdp__cdp_buffer,mcp__cdp__cdp_edit_plan,mcp__cdp__cdp_build,mcp__cdp__cdp_test,mcp__cdp__cdp_run,mcp__cdp__cdp_shell_run")
   ```
2. **In harnesses with phase-aware direct CDP tools (OpenCode):**
   Call `cdp_cdp_open`, `cdp_cdp_peek`, `cdp_cdp_buffer`, `cdp_cdp_edit_plan`, `cdp_cdp_build`, `cdp_cdp_test` directly! Switching phase via `cdp_cdp_context(phase="explore"|"act"|"verify", object="code")` dynamically updates your direct tool palette.
3. **Hard tool-choice rules:**
   - **Reading & Searching files:** Use `cdp_peek(path=..., offset=..., limit=...)` (returns clean numbered text without per-line anchor bloat) or `cdp_peek(query=..., glob=...)`. **Never** spawn `Bash` just to run `sed -n`, `head`, `tail`, `cat`, or `grep -rn`.
   - **Editing files:** Use `cdp_buffer` (`op="edit"`, `edit_op="anchor"|"replace"|"set_text"`) or `cdp_edit_plan` (`draft -> validate -> apply`). For multi-file C# scripted transformations, use `cdp_csx_check -> cdp_csx_run` (or `cdp_csx_run_plan -> cdp_csx_promote` in dirty trees). **Never** use inline `python - <<EOF` over `Bash` to rewrite source files when `cdp_buffer` / `cdp_edit_plan` / `Edit` is available.
   - **Building & Testing:** Use `cdp_build` and `cdp_test` (structured diagnostics and failed-test extraction) before falling back to raw shell `dotnet build` / `dotnet test`.
   - **Long-running processes:** Use `cdp_shell_run(background=true)` or `terminal_run(durable=true)` — never block a foreground turn with a long server or build.

## 2. Git Commit Hygiene & Contour Protection

1. **Blind `git add -A`, `git add .`, or `git add src/` is strictly forbidden.**
   - Always check `git status --short` (or `git_scene` / `git_diff_scene`) first.
   - Stage **only explicit file paths** that belong to your current unit of work (`git add path/to/File1.cs path/to/File2.fs`).
   - Never stage `.log` files (`host.log`), `.cdp/` scratch files, or build artifacts.
2. **Public vs Private Contour Gate before Commit:**
   - When committing or writing ADRs/tests/comments in an open-source repo (`open/dash-spec`, `open/cdp-mcp`, `open/kb-public`, etc.), verify that **zero** internal/corporate identifiers (`LUS`, `LUF`, `URSA`, `SSCAD`, `EDW`) or local machine paths (`D:\PrivateRepo`, personal names) appear in the staged diff!
   - If pre-commit (`gitleaks`) blocks a commit, fix the leak immediately and check `.gitignore` if runtime logs or scratch directories were untracked.
