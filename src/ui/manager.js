// Made by Claude Ai & Relic Hunter
"use strict";

const NEXUS_MOD = id => `https://www.nexusmods.com/runescapedragonwilds/mods/${id}?tab=files`;
const UE4SS_ID = 935, RUNESCHEMA_ID = 570;

const Mgr = {
  st: null, busy: null, downloads: {},
  place: null, needs: null, lastDeploy: null, dragging: false, overlap: null,

  onMessage(m) {
    switch (m.op) {
      case "mgr.state":
        this.st = m;
        if (Array.isArray(m.downloads)) {
          const keep = {};
          for (const d of Object.values(this.downloads)) if (d.state === "downloading" || d.state === "installing") keep[d.id] = d;
          for (const d of m.downloads) if (!keep[d.id]) keep[d.id] = d;
          this.downloads = keep;
        }
        if (m.lastDeploy !== this.lastDeploy) { const first = this.lastDeploy === null; this.lastDeploy = m.lastDeploy; if (!first) send({ op: "init" }); }
        if (!m.busy) this.busy = null;
        render();
        break;
      case "mgr.busy": this.busy = m.busy ? m.text : null; render(); break;
      case "mgr.progress": this.busy = m.text; this.paintBusy(); break;
      case "mgr.download": this.downloads[m.id] = m; if (S.view === "downloads") this.paintDownloads(); renderNav(); break;

      case "mgr.place": this.place = m; render(); break;
      case "mgr.needs": this.needs = m; render(); break;
      case "nx.list": RB.onList(m); break;
      case "crash.report": Crash.onReport(m); break;
    }
  },

  get on() { return !S.core.fromGame; },

  nav(item) {
    if (!this.on) return "";
    const st = this.st || {};
    const mods = st.mods || [];
    const active = Object.values(this.downloads).filter(d => d.state === "downloading" || d.state === "installing").length;
    let h = `<div class="mgrtop">`;
    if ((st.games || []).length > 1) h += `<select class="wide" data-mact="game">${st.games.map(g => `<option value="${esc(g.id)}" ${g.id === st.activeGame ? "selected" : ""}>${esc(g.name)}</option>`).join("")}</select>`;
    if (st.activeGame) h += `<div class="row2"><select data-mact="profile" title="Profile">${(st.profiles || []).map(p => `<option ${p === st.activeProfile ? "selected" : ""}>${esc(p)}</option>`).join("")}</select>
      <div class="playgrp"><button class="btn gold" data-mact="play" ${st.running ? "disabled" : ""}>${st.running ? "Playing" : "▶ Play"}</button><button class="btn gold caret" data-mact="playmenu" title="More ways to start">▾</button>
      ${this.playMenu ? `<div class="playmenu">
        <button data-mact="play" ${st.running ? "disabled" : ""}>▶ Play with mods</button>
        <button data-mact="playvanilla" ${st.running ? "disabled" : ""}>Play without mods (Vanilla)</button>
        <button data-mact="openpath" data-what="mods">Open the mods folder</button>
        <button data-mact="openpath" data-what="log">Open UE4SS.log</button>
        <button data-mact="openpath" data-what="crashes">Open the crash folder</button></div>` : ""}</div></div>`;
    h += `</div>`;
    h += item("library", ICONS.library, "Library", mods.length ? `<span class="count">${mods.filter(m => m.on).length}/${mods.length}</span>` : "");
    h += item("browse", ICONS.browse, "Nexus Mods");
    h += item("downloads", ICONS.download, "Install Mods", active ? `<span class="badge personal">${active}</span>` : "");
    h += item("games", ICONS.games, "Games & Profiles", st.activeGame ? "" : `<span class="badge restart">!</span>`);
    return h;
  },

  view(v) {
    if (v === "library") return this.viewLibrary();
    if (v === "browse") return this.viewBrowse();
    if (v === "downloads") return this.viewDownloads();
    if (v === "games") return this.viewGames();
    return null;
  },

  banners() {
    const st = this.st || {};
    let h = "";
    if (!st.activeGame) h += `<div class="warnbox"><div><b>No game found yet.</b> Open <a href="#" data-view="games">Games & Profiles</a> to find RuneScape: Dragonwilds or pick its folder.</div></div>`;
    if (st.running) h += `<div class="warnbox"><div><b>The game is running.</b> Close it to install, turn on/off or remove mods. Changes wait until then.</div></div>`;
    else if (st.deployPending) h += `<div class="warnbox"><div><b>Changes are waiting.</b> <button class="btn small gold" data-mact="apply">Apply now</button></div></div>`;
    if (this.busy) h += `<div class="busy" id="busyLine"><span class="spin"></span>${esc(this.busy)}</div>`;
    return h;
  },

  paintBusy() {
    const el = document.getElementById("busyLine");
    if (el) el.innerHTML = `<span class="spin"></span>${esc(this.busy || "")}`;
    else if (S.view === "library") render();
  },

  ue4ssBanner() {
    const u = (this.st || {}).ue4ss;
    if (!u || !u.found || u.ours) return "";
    return `<div class="warnbox gold"><div><b>You're using UE4SS ${esc(u.version || "")}.</b> Relic Mod Manager recommends UE4SS 4.0, the version the Dragonwilds modding community uses now (RuneSchema works on it).
      <button class="btn small gold" data-mact="get" data-id="${UE4SS_ID}">Get UE4SS 4.0</button></div></div>`;
  },

  warnIcon(m) {
    const notes = [];
    if (m.crash) notes.push(`Blamed for a crash${m.crashAt ? " on " + m.crashAt : ""}: ${m.crash}`);
    if (m.warn && m.warn.length) notes.push(`Conflicts with ${m.warn.join(", ")}: both change the same files and Auto-Sort can't fix it. Replace or remove one of them.`);
    if (!notes.length) return "";
    return `<span class="warnicon" title="${esc(notes.join("\n\n"))}" data-mact="overlap" data-key="${esc(m.key)}">⚠️</span>`;
  },

  clashBadge(m, adv) {
    if (!m.clashWith || !m.clashWith.length) return "";
    const tip = "Changes the same files as:\n" + m.clashWith.map(c => `• ${c.name} (${c.files} file${c.files === 1 ? "" : "s"}, ${c.wins ? "this mod wins" : c.name + " wins"})`).join("\n") + "\nClick for the list.";
    const names = m.clashWith.map(c => c.name);
    const label = adv && m.loses ? `${m.loses} files overridden` : `Overlaps ${names.length === 1 ? names[0] : names.length + " mods"}`;
    return `<span class="badge restart click" title="${esc(tip)}" data-mact="overlap" data-key="${esc(m.key)}">${esc(label)}</span>`;
  },

  clashTable(adv) {
    if (!adv) return "";
    const c = (this.st || {}).conflicts || [];
    let h = `<div class="section"><h2>File clashes</h2></div>`;
    h += c.length ? `<p class="muted small" style="margin-bottom:8px">Mods lower in the list win. Use ▲▼ to change who wins, or Auto-Sort.</p><div class="group" style="padding:4px 8px"><table><tr><th>File</th><th>Mods (last wins)</th></tr>
      ${c.map(x => `<tr><td class="mono">${esc(x.file)}</td><td>${x.mods.map((n, i) => i === x.mods.length - 1 ? `<b style="color:var(--gold2)">${esc(n)}</b>` : esc(n)).join(" → ")}</td></tr>`).join("")}</table></div>`
      : `<div class="empty">No two mods change the same file.</div>`;
    return h;
  },

  viewBrowse() {
    return `<div class="pagehead"><h1>Nexus Mods</h1><span class="grow"></span></div>` + RB.view();
  },

  dlHtml(strip) {
    const list = Object.values(this.downloads).sort((a, b) => b.id - a.id).slice(0, strip ? 3 : 80);
    if (!list.length) return strip ? "" : `<div class="empty">Nothing installed here yet. Drop a mod file in the box above.</div>`;
    const label = { downloading: "Downloading", installing: "Installing", done: "Installed", saved: "Saved (not a mod archive)", failed: "Didn't work", "other-game": "For another game, not installed",
      skipped: "Not installed", waiting: "Waiting for you" };
    return list.map(d => {
      const pct = d.total > 0 ? Math.min(100, Math.round(d.received * 100 / d.total)) : (d.state === "done" ? 100 : 0);
      const busy = d.state === "downloading" || d.state === "installing";
      const sz = b => b < 1048576 ? Math.max(1, Math.round(b / 1024)) + " KB" : (b / 1048576).toFixed(1) + " MB";
      const mb = d.state === "downloading" && d.received ? sz(d.received) + (d.total ? " / " + sz(d.total) : "") : (d.total || d.received ? sz(d.total || d.received) : "");
      const canInstall = !busy && d.path;
      const btn = !busy && d.state === "skipped" && d.path ? `<button class="btn small gold" data-mact="dlinstall" data-id="${d.id}">Install anyway</button>`
        : !busy && d.state === "failed" && d.path ? `<button class="btn small gold" data-mact="dlinstall" data-id="${d.id}">Try again</button>`
 : "";
      return `<div class="dl ${esc(d.state)}" data-dlid="${d.id}" ${canInstall ? `title="Double-click to install it again"` : ""}><div class="dlrow">
        <div class="dlpic">${d.picture ? `<img src="${esc(d.picture)}" alt="" loading="lazy">` : ICONS.gem}</div>
        <div class="dlmain"><div class="dlname">${esc(d.name)}<span class="muted small"> ${esc(d.file || "")}</span></div>
        <div class="bar"><i style="width:${pct}%"></i></div>
        <div class="muted small">${label[d.state] || esc(d.state)}${mb ? " · " + mb : ""}${d.reason ? " · " + esc(d.reason) : ""}</div></div>
        <div class="dlbtns">${btn}${d.path && !busy ? `<button class="btn small" data-mact="dlshow" data-id="${d.id}" title="Show the file">Show file</button>` : ""}</div></div></div>`;
    }).join("");
  },

  paintDownloads() {
    const list = document.getElementById("dlList");
    if (list) list.innerHTML = this.dlHtml(false);
  },

  viewDownloads() {
    return `<div class="pagehead"><h1>Install Mods</h1><span class="grow"></span></div>
      <div class="dropzone big ${this.dragging ? "hot" : ""}" data-mact="pick">
        <div class="dz-icon">${ICONS.download}</div>
        <div class="dz-title">Drag and Drop Mod Files here</div>
        <div class="muted">.zip, .7z or .rar · Relic Mod Manager puts every file in the right game folder for you.</div>
        <div class="muted small" style="margin-top:6px">Or click this box to choose files.</div></div>
      <p class="muted" style="margin:0">Installed mods show up in your <a href="#" data-view="library">Library</a>, switched on and ready to play.</p>`;
  },

  viewGames() {
    const st = this.st || {};
    let h = `<h1>Games & Profiles</h1><p class="muted" style="margin:6px 0 16px">Which copy of RuneScape: Dragonwilds Relic Mod Manager looks after, and your mod setups.</p>`;
    h += this.banners();
    h += `<div class="group"><h3>Your game</h3>`;
    h += (st.games || []).length ? st.games.map(g => `<div class="setting"><div><div class="label">${esc(g.name)} ${g.id === st.activeGame ? `<span class="badge changed">Active</span>` : ""}</div>
        <div class="tip mono">${esc(g.root)}</div><div class="tip">Mod storage: <span class="mono">${esc(g.storage)}</span></div></div>
        <div class="ctl">${g.id === st.activeGame ? "" : `<button class="btn small" data-mact="setgame" data-id="${esc(g.id)}">Use this one</button>`}
        <button class="btn small" data-mact="removegame" data-id="${esc(g.id)}">Forget</button></div></div>`).join("")
      : `<div class="setting wide"><div class="tip">No game found yet.</div></div>`;
    h += `<div class="setting"><div class="tip">Steam and Game Pass installs are found on their own. Pick the folder yourself for other copies or a dedicated server.</div>
      <div class="ctl"><button class="btn small" data-mact="detect">Find my game</button><button class="btn small gold" data-mact="addfolder">Pick a folder…</button></div></div></div>`;
    if (st.activeGame) {
      h += `<div class="group"><h3>Profiles</h3>${(st.profiles || []).map(p => `<div class="setting"><div><div class="label">${esc(p)} ${p === st.activeProfile ? `<span class="badge changed">In use</span>` : ""}</div>
          <div class="tip">${p === "Vanilla" ? "No mods at all: the game runs exactly as it ships." : "Its own list of mods that are on."}</div></div>
          <div class="ctl">${p === st.activeProfile ? "" : `<button class="btn small gold" data-mact="useprofile" data-name="${esc(p)}">Use</button>`}
          ${(st.profiles || []).length > 1 ? `<button class="btn small" data-mact="delprofile" data-name="${esc(p)}">Delete</button>` : ""}</div></div>`).join("")}
        <div class="setting"><div class="tip">New profiles start as a copy of the one in use.</div><div class="ctl"><button class="btn small" data-mact="newprofile">New profile</button></div></div></div>`;
      h += `<div class="group"><h3>Coming from Mod Organizer 2?</h3><div class="setting"><div>
          <div class="tip">Copies every mod from MO2 into Relic Mod Manager with the same on/off state and order. MO2 itself isn't changed.</div>
          ${st.mo2 ? `<div class="tip">Found: <span class="mono">${esc(st.mo2)}</span></div>` : ""}</div>
          <div class="ctl">${st.mo2 ? `<button class="btn small gold" data-mact="mo2" data-path="${esc(st.mo2)}">Import from MO2</button>` : ""}<button class="btn small" data-mact="mo2" data-path="pick">Pick MO2 folder…</button></div></div></div>`;
      h += `<div class="group"><h3>Coming from Vortex?</h3><div class="setting"><div>
          <div class="tip">Copies every mod from Vortex's staging folder into Relic Mod Manager. Mods Vortex had deployed come in switched on. Vortex itself isn't changed.</div>
          ${st.vortex ? `<div class="tip">Found: <span class="mono">${esc(st.vortex)}</span></div>` : `<div class="tip">Vortex wasn't found for this game. Pick its mod staging folder (Vortex > Settings > Mods).</div>`}</div>
          <div class="ctl">${st.vortex ? `<button class="btn small gold" data-mact="vortex" data-path="">Import from Vortex</button>` : ""}<button class="btn small" data-mact="vortex" data-path="pick">Pick Vortex folder…</button></div></div></div>`;
      h += `<div class="group"><h3>Clean up old installs</h3><div class="setting"><div>
          <div class="tip">Moves mod files that Relic Mod Manager didn't put there (old UE4SS, ue4ss\\Mods, ~mods, LogicMods) out of the game folder into a backup, so only your Relic Mod Manager setup is used.</div></div>
          <div class="ctl"><button class="btn small" data-mact="clean">Clean game folder</button></div></div></div>`;
    }
    return h;
  },

  modal() {
    if (this.overlap) {
      const m = ((this.st || {}).mods || []).find(x => x.key === this.overlap);
      if (!m) { this.overlap = null; return ""; }
      const files = ((this.st || {}).conflicts || []).filter(c => c.mods.includes(m.name));
      return `<div class="modalbg"><div class="modal"><h2>${esc(m.name)}</h2>
        ${m.crash ? `<div class="warnbox"><div><b>⚠️ Blamed for a crash${m.crashAt ? " on " + esc(m.crashAt) : ""}:</b> ${esc(m.crash)} <button class="btn small" data-mact="dismisscrash" data-key="${esc(m.key)}">Dismiss</button></div></div>` : ""}
        ${(m.clashWith || []).length ? `<p class="muted" style="margin:6px 0 10px">Changes the same files as these mods. The mod lower in the Library wins each file.</p>
        ${(m.clashWith || []).map(c => `<div class="toolbar"><span class="grow"><b>${esc(c.name)}</b> · ${c.files} file${c.files === 1 ? "" : "s"} · ${c.wins ? "this mod wins" : esc(c.name) + " wins"}</span>
          ${c.fixable ? `<span class="badge personal">Sorted</span>` : `<span class="badge restart">⚠️ Replace or remove one</span>`}</div>`).join("")}
        <pre class="report" style="max-height:220px">${esc(files.map(f => f.file + "   (" + f.mods.join(" → ") + ")").join("\n") || "No file list.")}</pre>` : ""}
        <div style="text-align:right;margin-top:10px"><button class="btn small gold" data-mact="autosort">Auto-Sort</button> <button class="btn small" data-mact="closeoverlap">Close</button></div></div></div>`;
    }
    if (this.place) {
      const p = this.place;
      const opts = [["ue4ss", "UE4SS mod", "Lua or DLL mod for ue4ss\\Mods"], ["paks", "Pak mod", "Content\\Paks\\~mods"], ["logicmods", "LogicMod", "Content\\Paks\\LogicMods"], ["win64", "Game's Win64 folder", "Binaries\\Win64"], ["project", "Game folder", "RSDragonwilds"]];
      return `<div class="modalbg"><div class="modal"><h2>Where do these files go?</h2>
        <p class="muted" style="margin:6px 0 10px">Relic Mod Manager couldn't tell how <b>${esc(p.name)}</b> is laid out. Pick where it belongs:</p>
        <pre class="report" style="max-height:200px">${esc(p.files.join("\n"))}${p.more ? `\n… and ${p.more} more` : ""}</pre>
        <div class="choices">${opts.map(([v, a, b]) => `<button class="btn" data-mact="placeans" data-choice="${v}"><b>${a}</b><span class="muted small">${b}</span></button>`).join("")}</div>
        <div style="text-align:right;margin-top:10px"><button class="btn small" data-mact="placeans" data-choice="cancel">Don't install</button></div></div></div>`;
    }
    if (this.needs) {
      const n = this.needs;
      return `<div class="modalbg"><div class="modal"><h2>${esc(n.name)} needs more</h2>
        <p class="muted" style="margin:6px 0 14px">It won't work until these are installed too:</p>
        ${n.needs.map(x => x === "ue4ss" ? `<div class="toolbar"><span class="grow"><b>UE4SS v4.0.0</b> — the mod loader every Lua mod uses.</span><button class="btn gold small" data-mact="get" data-id="${UE4SS_ID}">Get it</button></div>`
          : `<div class="toolbar"><span class="grow"><b>RuneSchema</b> — loads mod items, NPCs and quests.</span><button class="btn gold small" data-mact="get" data-id="${RUNESCHEMA_ID}">Get it</button></div>`).join("")}
        <div style="text-align:right"><button class="btn small" data-mact="closeneeds">Later</button></div></div></div>`;
    }
    return "";
  },

  afterRender() { },

  onClick(a) {
    const d = a.dataset;
    switch (d.mact) {
      case "play": this.playMenu = false; send({ op: "mgr.play" }); break;
      case "playmenu": this.playMenu = !this.playMenu; renderNav(); break;
      case "playvanilla": this.playMenu = false; if (confirm("Start the game without any mods? Relic Mod Manager switches to the Vanilla profile. Switch back to Modded afterwards.")) send({ op: "mgr.playVanilla" }); else renderNav(); break;
      case "openpath": this.playMenu = false; renderNav(); send({ op: "mgr.openPath", what: d.what }); break;
      case "dlclear": for (const k of Object.keys(this.downloads)) if (!["downloading", "installing"].includes(this.downloads[k].state)) delete this.downloads[k]; send({ op: "mgr.dlclear" }); render(); break;
      case "dlinstall": send({ op: "mgr.dlinstall", id: Number(d.id) }); break;
      case "dlshow": send({ op: "mgr.dlshow", id: Number(d.id) }); break;

      case "apply": send({ op: "mgr.apply" }); break;
      case "pick": case "install": send({ op: "mgr.installFile" }); break;
      case "updates": send({ op: "mgr.updates" }); break;
      case "folder": send({ op: "mgr.openFolder" }); break;
      case "modfolder": send({ op: "mgr.openFolder", key: d.key }); break;
      case "up": send({ op: "mgr.move", key: d.key, dir: "up" }); break;
      case "down": send({ op: "mgr.move", key: d.key, dir: "down" }); break;
      case "uninstall": if (confirm(`Remove ${d.name}? Its files are deleted from Relic Mod Manager and the game.`)) send({ op: "mgr.uninstall", key: d.key }); break;
      case "get": this.needs = null; render(); send({ op: "open", url: NEXUS_MOD(d.id) }); toast("Opening Nexus in your browser. Click Manual Download, then drop the file on Install Mods."); break;
      case "page": send({ op: "open", url: d.url || (d.id ? NX_PAGE(d.id) : "") }); break;
      case "installpath": send({ op: "mgr.installPath", path: d.path }); break;
      case "libview": send({ op: "mgr.view", view: d.v }); if (this.st) { this.st.libraryView = d.v; render(); } break;
      case "autosort": this.overlap = null; send({ op: "mgr.autosort" }); break;
      case "vortex": if (confirm("Copy all mods from Vortex into Relic Mod Manager? Vortex is left as it is.")) send({ op: "mgr.vortex", path: d.path }); break;
      case "overlap": this.overlap = d.key; render(); break;
      case "closeoverlap": this.overlap = null; render(); break;
      case "dismisscrash": send({ op: "crash.dismiss", key: d.key }); break;

      case "detect": send({ op: "mgr.detect" }); break;
      case "addfolder": send({ op: "mgr.addFolder" }); break;
      case "setgame": send({ op: "mgr.game", id: d.id }); break;
      case "removegame": if (confirm("Forget this game? Its mods stay in storage.")) send({ op: "mgr.removeGame", id: d.id }); break;
      case "useprofile": send({ op: "mgr.profile", name: d.name }); break;
      case "delprofile": if (confirm(`Delete the profile ${d.name}?`)) send({ op: "mgr.deleteProfile", name: d.name }); break;
      case "newprofile": { const n = (prompt("Name for the new profile:") || "").trim(); if (n) send({ op: "mgr.newProfile", name: n, copy: true }); break; }
      case "mo2": if (confirm("Copy all mods from MO2 into Relic Mod Manager? MO2 is left as it is.")) send({ op: "mgr.importMo2", path: d.path }); break;
      case "clean": if (confirm("Move old mod files that Relic Mod Manager didn't install out of the game folder? They go to a backup folder.")) send({ op: "mgr.clean" }); break;
      case "placeans": send({ op: "mgr.place", id: this.place.id, choice: d.choice }); this.place = null; render(); break;
      case "closeneeds": this.needs = null; render(); break;
      default: return false;
    }
    return true;
  },

  onChange(el) {
    const d = el.dataset;
    switch (d.mact) {
      case "toggle": send({ op: "mgr.toggle", key: d.key, on: el.checked }); return true;
      case "advanced": send({ op: "mgr.advanced", on: el.checked }); return true;
      case "profile": send({ op: "mgr.profile", name: el.value }); return true;
      case "game": send({ op: "mgr.game", id: el.value }); return true;
      case "watch": send({ op: "mgr.watch", on: el.checked }); if (this.st) this.st.watch = el.checked; return true;
    }
    return false;
  },
};

