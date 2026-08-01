#!/usr/bin/env node
/**
 * CDP TypeScript LanguageService worker — one JSON object per stdin line.
 * Request:  { id, method, params }
 * Response: { id, ok: true, result } | { id, ok: false, error }
 */
import ts from "typescript";
import * as fs from "node:fs";
import * as path from "node:path";
import * as readline from "node:readline";

/** @type {ts.LanguageService | null} */
let service = null;
/** @type {string | null} */
let projectRoot = null;
/** @type {string | null} */
let configFileName = null;
/** @type {Map<string, { version: number, snapshot: ts.IScriptSnapshot }>} */
const scriptVersions = new Map();

function respond(id, ok, resultOrError) {
  const msg = ok
    ? { id, ok: true, result: resultOrError }
    : { id, ok: false, error: String(resultOrError) };
  process.stdout.write(JSON.stringify(msg) + "\n");
}

function fileExists(p) {
  try {
    return fs.existsSync(p);
  } catch {
    return false;
  }
}

function readFile(p) {
  try {
    return fs.readFileSync(p, "utf8");
  } catch {
    return undefined;
  }
}

function ensureSnapshot(fileName) {
  const text = readFile(fileName) ?? "";
  const existing = scriptVersions.get(fileName);
  if (existing && existing.snapshot.getText(0, existing.snapshot.getLength()) === text) {
    return;
  }
  const version = (existing?.version ?? 0) + 1;
  scriptVersions.set(fileName, {
    version,
    snapshot: ts.ScriptSnapshot.fromString(text),
  });
}

function openProject(params) {
  const root = path.resolve(params.projectRoot);
  let cfg = params.tsconfigPath
    ? path.resolve(params.tsconfigPath)
    : ts.findConfigFile(root, fileExists, "tsconfig.json");
  if (!cfg || !fileExists(cfg)) {
    throw new Error(`tsconfig.json not found under ${root}`);
  }
  configFileName = cfg;
  projectRoot = path.dirname(cfg);

  const configFile = ts.readConfigFile(cfg, readFile);
  if (configFile.error) {
    throw new Error(ts.flattenDiagnosticMessageText(configFile.error.messageText, "\n"));
  }
  const parsed = ts.parseJsonConfigFileContent(
    configFile.config,
    ts.sys,
    projectRoot,
    undefined,
    cfg
  );

  const fileNames = new Set(parsed.fileNames.map((f) => path.normalize(f)));

  /** @type {ts.LanguageServiceHost} */
  const host = {
    getCompilationSettings: () => parsed.options,
    getScriptFileNames: () => [...fileNames],
    getScriptVersion: (fileName) => {
      ensureSnapshot(fileName);
      return String(scriptVersions.get(fileName)?.version ?? 0);
    },
    getScriptSnapshot: (fileName) => {
      if (!fileExists(fileName) && !fileNames.has(path.normalize(fileName))) {
        return undefined;
      }
      ensureSnapshot(fileName);
      return scriptVersions.get(fileName)?.snapshot;
    },
    getCurrentDirectory: () => projectRoot,
    getDefaultLibFileName: (opts) => ts.getDefaultLibFilePath(opts),
    fileExists,
    readFile,
    readDirectory: ts.sys.readDirectory,
    directoryExists: ts.sys.directoryExists,
    getDirectories: ts.sys.getDirectories,
    realpath: ts.sys.realpath,
  };

  if (service) {
    service.dispose();
  }
  service = ts.createLanguageService(host, ts.createDocumentRegistry());
  return {
    projectRoot,
    tsconfigPath: cfg,
    fileCount: fileNames.size,
  };
}

function requireService() {
  if (!service) {
    throw new Error("Call open_project first (or cdp_open with a tsconfig project).");
  }
  return service;
}

function toPos(filePath, line, column) {
  const abs = path.resolve(filePath);
  ensureSnapshot(abs);
  const snap = scriptVersions.get(abs)?.snapshot;
  if (!snap) throw new Error(`File not in project / unreadable: ${abs}`);
  const sf = ts.createSourceFile(abs, snap.getText(0, snap.getLength()), ts.ScriptTarget.Latest, true);
  // MCP / Roslyn: 1-based line & column
  return {
    abs,
    pos: sf.getPositionOfLineAndCharacter(line - 1, column - 1),
    sf,
  };
}

function locFrom(fileName, start, end) {
  const abs = path.resolve(fileName);
  ensureSnapshot(abs);
  const text = scriptVersions.get(abs)?.snapshot.getText(0, scriptVersions.get(abs).snapshot.getLength()) ?? "";
  const sf = ts.createSourceFile(abs, text, ts.ScriptTarget.Latest, true);
  const startLc = sf.getLineAndCharacterOfPosition(start);
  const endLc = sf.getLineAndCharacterOfPosition(end);
  return {
    file_path: abs,
    line: startLc.line + 1,
    column: startLc.character + 1,
    end_line: endLc.line + 1,
    end_column: endLc.character + 1,
  };
}

