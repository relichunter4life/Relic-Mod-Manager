// Made by Claude Ai & Relic Hunter
"use strict";

const FF = {
  tree: null, loading: false, open: { mods: true, paks: true, rs: true },

  onTree(m) { this.tree = m.tree; this.loading = false; render(); },

  size(n) { n = Number(n) || 0; return n >= 1073741824 ? (n / 1073741824).toFixed(1) + " GB" : n >= 1048576 ? (n / 1048576).toFixed(1) + " MB" : n >= 1024 ? Math.round(n / 1024) + " KB" : n + " B"; },

  view() {
    if (!this.tree && !this.loading) { this.loading = true; send({ op: "files.tree" }); }
    let h = `<div class="toolbar"><span class="grow muted small">How your game's mod folders look right now. Gold names show which Relic Mod Manager entry put each mod there.</span>
      <button class="btn small" data-ffact="refresh">Refresh</button></div>`;
    if (!this.tree) return h + `<div class="busy"><span class="spin"></span>Reading the game folder…</div>`;
    const t = this.tree;
    h += `<div class="fftop mono">${esc(t.root)}</div>`;
    if (!t.managed) h += `<div class="infobar"><span class="grow">This game copy isn't the one Relic Mod Manager manages, so mods aren't matched to Library entries.</span></div>`;
    for (const s of t.sections) {
      const shut = this.open[s.id] === false || (this.open[s.id] === undefined && (s.id === "win64" || s.id === "ue4ss" || s.id === "logic"));
      const on = s.items.filter(i => i.folder && i.on).length;
      h += `<div class="group ffsec ${shut ? "folded" : ""}"><h3 class="fold" data-ffsec="${esc(s.id)}">${shut ? "▸" : "▾"} ${ICONS.folder} ${esc(s.title)}
          <span class="muted small">${s.items.length} item${s.items.length === 1 ? "" : "s"}${s.items.some(i => i.folder) ? ` · ${on} on` : ""}</span>
          <span class="grow"></span><button class="btn small" data-ffopen="${esc(s.path)}" title="Open in File Explorer">Open</button></h3>`;
      if (!shut) {
        h += `<div class="ffnote muted small">${esc(s.note || "")}</div>`;
        h += s.items.length ? `<div class="fflist">${s.items.map(i => this.row(i)).join("")}</div>` : `<div class="empty" style="margin:8px">Empty.</div>`;
      }
      h += `</div>`;
    }
    return h;
  },

  row(i) {
    if (!i.folder) return `<div class="ffrow file"><span class="ffico">${ICONS.file}</span><span class="ffname">${esc(i.name)}</span>
      ${i.kind ? `<span class="badge">${esc(i.kind)}</span>` : ""}<span class="grow"></span>
      ${i.owner ? `<span class="ffowner" title="Put here by Relic Mod Manager">${esc(i.owner)}</span>` : ""}<span class="muted small ffsize">${this.size(i.size)}</span></div>`;
    return `<div class="ffrow ${i.on ? "" : "off"}" data-ffopen="${esc(i.path)}" title="Double-click to open">
      <span class="ffdot ${i.on ? "ok" : "off"}" title="${i.on ? "Runs in game" : "Off: no enabled.txt"}"></span><span class="ffico">${ICONS.folder}</span>
      <span class="ffname">${esc(i.name)}</span>${(i.tags || []).map(t => `<span class="badge ${t === "Relic" ? "changed" : ""}">${esc(t)}</span>`).join("")}
      <span class="grow"></span>${i.owner ? `<span class="ffowner" title="Put here by Relic Mod Manager">${esc(i.owner)}</span>` : `<span class="muted small">not from Relic Mod Manager</span>`}
      <span class="muted small ffsize">${i.files} files · ${this.size(i.size)}</span></div>`;
  }
};

ICONS.folder = '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><path d="M3 7.5a1.5 1.5 0 0 1 1.5-1.5H9l2 2h8.5A1.5 1.5 0 0 1 21 9.5v8a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5Z"/></svg>';
ICONS.file = '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.5"><path d="M6.5 3.5h7l4 4v13h-11Z"/><path d="M13.5 3.5v4h4"/></svg>';

document.addEventListener("click", e => {
  const sec = e.target.closest("[data-ffsec]");
  if (sec && !e.target.closest("[data-ffopen]")) {
    const id = sec.dataset.ffsec;
    const shut = FF.open[id] === false || (FF.open[id] === undefined && (id === "win64" || id === "ue4ss" || id === "logic"));
    FF.open[id] = shut; render(); return;
  }
  const b = e.target.closest("button[data-ffopen]");
  if (b) { send({ op: "files.open", path: b.dataset.ffopen }); return; }
  if (e.target.closest('[data-ffact="refresh"]')) { FF.tree = null; FF.loading = false; render(); }
});
document.addEventListener("dblclick", e => {
  const r = e.target.closest(".ffrow[data-ffopen]");
  if (r) send({ op: "files.open", path: r.dataset.ffopen });
});

window.FF = FF;