Object.assign(ICONS, {
  library: '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><path d="M4 4.5h4v15H4zM10 4.5h4v15h-4z"/><path d="m16 5.2 3.8-1 3 14.5-3.8 1z"/></svg>',
  browse: '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><circle cx="12" cy="12" r="8.5"/><path d="M3.5 12h17M12 3.5c2.6 2.6 2.6 14.4 0 17M12 3.5c-2.6 2.6-2.6 14.4 0 17"/></svg>',
  download: '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><path d="M12 3.5v11M7.5 10l4.5 4.5 4.5-4.5"/><path d="M4 16.5v3h16v-3"/></svg>',
  games: '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><rect x="2.5" y="7" width="19" height="11" rx="5"/><path d="M7 11v3M5.5 12.5h3"/><circle cx="16" cy="11.5" r="1"/><circle cx="18" cy="13.5" r="1"/></svg>'
});

document.addEventListener("dragover", e => {
  if (S.view === "trouble") { e.preventDefault(); e.dataTransfer.dropEffect = "copy"; return; }
  if (!Mgr.on) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = "copy";
  if (!Mgr.dragging) { Mgr.dragging = true; const z = document.querySelector(".dropzone"); if (z) z.classList.add("hot"); }
});
document.addEventListener("dragleave", e => {
  if (e.relatedTarget) return;
  Mgr.dragging = false;
  const z = document.querySelector(".dropzone"); if (z) z.classList.remove("hot");
});
document.addEventListener("drop", e => {
  if (S.view === "trouble") {
    e.preventDefault();
    const f = e.dataTransfer && e.dataTransfer.files;
    if (f && f.length && HOST && HOST.postMessageWithAdditionalObjects) { HOST.postMessageWithAdditionalObjects({ op: "crash.drop" }, f); Crash.busy = true; render(); }
    return;
  }
  if (!Mgr.on) return;
  e.preventDefault();
  Mgr.dragging = false;
  const files = e.dataTransfer && e.dataTransfer.files;
  if (!files || !files.length) return;
  if (HOST && HOST.postMessageWithAdditionalObjects) HOST.postMessageWithAdditionalObjects({ op: "drop" }, files);
  if (S.view !== "library") S.view = "library";
  render();
  toast(`Installing ${files.length} file${files.length === 1 ? "" : "s"}…`);
});
document.addEventListener("dblclick", e => {
  const row = e.target.closest(".dl[data-dlid]");
  if (!row || e.target.closest("button")) return;
  const d = Mgr.downloads[row.dataset.dlid];
  if (d && d.path && d.state !== "downloading" && d.state !== "installing") send({ op: "mgr.dlinstall", id: Number(d.id) });
});

window.Mgr = Mgr;