function goToDefinition(params) {
  const svc = requireService();
  const { abs, pos } = toPos(params.filePath, params.line, params.column);
  const defs = svc.getDefinitionAtPosition(abs, pos) ?? [];
  return {
    definitions: defs.map((d) => ({
      ...locFrom(d.fileName, d.textSpan.start, d.textSpan.start + d.textSpan.length),
      name: d.name,
      kind: d.kind,
      container_name: d.containerName,
    })),
  };
}

function findUsages(params) {
  const svc = requireService();
  const { abs, pos } = toPos(params.filePath, params.line, params.column);
  const refs = svc.findReferences(abs, pos) ?? [];
  const usages = [];
  for (var group of refs) {
    for (var ref of group.references) {
      usages.push({
        ...locFrom(ref.fileName, ref.textSpan.start, ref.textSpan.start + ref.textSpan.length),
        is_definition: !!ref.isDefinition,
      });
    }
  }
  return { usages };
}

function getDocumentSymbols(params) {
  const svc = requireService();
  const abs = path.resolve(params.filePath);
  ensureSnapshot(abs);
  const nav = svc.getNavigationTree(abs);
  if (!nav) return { symbols: [] };

  function walk(node, depth) {
    const items = [];
    if (node.name !== "<unknown>" && depth > 0) {
      const span = node.spans?.[0];
      if (span) {
        items.push({
          name: node.text,
          kind: node.kind,
          ...locFrom(abs, span.start, span.start + span.length),
        });
      }
    }
    for (var child of node.childItems ?? []) {
      items.push(...walk(child, depth + 1));
    }
    return items;
  }
  return { symbols: walk(nav, 0) };
}

function getSymbolAtPosition(params) {
  const svc = requireService();
  const { abs, pos } = toPos(params.filePath, params.line, params.column);
  const qi = svc.getQuickInfoAtPosition(abs, pos);
  if (!qi) return { symbol: null };
  const display = ts.displayPartsToString(qi.displayParts);
  const docs = ts.displayPartsToString(qi.documentation);
  return {
    symbol: {
      ...locFrom(abs, qi.textSpan.start, qi.textSpan.start + qi.textSpan.length),
      display,
      documentation: docs,
      kind: qi.kind,
    },
  };
}

function getDiagnostics(params) {
  const svc = requireService();
  const abs = path.resolve(params.filePath);
  ensureSnapshot(abs);
  const syn = svc.getSyntacticDiagnostics(abs);
  const sem = svc.getSemanticDiagnostics(abs);
  const all = [...syn, ...sem];
  return {
    diagnostics: all.map((d) => {
      const start = d.start ?? 0;
      const length = d.length ?? 0;
      return {
        ...locFrom(d.file?.fileName ?? abs, start, start + length),
        message: ts.flattenDiagnosticMessageText(d.messageText, "\n"),
        category: ts.DiagnosticCategory[d.category],
        code: d.code,
      };
    }),
  };
}

function resolveProjectRoot(params) {
  const start = path.resolve(params.path);
  const base = fileExists(start) && fs.statSync(start).isFile() ? path.dirname(start) : start;
  const cfg = ts.findConfigFile(base, fileExists, "tsconfig.json");
  if (!cfg) {
    return { found: false, path: start };
  }
  return {
    found: true,
    projectRoot: path.dirname(cfg),
    tsconfigPath: cfg,
  };
}

async function handle(msg) {
  const { id, method, params = {} } = msg;
  try {
    let result;
    switch (method) {
      case "ping":
        result = { ok: true, typescript: ts.version, projectRoot, configFileName };
        break;
      case "open_project":
        result = openProject(params);
        break;
      case "go_to_definition":
        result = goToDefinition(params);
        break;
      case "find_usages":
        result = findUsages(params);
        break;
      case "get_document_symbols":
        result = getDocumentSymbols(params);
        break;
      case "get_symbol_at_position":
        result = getSymbolAtPosition(params);
        break;
      case "get_diagnostics":
        result = getDiagnostics(params);
        break;
      case "resolve_project_root":
        result = resolveProjectRoot(params);
        break;
      default:
        throw new Error(`Unknown method: ${method}`);
    }
    respond(id, true, result);
  } catch (err) {
    respond(id, false, err?.message ?? String(err));
  }
}

const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
rl.on("line", (line) => {
  const trimmed = line.trim();
  if (!trimmed) return;
  try {
    const msg = JSON.parse(trimmed);
    handle(msg);
  } catch (err) {
    process.stderr.write(`parse error: ${err?.message ?? err}\n`);
  }
});

process.stderr.write(`cdp-ts-worker ready (typescript ${ts.version})\n`);
