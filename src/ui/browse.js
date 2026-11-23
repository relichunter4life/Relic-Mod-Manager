// Made by Claude Ai & Relic Hunter
"use strict";

const NX_PAGE = id => `https://www.nexusmods.com/runescapedragonwilds/mods/${id}`;

function makeBrowser(o) {
  return {
    q: "", sort: "new", list: [], total: 0, loading: false, error: null, token: 0, started: false,

    fmt(n) { n = Number(n) || 0; return n >= 1e6 ? (n / 1e6).toFixed(1) + "M" : n >= 1e3 ? (n / 1e3).toFixed(1) + "k" : String(n); },
    date(s) { if (!s) return ""; const d = new Date(s); return isNaN(d) ? "" : d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" }); },

    installed(n) { return ((Mgr.st && Mgr.st.mods) || []).find(m => o.isInstalled(m, n)); },

    search(more) {
      this.loading = true; this.error = null;
      if (!more) { this.list = []; this.total = 0; }
      this.token++;
      send({ op: o.op, q: this.q, sort: this.sort, offset: more ? this.list.length : 0, token: this.token });
      render();
    },

    onList(m) {
      if (m.token !== this.token) return;
      this.loading = false;
      if (m.error) { this.error = m.error; render(); return; }
      const r = m.result || {};
      this.total = Number(r.total) || 0;
      this.list = (r.offset ? this.list : []).concat(r.nodes || []);
      render();
    },

    view() {
      if (!this.started) { this.started = true; this.search(false); }
      const p = o.key;
      let h = `<div class="toolbar"><input type="search" id="${p}q" placeholder="${o.placeholder}" value="${esc(this.q)}" style="flex:1;min-width:180px">
        <select data-${p}ctl="sort">${o.sorts.map(([v, l]) => `<option value="${v}" ${this.sort === v ? "selected" : ""}>${l}</option>`).join("")}</select>
        <button class="btn small gold" data-${p}act="go">Search</button></div>
        <div class="sitehint">${o.hint}</div>`;
      if (this.error) h += `<div class="warnbox"><div><b>${o.label} didn't answer:</b> ${esc(this.error)} <button class="btn small" data-${p}act="go">Try again</button></div></div>`;
      if (!this.list.length && this.loading) return h + `<div class="busy"><span class="spin"></span>Loading mods…</div>`;
      if (!this.list.length && !this.error) return h + `<div class="empty">No mods found.</div>`;
      h += `<div class="muted small" style="margin:0 0 8px">${this.total} mods</div><div class="nxgrid">`;
      this.list.forEach((n, i) => {
        const inst = this.installed(n);
        h += `<div class="nxcard" data-${p}act="mod" data-i="${i}" title="Open ${esc(n.name)} on ${o.label}">
          <div class="nxpic">${n.thumbnailUrl || n.pictureUrl ? `<img src="${esc(n.thumbnailUrl || n.pictureUrl)}" alt="" loading="lazy">` : ICONS.gem}
            ${inst ? `<span class="nxinst">${inst.latest ? "Update" : "Installed"}</span>` : ""}</div>
          <div class="nxbody"><div class="nxname">${esc(n.name)}</div>
          <div class="muted small">by ${esc(n.author || (n.uploader || {}).name || "?")}${n.category ? " · " + esc(n.category) : ""}</div>
          <div class="nxsum">${esc(n.summary || "")}</div>
          <div class="nxstats"><span title="${o.likeName}">♥ ${this.fmt(n.endorsements)}</span><span title="Downloads">⬇ ${this.fmt(n.downloads)}</span><span>${esc(this.date(o.dateOf(n, this.sort)))}</span></div>
          <div class="nxacts"><button class="btn small gold" data-${p}act="files" data-i="${i}">${inst ? (inst.latest ? "Update" : "Download again") : "Download"}</button><button class="btn small" data-${p}act="like" data-i="${i}">♥ ${o.likeName}</button></div></div></div>`;
      });
      h += `</div>`;
      if (this.list.length < this.total) h += `<div style="text-align:center;margin:14px 0"><button class="btn" data-${p}act="more" ${this.loading ? "disabled" : ""}>${this.loading ? "Loading…" : "Load more"}</button></div>`;
      return h;
    },

    onClick(a) {
      const d = a.dataset, act = d[o.key + "act"];
      if (!act) return false;
      const n = this.list[Number(d.i)];
      switch (act) {
        case "go": { const i = $("#" + o.key + "q"); this.q = i ? i.value : this.q; this.search(false); return true; }
        case "more": this.search(true); return true;
        case "mod": if (n) send({ op: "open", url: o.page(n) }); return true;
        case "files": if (n) { send({ op: "open", url: o.files(n) }); toast(o.downloadTip); } return true;
        case "like": if (n) { send({ op: "open", url: o.page(n) }); toast(o.likeTip); } return true;
      }
      return false;
    },

    onChange(el) {
      if (el.dataset[o.key + "ctl"] === "sort") { this.sort = el.value; this.search(false); return true; }
      return false;
    }
  };
}

const RB = makeBrowser({
  key: "rb", op: "nx.search", label: "Nexus", likeName: "Endorse",
  placeholder: "Search Dragonwilds mods on Nexus…",
  sorts: [["new", "Newest"], ["updated", "Recently updated"], ["endorsements", "Most endorsed"], ["downloads", "Most downloaded"]],
  hint: "Mods open in your own browser. On the mod's <b>Files</b> tab click <b>Manual Download</b>, then drop the file on <b>Install Mods</b>.",
  isInstalled: (m, n) => m.site === "nexus" && String(m.siteModId) === String(n.modId),
  page: n => NX_PAGE(n.modId),
  files: n => NX_PAGE(n.modId) + "?tab=files",
  dateOf: (n, sort) => sort === "new" ? (n.createdAt || n.updatedAt) : n.updatedAt,
  downloadTip: "Opening Nexus in your browser. Click Manual Download, then drop the file on Install Mods.",
  likeTip: "Opening the mod in your browser. Click Endorse there."
});

document.addEventListener("keydown", e => {
  if (e.key !== "Enter" || !e.target) return;
  if (e.target.id === "rbq") { RB.q = e.target.value; RB.search(false); }
});

window.RB = RB;
