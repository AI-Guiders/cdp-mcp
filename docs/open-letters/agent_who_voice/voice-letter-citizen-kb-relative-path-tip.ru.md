# Voice Letter #185 — need relative_path= (abs/.. refuse tip)

**organ:** citizen · TipKbArgException relative knowledge path  
**lived:** 2026-08-06 · SoftFL invent after bare `failed` on abs/`..` paths

После VL#184 densest residual dig (live wire):
- `list_knowledge_files path=../outside` → bare `failed`
- `list_knowledge_files path=/abs/bad` → bare `failed`
- `read_knowledge_file file_path=../secret.md` → bare `failed`

MemoryScopeGateway кидает ArgumentException «Path … is invalid (no absolute paths or '..' segments)» — не формат `X is required`, поэтому generic TryTipRequiredArg молчал.

Теперь tip `need relative_path=` · reason `kb_path_not_relative`. SoftFL invent REJECT. Не Hold. SoftFL CLOSED (warn FileLines — не invent split).

**live dogfood** dual hard `0.5.675` `build_utc=2026-08-06T19:38:32Z`:
- `list_knowledge_files path=../outside` → `need relative_path=`
- `list_knowledge_files path=/abs/bad` → `need relative_path=`
- `read_knowledge_file file_path=../secret.md` → `need relative_path=`
