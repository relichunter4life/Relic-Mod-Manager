// Made by Claude Ai & Relic Hunter
"use strict";

(function () {
  const store = (() => { try { return JSON.parse(localStorage.getItem("relic.lib") || "{}"); } catch (e) { return {}; } })();
  const lib = Object.assign({ q: "", sort: "category", filter: "all" }, store);
  const saveLib = () => { try { localStorage.setItem("relic.lib", JSON.stringify({ sort: lib.sort, filter: lib.filter })); } catch (e) { } };
  const CAT_ORDER = ["Frameworks"];

  function problem(m) { return !!(m.crash || (m.warn && m.warn.length) || (m.needs && m.needs.length)); }

  function listed(st) {
    const mods = (st.mods || []).slice();
    const q = lib.q.trim().toLowerCase();
    let out = mods.filter(m => lib.filter === "all" || (lib.filter === "on" && m.on) || (lib.filter === "off" && !m.on) || (lib.filter === "problems" && problem(m)));
    if (q) out = out.filter(m => [m.name, m.author, m.category, m.notes, m.kind].some(x => (x || "").toLowerCase().includes(q)));
    const by = { name: (a, b) => a.name.localeCompare(b.name), installed: (a, b) => String(b.installedAt || "").localeCompare(String(a.installedAt || "")),
      author: (a, b) => (a.author || "~").localeCompare(b.author || "~") || a.name.localeCompare(b.name), order: (a, b) => a.order - b.order,
      category: (a, b) => catRank(a.category) - catRank(b.category) || (a.category || "").localeCompare(b.category || "") || a.order - b.order };
    out.sort(by[lib.sort] || by.order);
    return out;
  }
  function catRank(c) { const i = CAT_ORDER.indexOf(c); return i >= 0 ? i : c === "Other" ? 99 : 50; }

  Object.assign(Mgr, {
    info: null, dragKey: null,

    viewLibrary() {
      const st = this.st || {};
      const mods = st.mods || [];
      const adv = !!st.advanced, gal = st.libraryView === "gallery";
      const on = mods.filter(m => m.on).length, probs = mods.filter(problem).length;
      let h = `<div class="pagehead"><h1>Library</h1><span class="counter" title="Mods switched on / installed">Active <b>${on}</b> / ${mods.length}</span><span class="grow"></span>
        <div class="seg viewseg"><button class="btn small ${gal ? "" : "gold"}" data-mact="libview" data-v="list">☰ List</button><button class="btn small ${gal ? "gold" : ""}" data-mact="libview" data-v="gallery">▦ Gallery</button></div>
        <label class="advswitch" title="Shows load order numbers, drag to reorder, file clashes"><span>Advanced</span><label class="switch"><input type="checkbox" data-mact="advanced" ${adv ? "checked" : ""}><span></span></label></label></div>`;
      h += this.banners();
      h += this.ue4ssBanner();
      h += `<div class="dropzone slim ${this.dragging ? "hot" : ""}" data-mact="pick"><div class="dz-icon">${ICONS.download}</div>
        <div><b>Drop mod files here</b><span class="muted"> · .zip, .7z or .rar · Relic Mod Manager puts every file in the right place. Or click to choose files.</span></div></div>`;
      const missing = [];
      if (st.activeGame && !(st.has || {}).ue4ss && mods.some(m => (m.needs || []).includes("ue4ss"))) missing.push(["UE4SS 4.0", UE4SS_ID]);
      if (st.activeGame && !(st.has || {}).runeschema && mods.some(m => (m.needs || []).includes("runeschema"))) missing.push(["RuneSchema", RUNESCHEMA_ID]);
      if (missing.length) h += `<div class="warnbox">${missing.map(([n, id]) => `<div><b>Some mods need ${esc(n)}.</b> <button class="btn small gold" data-mact="get" data-id="${id}">Get ${esc(n)}</button></div>`).join("")}</div>`;
      h += `<div class="libbar">
        <input type="search" id="libq" placeholder="Search your mods…" value="${esc(lib.q)}">
        <div class="seg chips">${[["all", "All"], ["on", "On"], ["off", "Off"], ["problems", `⚠️ Problems${probs ? " " + probs : ""}`]].map(([v, l]) => `<button class="chipbtn ${lib.filter === v ? "on" : ""}" data-libf="${v}">${l}</button>`).join("")}</div>
        <select data-libsort="1" title="Sort">${[["category", "By category"], ["order", "Load order"], ["name", "Name"], ["installed", "Newest first"], ["author", "Author"]].map(([v, l]) => `<option value="${v}" ${lib.sort === v ? "selected" : ""}>${l}</option>`).join("")}</select>
        <span class="grow"></span>
        <button class="btn small" data-mact="install">Install from file</button>
        <button class="btn small" data-mact="autosort" title="Frameworks first, add-ons after the mods they change. Runs after every install too.">Auto-Sort</button>
        <button class="btn small" data-mact="updates">Check for updates</button></div>`;
      if (st.lastResult) h += `<div class="muted small" style="margin:-4px 0 8px">${esc(st.lastResult)}</div>`;
      h += `<div id="libList">${this.libList()}</div>`;
      return h + this.clashTable(adv);
    },

    libList() {
      const st = this.st || {};
      const mods = st.mods || [];
      if (!mods.length) return `<div class="empty">No mods yet. Drop a mod file above, or find one in <a href="#" data-view="browse">Nexus Mods</a>.</div>`;
      const list = listed(st);
      if (!list.length) return `<div class="empty">No mods match. <a href="#" data-libclear="1">Show all</a></div>`;
      const adv = !!st.advanced, gal = st.libraryView === "gallery";
      const drag = adv && lib.sort === "order" && !lib.q && lib.filter === "all";
      const card = gal ? m => this.gcard(m, adv) : (m, i) => this.lrow(m, adv, drag);
      const wrap = items => gal ? `<div class="gallery">${items}</div>` : `<div class="modlist">${items}</div>`;
      if (lib.sort !== "category") return (drag ? `<div class="muted small" style="margin-bottom:6px">Drag mods to change the load order. Lower numbers load first; the higher number wins a file clash.</div>` : "") + wrap(list.map(card).join(""));
      let h = "", cat = null, buf = [];
      const flush = () => { if (buf.length) h += `<div class="cathead"><span>${esc(cat)}</span><i>${buf.length}</i></div>` + wrap(buf.join("")); buf = []; };
      for (const m of list) { const c = m.category || "Other"; if (c !== cat) { flush(); cat = c; } buf.push(card(m)); }
      flush();
      return h;
    },

    paintLib() { const el = document.getElementById("libList"); if (el) el.innerHTML = this.libList(); else render(); },

    pic(m, cls) { return m.picture ? `<img class="${cls}" src="${esc(m.picture)}" alt="" loading="lazy">` : `<span class="${cls} gem">${ICONS.gem}</span>`; },

    gcard(m, adv) {
      const st = this.st || {};
      return `<div class="gcard ${m.on ? "" : "off"}" data-libdbl="${esc(m.key)}" title="Double-click to open its folder">
        <div class="gpic">${m.picture ? `<img src="${esc(m.picture)}" alt="" loading="lazy">` : `<span class="gem">${ICONS.gem}</span>`}
          ${this.warnIcon(m)}${m.relic ? `<span class="relictag">Relic</span>` : ""}${adv ? `<span class="prio">#${m.order + 1}</span>` : ""}</div>
        <div class="gbody"><div class="gname" title="${esc(m.name)}">${esc(m.name)}</div>
          <div class="muted small">${esc(m.version || "")}${m.author ? " · " + esc(m.author) : ""}</div>
          ${m.notes ? `<div class="note" title="${esc(m.notes)}">✎ ${esc(m.notes)}</div>` : ""}
          <div class="tags">${m.latest ? `<span class="badge changed">Update: ${esc(m.latest)}</span>` : ""}${this.clashBadge(m, false)}</div>
          <div class="gfoot"><label class="switch"><input type="checkbox" data-mact="toggle" data-key="${esc(m.key)}" ${m.on ? "checked" : ""} ${st.running ? "disabled" : ""}><span></span></label><span class="grow"></span>
            ${m.latest && m.siteModId ? `<button class="btn small gold" data-mact="get" data-id="${esc(m.siteModId)}">Update</button>` : ""}
            <button class="btn small" data-libinfo="${esc(m.key)}" title="Files, conflicts, notes and more">ⓘ Info</button>
            <button class="btn small" data-mact="uninstall" data-key="${esc(m.key)}" data-name="${esc(m.name)}" ${st.running ? "disabled" : ""} title="Remove">✕</button></div></div></div>`;
    },

    lrow(m, adv, drag) {
      const st = this.st || {};
      const site = m.site === "nexus" ? "Nexus" : m.site === "curseforge" ? "CurseForge" : m.site === "mo2" ? "From MO2" : m.site === "vortex" ? "From Vortex" : "From file";
      return `<div class="modrow ${m.on ? "" : "off"} ${drag ? "drag" : ""}" data-libdbl="${esc(m.key)}" ${drag ? `draggable="true" data-dragkey="${esc(m.key)}"` : ""} title="Double-click to open its folder">
        ${adv ? `<span class="prio">${m.order + 1}</span>` : ""}
        <label class="switch"><input type="checkbox" data-mact="toggle" data-key="${esc(m.key)}" ${m.on ? "checked" : ""} ${st.running ? "disabled" : ""}><span></span></label>
        ${this.pic(m, "pic")}
        <div class="info"><div class="name">${this.warnIcon(m)}${esc(m.name)} <span class="muted small">${esc(m.version || "")}${m.author ? " · " + esc(m.author) : ""}</span></div>
          ${m.notes ? `<div class="note" title="${esc(m.notes)}">✎ ${esc(m.notes)}</div>` : ""}
          <div class="tags"><span class="badge">${esc(m.kind || "Files")}</span><span class="badge">${site}</span>${m.relic ? `<span class="badge personal">Relic mod</span>` : ""}
          ${m.latest ? `<span class="badge changed">Update: ${esc(m.latest)}</span>` : ""}
          ${(m.needs || []).map(n => `<span class="badge restart">Needs ${n === "ue4ss" ? "UE4SS" : "RuneSchema"}</span>`).join("")}
          ${this.clashBadge(m, adv)}${adv && m.wins ? `<span class="badge personal">wins ${m.wins} files</span>` : ""}${adv ? `<span class="muted small">${m.files} files</span>` : ""}</div></div>
        <div class="acts">${m.latest && m.siteModId ? `<button class="btn small gold" data-mact="get" data-id="${esc(m.siteModId)}">Update</button>` : ""}
          <button class="btn small" data-libinfo="${esc(m.key)}">ⓘ Info</button>
          <button class="btn small" data-mact="uninstall" data-key="${esc(m.key)}" data-name="${esc(m.name)}" ${st.running ? "disabled" : ""}>Remove</button></div></div>`;
    },

    settingsView(folder) {
      if (!folder) return null;
      if (S.byId[folder]) return "mod:" + folder;
      if (OR.byFolder[folder]) return "mm:" + folder;
      return null;
    },

    infoModal() {
      const i = this.info, m = ((this.st || {}).mods || []).find(x => x.key === i.key);
      if (!m) { this.info = null; return ""; }
      const d = i.data;
      const tabs = [["overview", "Overview"], ["files", "Files"], ["conflicts", `Conflicts${m.clashWith && m.clashWith.length ? " (" + m.clashWith.length + ")" : ""}`], ["notes", "Notes"]];
      let body = "";
      const folder = d && (d.files.map(f => (f.rel.match(/ue4ss\/Mods\/([^/]+)\//i) || [])[1]).find(x => x && x.toLowerCase() !== "shared") || null);
      const sv = this.settingsView(folder);
      if (i.tab === "overview") {
        body = `<div class="infohero">${this.pic(m, "ipic")}<div class="grow">
          <div class="muted small">${esc(m.kind || "Files")} · ${m.on ? "On" : "Off"} · load order #${m.order + 1}</div>
          <div style="margin:4px 0">${m.author ? "by <b>" + esc(m.author) + "</b> · " : ""}version ${esc(m.version || "?")}${m.latest ? ` · <b style="color:var(--gold2)">update ${esc(m.latest)}</b>` : ""}</div>
          <div class="muted small">Category: <select data-infocat="${esc(m.key)}"><option value="Automatic">Automatic (${esc(m.category || "Other")})</option>${["Frameworks", "Gameplay", "User Interface", "Items", "Armour", "Weapons", "Spells", "Skills", "Crafting", "Buildings", "Companions", "Visuals", "Audio", "Animations", "Utilities", "Save Files", "Miscellaneous"].map(c => `<option ${m.catUser && c === m.category ? "selected" : ""}>${esc(c)}</option>`).join("")}</select></div>
          ${d && d.installedAt ? `<div class="muted small">Installed ${esc(String(d.installedAt).slice(0, 10))}${d.archive ? " from " + esc(String(d.archive).replace(/^.*[\\/]/, "")) : ""}</div>` : ""}</div></div>
          ${m.crash ? `<div class="warnbox"><div><b>⚠️ Blamed for a crash${m.crashAt ? " on " + esc(m.crashAt) : ""}:</b> ${esc(m.crash)} <button class="btn small" data-mact="dismisscrash" data-key="${esc(m.key)}">Dismiss</button></div></div>` : ""}
          <div class="nxbtns"><button class="btn gold" data-mact="openModFolder" data-key="${esc(m.key)}">Open its folder</button>
            ${sv ? `<button class="btn" data-libgo="${esc(sv)}">Its settings</button>` : ""}
            ${m.url || m.siteModId ? `<button class="btn" data-mact="page" data-url="${esc(m.url || "")}" data-id="${esc(m.siteModId || "")}" data-site="${esc(m.site || "")}">Mod page</button>` : ""}
            ${m.latest && m.siteModId ? `<button class="btn" data-mact="get" data-id="${esc(m.siteModId)}">Update</button>` : ""}</div>`;
      } else if (i.tab === "files") {
        body = !d ? `<div class="busy"><span class="spin"></span>Loading…</div>` : `<p class="muted small" style="margin:0 0 6px">${d.files.length}${d.more ? "+" + d.more : ""} files. <span style="color:var(--green)">●</span> wins a clash · <span style="color:var(--red)">●</span> replaced by another mod</p>
          <pre class="report filetree">${d.files.map(f => (f.won ? "● " : f.lost ? "○ " : "  ") + esc(f.rel)).join("\n")}</pre>`;
      } else if (i.tab === "conflicts") {
        body = !(m.clashWith || []).length ? `<div class="empty">No other mod changes the same files.</div>` :
          (m.clashWith || []).map(c => `<div class="toolbar"><span class="grow"><b>${esc(c.name)}</b> · ${c.files} file${c.files === 1 ? "" : "s"} · ${c.wins ? "this mod wins" : esc(c.name) + " wins"}</span>
            ${c.fixable ? `<span class="badge personal">Sorted</span>` : `<span class="badge restart">⚠️ Replace or remove one</span>`}</div>`).join("") +
          (d ? `<pre class="report" style="max-height:200px">${esc(d.conflicts.map(f => f.file + "   (" + f.mods.join(" → ") + ")").join("\n"))}</pre>` : "") +
          `<div style="text-align:right"><button class="btn small gold" data-mact="autosort">Auto-Sort</button></div>`;
      } else {
        body = `<p class="muted small" style="margin:0 0 6px">A note just for you, shown on the mod's card.</p>
          <textarea id="infonote" maxlength="300" placeholder="e.g. turn off before co-op">${esc(m.notes || "")}</textarea>
          <div style="text-align:right;margin-top:8px"><button class="btn small gold" data-libnote="${esc(m.key)}">Save note</button></div>`;
      }
      return `<div class="modalbg"><div class="modal wide"><div class="modalhead"><h2>${esc(m.name)}</h2><span class="grow"></span><button class="btn small" data-libclose="1">✕</button></div>
        <div class="tabs">${tabs.map(([t, l]) => `<button class="tab ${i.tab === t ? "on" : ""}" data-libtab="${t}">${l}</button>`).join("")}</div>${body}</div></div>`;
    },
  });

  const baseModal = Mgr.modal;
  Mgr.modal = function () { return this.info ? this.infoModal() : baseModal.call(this); };
  const baseMsg = Mgr.onMessage;
  Mgr.onMessage = function (m) {
    if (m.op === "mgr.files") { if (this.info && this.info.key === m.key) { this.info.data = m; render(); } return; }
    return baseMsg.call(this, m);
  };

  document.addEventListener("click", e => {
    const t = e.target;
    const f = t.closest("[data-libf]");
    if (f) { lib.filter = f.dataset.libf; saveLib(); render(); return; }
    if (t.closest("[data-libclear]")) { e.preventDefault(); lib.q = ""; lib.filter = "all"; saveLib(); render(); return; }
    const inf = t.closest("[data-libinfo]");
    if (inf) { Mgr.info = { key: inf.dataset.libinfo, tab: "overview", data: null }; send({ op: "mgr.files", key: inf.dataset.libinfo }); render(); return; }
    const tab = t.closest("[data-libtab]");
    if (tab && Mgr.info) { Mgr.info.tab = tab.dataset.libtab; render(); return; }
    if (t.closest("[data-libclose]")) { Mgr.info = null; render(); return; }
    const go = t.closest("[data-libgo]");
    if (go) { Mgr.info = null; S.view = go.dataset.libgo; S.tab = "settings"; render(); return; }
    const note = t.closest("[data-libnote]");
    if (note) { const v = ($("#infonote") || {}).value || ""; send({ op: "mgr.note", key: note.dataset.libnote, text: v }); toast("Note saved"); return; }
    const ma = t.closest(".modal [data-mact]");
    if (ma && Mgr.info && ["page", "get", "autosort"].includes(ma.dataset.mact)) Mgr.info = null;
    if (Mgr.playMenu && !t.closest(".playgrp")) { Mgr.playMenu = false; renderNav(); }
    if (t.closest('[data-mact="openModFolder"]')) { send({ op: "mgr.openModFolder", key: t.closest('[data-mact="openModFolder"]').dataset.key }); e.stopPropagation(); }
  }, true);

  document.addEventListener("dblclick", e => {
    if (e.target.closest("button, input, select, label, a, textarea")) return;
    const c = e.target.closest("[data-libdbl]");
    if (c) send({ op: "mgr.openModFolder", key: c.dataset.libdbl });
  });

  document.addEventListener("input", e => {
    if (e.target.id === "libq") { lib.q = e.target.value; Mgr.paintLib(); }
  });
  document.addEventListener("change", e => {
    const el = e.target;
    if (el.dataset.libsort) { lib.sort = el.value; saveLib(); Mgr.paintLib(); }
    else if (el.dataset.infocat) send({ op: "mgr.category", key: el.dataset.infocat, text: el.value });
  });

  document.addEventListener("dragstart", e => {
    const r = e.target.closest && e.target.closest("[data-dragkey]");
    if (!r) return;
    Mgr.dragKey = r.dataset.dragkey;
    e.dataTransfer.effectAllowed = "move";
    try { e.dataTransfer.setData("text/plain", Mgr.dragKey); } catch (x) { }
    r.classList.add("dragging");
  });
  document.addEventListener("dragend", () => { Mgr.dragKey = null; document.querySelectorAll(".dragging,.dropbefore,.dropafter").forEach(x => x.classList.remove("dragging", "dropbefore", "dropafter")); });
  document.addEventListener("dragover", e => {
    if (!Mgr.dragKey) return;
    const r = e.target.closest && e.target.closest("[data-dragkey]");
    e.preventDefault(); e.stopImmediatePropagation();
    document.querySelectorAll(".dropbefore,.dropafter").forEach(x => x.classList.remove("dropbefore", "dropafter"));
    if (r && r.dataset.dragkey !== Mgr.dragKey) { const b = r.getBoundingClientRect(); r.classList.add(e.clientY < b.top + b.height / 2 ? "dropbefore" : "dropafter"); }
  }, true);
  document.addEventListener("drop", e => {
    if (!Mgr.dragKey) return;
    e.preventDefault(); e.stopImmediatePropagation();
    const r = e.target.closest && e.target.closest("[data-dragkey]");
    const key = Mgr.dragKey; Mgr.dragKey = null;
    if (!r || r.dataset.dragkey === key) return render();
    const mods = (Mgr.st.mods || []).slice().sort((a, b) => a.order - b.order);
    const from = mods.findIndex(m => m.key === key), target = mods.find(m => m.key === r.dataset.dragkey);
    const b = r.getBoundingClientRect();
    let idx = target.order + (e.clientY < b.top + b.height / 2 ? 0 : 1);
    if (from < idx) idx--;
    send({ op: "mgr.moveTo", key, index: idx });
  }, true);
})();
