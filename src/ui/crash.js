// Made by Claude Ai & Relic Hunter
"use strict";

const Crash = {
  report: null, auto: false, busy: false, showEvidence: false, recent: null,

  onList(m) { this.recent = m.items || []; render(); },

  onReport(m) {
    this.report = m.report;
    this.auto = !!m.auto;
    this.busy = false;
    if (m.auto) {
      S.view = "trouble";
      toast("The game crashed. Relic Mod Manager checked what caused it.", true);
    }
    render();
  },

  summary() {
    const r = this.report; if (!r) return "";
    const lines = [`Crash on ${r.When || "?"}: ${r.Cause}`];
    if (r.Detail) lines.push(r.Detail);
    if (r.LuaError) lines.push("Lua error: " + r.LuaError);
    if (r.Error) lines.push("Error: " + r.Error);
    for (const s of r.Suspects || []) lines.push(`- ${s.Confidence === "likely" ? "Likely" : "Possible"}: ${s.Name}${s.Why ? " (" + s.Why + ")" : ""}`);
    if ((r.Evidence || []).length) lines.push("Details:", ...r.Evidence.slice(0, 8));
    lines.push(`Relic Mod Manager ${S.core.version}`);
    return "```\n" + lines.join("\n") + "\n```";
  },

  html() {
    let h = `<div class="section" style="margin-top:0"><h2>Crash reader</h2></div>
      <div class="dropzone small" data-cract="pick"><div class="dz-icon">${ICONS.trouble}</div>
      <div class="grow"><b>Drop a crash folder or file here</b><div class="muted">A crash folder, .dmp, .log or .runtime-xml file. Relic Mod Manager also checks by itself after the game closes.</div></div>
      <button class="btn small gold" data-cract="latest">Read my last crash</button></div>`;
    if (this.recent === null) { this.recent = []; send({ op: "crash.list" }); }
    if (this.recent.length) h += `<div class="group" style="margin-bottom:12px"><h3>Crashes on this PC</h3>${this.recent.map(c => `<div class="setting"><div><div class="label">${esc(c.when)} <span class="badge">${esc(c.kind)}</span></div>
      <div class="tip mono">${esc(String(c.path).replace(/^.*[\\/](?=[^\\/]+$)/, ""))} · ${c.mb} MB</div></div><div class="ctl"><button class="btn small" data-cract="read" data-path="${esc(c.path)}">Read it</button></div></div>`).join("")}</div>`;
    if (this.busy) h += `<div class="busy"><span class="spin"></span>Reading the crash files… (big dumps take a few seconds)</div>`;
    const r = this.report;
    if (!r) return h;
    const icon = { gpu: "🖥", memory: "💾", none: "📁" }[r.Kind] || "⚠️";
    h += `<div class="crashcard"><div class="crashtop"><span class="crashicon">${icon}</span><div class="grow">
        <div class="crashcause">${esc(r.Cause)}</div>
        ${r.Detail ? `<div class="crashdetail">${esc(r.Detail)}</div>` : ""}
        ${r.LuaError ? `<div class="crashdetail mono">Lua error: ${esc(r.LuaError)}</div>` : ""}
        <div class="muted small">${r.When ? "Crash on " + esc(r.When) : ""}${r.Module ? " · crashed in " + esc(r.Module) : ""}${this.auto ? " · found after the game closed" : ""}</div></div></div>`;
    if ((r.Suspects || []).length) {
      h += `<div class="suspects">`;
      for (const s of r.Suspects) {
        const btn = s.Relic
          ? `<button class="btn small red" data-cract="report" data-url="${esc(s.ReportUrl || DISCORD)}" data-relic="1">Report in our Discord</button>`
          : s.ReportUrl ? `<button class="btn small gold" data-cract="report" data-url="${esc(s.ReportUrl)}">Report to mod author</button>`
          : `<span class="muted small">No mod page known</span>`;
        h += `<div class="suspect"><span class="chip ${s.Confidence === "likely" ? "bad" : "warn"}">${s.Confidence === "likely" ? "Likely cause" : "Possible"}</span>
          <div class="grow"><b>${esc(s.Name)}</b>${s.Relic ? ` <span class="badge changed">Relic mod</span>` : ""}<div class="muted small">${esc(s.Why || "")}</div></div>${btn}</div>`;
      }
      h += `</div>`;
    }
    h += `<div class="toolbar" style="margin-top:8px"><span class="grow muted small">Reporting? The summary is copied for you to paste.</span>
      <button class="btn small" data-cract="copy">Copy summary</button>
      <button class="btn small" data-cract="evidence">${this.showEvidence ? "Hide" : "Show"} details</button></div>`;
    if (this.showEvidence) h += `<pre class="report">${esc([r.Error ? "Error: " + r.Error : "", ...(r.Evidence || [])].filter(Boolean).join("\n") || "No more details.")}</pre>`;
    return h + `</div>`;
  },

  onClick(a) {
    const d = a.dataset;
    switch (d.cract) {
      case "pick": if (!a.closest("button") || a.dataset.cract === "pick") send({ op: "crash.pick" }); return true;
      case "latest": this.busy = true; render(); send({ op: "crash.latest" }); return true;
      case "read": this.busy = true; render(); send({ op: "crash.read", path: d.path }); return true;
      case "copy": send({ op: "copy", text: this.summary() }); return true;
      case "evidence": this.showEvidence = !this.showEvidence; render(); return true;
      case "report": {
        send({ op: "copy", text: this.summary() });
        send({ op: "open", url: d.url });
        toast("Summary copied. Paste it into your report.");
        return true;
      }
    }
    return false;
  }
};

window.Crash = Crash;
