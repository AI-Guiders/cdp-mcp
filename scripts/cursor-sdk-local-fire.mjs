#!/usr/bin/env node
/**
 * L6 bridge: stdin JSON { charge, cwd, agent_id, setting_sources }
 * stdout JSON { ok, agent_id?, error?, unavailable?, detail? }
 */

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  return Buffer.concat(chunks).toString("utf8");
}

function emit(obj, exitCode = 0) {
  process.stdout.write(JSON.stringify(obj));
  process.exit(exitCode);
}

async function main() {
  let req = {};
  try {
    const raw = await readStdin();
    req = raw.trim() ? JSON.parse(raw) : {};
  } catch (e) {
    emit({ ok: false, error: "bad_json", detail: String(e?.message ?? e) }, 1);
  }

  let Agent;
  try {
    ({ Agent } = await import("@cursor/sdk"));
  } catch {
    emit({ ok: false, unavailable: true, error: "module_missing" }, 0);
  }

  const apiKey = process.env.CURSOR_API_KEY;
  if (!apiKey) {
    emit({ ok: false, unavailable: true, error: "missing_api_key" }, 0);
  }

  const cwd = req.cwd || process.cwd();
  const settingSources = Array.isArray(req.setting_sources)
    ? req.setting_sources
    : ["project", "user"];
  const charge = req.charge || "";

  try {
    let agent;
    let agentId = req.agent_id || null;

    if (agentId) {
      agent = await Agent.resume(agentId, {
        apiKey,
        local: { cwd, settingSources },
      });
    } else {
      agent = await Agent.create({
        apiKey,
        model: { id: "composer-2.5" },
        local: { cwd, settingSources },
      });
      agentId = agent.id ?? agent.agentId ?? null;
    }

    const run = await agent.send(charge);
    if (run?.wait) await run.wait();
    else if (run?.stream) {
      for await (const _ of run.stream()) {
        /* drain */
      }
    }

    emit({ ok: true, agent_id: agentId, detail: "sdk_local_send_ok" }, 0);
  } catch (e) {
    const msg = String(e?.message ?? e);
    const unavailable =
      /CURSOR_API_KEY|MODULE_NOT_FOUND|Cannot find module|ENOENT|missing_api_key/i.test(
        msg,
      );
    emit(
      {
        ok: false,
        unavailable,
        error: msg.slice(0, 400),
      },
      unavailable ? 0 : 1,
    );
  }
}

main();
