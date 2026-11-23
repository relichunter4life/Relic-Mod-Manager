// Made by Claude Ai & Relic Hunter
"use strict";

const HOST = window.chrome && window.chrome.webview ? window.chrome.webview : null;
const WINDOW_KEY = "Shift+F7";
const DISCORD = "https://discord.gg/BumVNYDUec";

const S = {
  core: { version: "", phase: "", dll: "unknown", gameRunning: false, fromGame: false },
  mods: [], byId: {}, values: {}, files: {},
  coreSet: { schema: 1, presets: {}, disabled: {} },
  view: "home", tab: "settings", reportMod: null, reports: {}, log: null,
  capture: null, saveTimers: {}, fold: {}
};

const $ = (sel, el = document) => el.querySelector(sel);
const esc = s => String(s == null ? "" : s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const send = msg => { if (HOST) HOST.postMessage(msg); };
const clone = o => JSON.parse(JSON.stringify(o));
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

const ICONS = {
  home: '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><rect x="3.5" y="3.5" width="7" height="7" rx="1.5"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.5"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.5"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.5"/></svg>',
  keys: '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6 10h1M9.5 10h1M13 10h1M16.5 10h1M7 14h10"/></svg>',
  trouble: '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><path d="M12 3 20 6.5v5.5c0 4.5-3.4 7.8-8 9-4.6-1.2-8-4.5-8-9V6.5Z"/><path d="M12 8v5M12 16.2v.3"/></svg>',
  gem: '<svg viewBox="0 0 64 64"><path d="M32 3 57 17.5v29L32 61 7 46.5v-29Z" fill="#140c0c" stroke="url(#gg)" stroke-width="3"/><path d="M32 13 47 32 32 51 17 32Z" fill="none" stroke="url(#gg)" stroke-width="2.6"/><path d="M32 22 40 32 32 42 24 32Z" fill="url(#rr)" stroke="#f6d690"/></svg>'
};
const DEFS = '<svg width="0" height="0" style="position:absolute"><defs><linearGradient id="gg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f7dc9c"/><stop offset=".55" stop-color="#e3b75c"/><stop offset="1" stop-color="#9c6d22"/></linearGradient><linearGradient id="rr" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ff6b7a"/><stop offset=".5" stop-color="#c8102e"/><stop offset="1" stop-color="#5c0612"/></linearGradient></defs></svg>';
document.body.insertAdjacentHTML("afterbegin", DEFS);

function toast(text, bad) {
  const t = $("#toast");
  t.textContent = text;
  t.classList.toggle("bad", !!bad);
  t.classList.add("show");
  clearTimeout(toast.t);
  toast.t = setTimeout(() => t.classList.remove("show"), bad ? 5000 : Math.max(1800, Math.min(5000, text.length * 45)));
}

function parse(text, fallback) {
  if (text == null || text === "") return fallback;
  try { return JSON.parse(text); } catch (e) { return fallback; }
}

function modIcon(mod) {
  if (mod.m.icon) return `<img class="modicon" src="https://mods.relic/${encodeURIComponent(mod.folder)}/${mod.m.icon.split("/").map(encodeURIComponent).join("/")}" alt="">`;
  return `<span class="modicon" style="display:grid;place-items:center">${ICONS.gem}</span>`;
}

function loadMod(entry) {
  const m = parse(entry.manifest, null);
  if (!m || typeof m !== "object") return null;
  const id = entry.folder;
  const settings = Array.isArray(m.settings) ? m.settings.filter(s => s && s.key && s.type) : [];
  const defaults = {};
  settings.forEach(s => { defaults[s.key] = s.default !== undefined ? clone(s.default) : defaultFor(s); });
  const mod = { folder: entry.folder, id, m, name: m.name || id, version: m.version || "?", settings, defaults,
    hasReport: !!entry.report, isCore: id === "RelicCore" };
  S.files[id] = parse(entry.settings, null);
  S.values[id] = mergeValues(mod, S.files[id]);
  return mod;
}

function defaultFor(s) {
  switch (s.type) {
    case "toggle": return false;
    case "slider": case "number": return s.min || 0;
    case "list": return [];
    case "picker": return s.multi ? [] : "";
    case "color": return "#e3b75c";
    case "key": return "";
    default: return "";
  }
}

function mergeValues(mod, file) {
  const v = clone(mod.defaults);
  const src = file && file.values && typeof file.values === "object" ? file.values : {};
  for (const s of mod.settings) {
    if (!(s.key in src)) continue;
    const val = src[s.key], def = mod.defaults[s.key];
    if (Array.isArray(def) ? Array.isArray(val) : typeof val === typeof def) v[s.key] = val;
  }
  return v;
}

function applySnapshot(snap) {
  S.core = Object.assign(S.core, snap.core || {});
  S.coreSet = Object.assign({ schema: 1, presets: {}, disabled: {} }, parse(snap.coreSettings, {}));
  S.mods = [];
  S.byId = {};
  for (const e of snap.mods || []) {
    const mod = loadMod(e);
    if (!mod) continue;
    S.mods.push(mod);
    S.byId[mod.id] = mod;
  }
  S.mods.sort((a, b) => (b.isCore - a.isCore) || a.name.localeCompare(b.name));
  OR.load(snap);
  if (S.view.startsWith("mod:") && !S.byId[S.view.slice(4)]) S.view = "home";
  if (S.view === "trouble" && !S.log) send({ op: "log" });
  render();
}

function save(id) {
  if (id === "_core") {
    clearTimeout(S.saveTimers._core);
    S.saveTimers._core = setTimeout(() => send({ op: "save", mod: "_core", data: S.coreSet }), 150);
    return;
  }
  const mod = S.byId[id];
  if (!mod) return;
  clearTimeout(S.saveTimers[id]);
  S.saveTimers[id] = setTimeout(() => {
    const data = { schema: 1, mod: id, version: mod.version, values: S.values[id] };
    S.files[id] = data;
    send({ op: "save", mod: id, data });
  }, 150);
}

function setValue(id, key, value, quiet) {
  S.values[id][key] = value;
  save(id);
  if (!quiet) render();
}

/* ---------- views ---------- */

function render() {
  $("#coreVersion").textContent = "Version " + (S.core.version || "");
  renderNav();
  renderFoot();
  const main = $("#main");
  const top = main.scrollTop;
  const mv = window.Mgr && Mgr.on ? Mgr.view(S.view) : null;
  if (mv != null) main.innerHTML = mv;
  else if (S.view === "home") main.innerHTML = viewHome();
  else if (S.view === "keys") main.innerHTML = viewKeys();
  else if (S.view === "trouble") main.innerHTML = viewTrouble();
  else if (S.view.startsWith("mod:")) main.innerHTML = viewMod(S.byId[S.view.slice(4)]);
  else if (S.view.startsWith("mm:")) main.innerHTML = OR.view(S.view.slice(3));
  else main.innerHTML = viewHome();
  if (window.Mgr) main.insertAdjacentHTML("beforeend", Mgr.modal());
  main.scrollTop = top;
  paintSliders();
  if (window.Mgr) Mgr.afterRender();
}

function renderNav() {
  const item = (view, icon, text, extra = "") =>
    `<button class="navitem ${S.view === view ? "on" : ""}" data-view="${esc(view)}"><span class="ico">${icon}</span><span class="txt">${esc(text)}</span>${extra}</button>`;
  const mgr = window.Mgr && Mgr.on;
  let h = mgr ? Mgr.nav(item) + `<div class="navlabel">Relic mod settings</div>` : "";
  h += item("home", ICONS.home, mgr ? "Settings home" : "Dashboard");
  if (!mgr) h += `<div class="navlabel">Relic mods</div>`;
  if (!S.mods.length && !OR.mine.length) h += `<div class="muted small" style="padding:4px 10px">No Relic mods found yet.</div>`;
  for (const mod of S.mods) {
    const off = !!S.coreSet.disabled[mod.id];
    h += item("mod:" + mod.id, mod.m.icon ? `<img src="https://mods.relic/${encodeURIComponent(mod.folder)}/${esc(mod.m.icon)}" alt="">` : ICONS.gem,
      mod.name, `<span class="dot ${off ? "off" : "ok"}" title="${off ? "Turned off" : "On"}"></span>`);
  }
  h += OR.navItems(item);
  h += `<div class="navlabel">Tools</div>`;
  h += item("keys", ICONS.keys, "Key Bindings", clashes().size ? `<span class="badge restart">${clashes().size}</span>` : "");
  h += item("trouble", ICONS.trouble, "Troubleshooting");
  $("#nav").innerHTML = h;
}

function phaseText() {
  if (!S.core.gameRunning && !S.core.phase) return "Not running";
  return { menu: "Main menu", world: "In a world", boot: "Starting" }[S.core.phase] || "Running";
}

function dllText() {
  return { loaded: "DLL", files: "Files (no DLL yet)", failed: "DLL failed, using files" }[S.core.dll] || "Waiting for the game";
}

function renderFoot() {
  $("#sidefoot").innerHTML =
    `<div class="row"><span>Game</span><b>${esc(phaseText())}</b></div>` +
    `<div class="row"><span>Core link</span><b>${esc(dllText())}</b></div>` +
    `<div class="row"><span>Open / hide</span><b>${WINDOW_KEY}</b></div>`;
}

function warnings() {
  const w = [];
  if (!HOST) w.push(["Preview mode", "This page is running outside the Relic Mod Manager window, so nothing is saved."]);
  if (S.core.dll === "failed") w.push(["Core DLL not loaded", "Relic Mod Manager is using its file link instead. Install UE4SS v4.0.0 from Nexus mod 935."]);
  if (HOST && !S.core.gameRunning) w.push(["Game not running", "You can still change settings. They apply the next time you play."]);
  if (!S.mods.some(m => !m.isCore)) w.push(["No Relic mods found", "Install a Relic mod and it will show up here."]);
  return w;
}

function viewHome() {
  const mods = S.mods;
  const on = mods.filter(m => !S.coreSet.disabled[m.id]).length;
  const pills = [
    [`Game: ${phaseText()}`, S.core.phase === "world" || S.core.phase === "menu" ? "good" : "warn"],
    [`Core link: ${dllText()}`, S.core.dll === "loaded" ? "good" : S.core.dll === "failed" ? "bad" : "warn"],
    [`${on} of ${mods.length} mods on`, "good"],
    [`Key clashes: ${clashes().size}`, clashes().size ? "bad" : "good"]
  ].map(([t, c]) => `<span class="pill ${c}"><i></i>${esc(t)}</span>`).join("");
  let h = `<div class="hero"><img src="img/gem.svg" alt=""><div class="grow"><h1>Relic Mod Manager</h1>
    <p class="muted">Every Relic mod's settings in one place. Press <b style="color:var(--gold2)">${WINDOW_KEY}</b> in game to open or hide this window.</p>
    <div class="pills">${pills}</div></div></div>`;
  const w = warnings();
  if (w.length) h += `<div class="warnbox">${w.map(([a, b]) => `<div><b>${esc(a)}:</b> ${esc(b)}</div>`).join("")}</div>`;

  const packs = packPresets();
  h += `<div class="section"><h2>Pack presets</h2></div>`;
  h += packs.length
    ? `<div class="toolbar"><span>Apply one preset to every Relic mod that has it:</span>
       <select id="packSel">${packs.map(p => `<option>${esc(p)}</option>`).join("")}</select>
       <button class="btn gold" data-act="applyPack">Apply to all mods</button></div>`
    : `<div class="empty">None of your Relic mods have pack presets yet.</div>`;

  h += `<div class="section"><h2>Your Relic mods</h2></div><div class="grid">`;
  for (const mod of mods) {
    const off = !!S.coreSet.disabled[mod.id];
    h += `<div class="card"><div class="top" data-view="mod:${esc(mod.id)}">${modIcon(mod)}<div>
        <div class="name">${esc(mod.name)}</div><div class="muted small">Version ${esc(mod.version)}</div></div></div>
      <div class="desc">${esc(mod.m.description || "")}</div>
      <div class="foot">${mod.isCore ? `<span class="muted small">Always on</span>` :
        `<label class="switch" title="Turn this mod on or off (next launch)"><input type="checkbox" data-act="modOn" data-mod="${esc(mod.id)}" ${off ? "" : "checked"}><span></span></label>`}
        ${mod.isCore ? "" : `<span class="muted small">${off ? "Off from next launch" : "On"}</span>`}
        ${mod.m.nexus ? `<button class="btn small" data-act="open" data-url="https://www.nexusmods.com/runescapedragonwilds/mods/${Number(mod.m.nexus)}">Nexus page</button>` : ""}</div></div>`;
  }
  h += `</div>`;
  h += `<div class="section"><h2>Relic Mods news</h2></div>
    <div class="toolbar"><span class="grow">Updates, help and new mods are posted in the Relic Mods Discord.</span>
    <button class="btn red" data-act="open" data-url="${DISCORD}">Join the Discord</button></div>`;
  return h;
}

function viewMod(mod) {
  if (!mod) return `<div class="empty">This mod is no longer installed.</div>`;
  const tabs = mod.isCore ? ["settings", "browse", "about"] : ["settings", "browse", "keys", "about"];
  const off = !!S.coreSet.disabled[mod.id];
  let h = `<div class="modhead">${modIcon(mod)}<div class="grow"><h1>${esc(mod.name)}</h1>
    <div class="meta">Version ${esc(mod.version)}${mod.m.author ? " · by " + esc(mod.m.author) : ""}${off ? " · <b style='color:var(--red)'>turned off from next launch</b>" : ""}</div></div>
    ${mod.m.nexus ? `<button class="btn" data-act="open" data-url="https://www.nexusmods.com/runescapedragonwilds/mods/${Number(mod.m.nexus)}">Nexus page</button>` : ""}</div>`;
  const tabName = t => t === "browse" && mod.isCore ? "File Folder" : t;
  h += `<div class="tabs">${tabs.map(t => `<button class="tab ${S.tab === t ? "on" : ""}" data-tab="${t}">${tabName(t)}</button>`).join("")}</div>`;
  if (S.tab === "settings") h += viewSettings(mod);
  else if (S.tab === "browse" && mod.isCore) h += FF.view();
  else if (S.tab === "browse") h += viewBrowse(mod);
  else if (S.tab === "keys") h += viewKeyTable(mod.settings.filter(s => s.type === "key").map(s => ({ mod, s })), false);
  else h += viewAbout(mod);
  return h;
}

function viewSettings(mod) {
  if (!mod.settings.length) return `<div class="empty">This mod has no settings.</div>`;
  const own = Object.keys(S.coreSet.presets[mod.id] || {});
  const built = Object.keys(mod.m.presets || {});
  let h = `<div class="toolbar"><span>Preset</span><select id="presetSel"><option value="">Choose…</option>
    ${built.map(p => `<option value="b:${esc(p)}">${esc(p)}</option>`).join("")}
    ${own.map(p => `<option value="o:${esc(p)}">${esc(p)} (yours)</option>`).join("")}</select>
    <button class="btn small" data-act="loadPreset" data-mod="${esc(mod.id)}">Load</button>
    <button class="btn small" data-act="savePreset" data-mod="${esc(mod.id)}">Save as preset</button>
    <button class="btn small" data-act="delPreset" data-mod="${esc(mod.id)}">Delete mine</button>
    <span class="grow"></span><button class="btn small" data-fold="open" data-mod="${esc(mod.id)}">Expand all</button><button class="btn small" data-fold="close" data-mod="${esc(mod.id)}">Collapse all</button>
    <button class="btn small" data-act="resetAll" data-mod="${esc(mod.id)}">Reset page</button></div>`;
  const groups = Array.isArray(mod.m.groups) && mod.m.groups.length ? mod.m.groups : [{ id: "", label: "Settings" }];
  const seen = new Set();
  for (const g of groups) {
    const list = mod.settings.filter(s => (s.group || "") === (g.id || "") || (!groups.some(x => x.id === s.group) && g === groups[0]));
    const fresh = list.filter(s => !seen.has(s.key));
    fresh.forEach(s => seen.add(s.key));
    if (!fresh.length) continue;
    const fk = mod.id + "|" + (g.id || g.label || "");
    const shut = S.fold[fk] !== undefined ? S.fold[fk] : true;
    h += `<div class="group ${shut ? "folded" : ""}"><h3 class="fold" data-foldkey="${esc(fk)}">${shut ? "▸" : "▾"} ${esc(g.label || g.id)} <span class="muted small">${fresh.length}</span></h3>${shut ? "" : fresh.map(s => settingRow(mod, s)).join("")}</div>`;
  }
  return h;
}

function settingRow(mod, s) {
  const v = S.values[mod.id][s.key];
  const changed = !same(v, mod.defaults[s.key]);
  const wide = s.type === "list" || s.type === "picker";
  const badges = (s.restart ? `<span class="badge restart">Needs restart</span>` : "") +
    (s.personal ? `<span class="badge personal" title="Stays yours in co-op">Personal</span>` : "") +
    (changed ? `<button class="reset" title="Back to default" data-act="resetOne" data-mod="${esc(mod.id)}" data-key="${esc(s.key)}">↺</button>` : "");
  return `<div class="setting ${wide ? "wide" : ""}"><div><div class="label">${esc(s.label || s.key)} ${badges}</div>
    ${s.tooltip ? `<div class="tip">${esc(s.tooltip)}</div>` : ""}</div><div class="ctl">${control(mod, s, v)}</div></div>`;
}

function attrs(mod, s) { return `data-mod="${esc(mod.id)}" data-key="${esc(s.key)}"`; }

function fmt(s, v) {
  const d = s.decimals != null ? s.decimals : (String(s.step || 1).split(".")[1] || "").length;
  return Number(v).toFixed(d) + (s.suffix || "");
}

function control(mod, s, v) {
  const a = attrs(mod, s);
  switch (s.type) {
    case "toggle":
      return `<label class="switch"><input type="checkbox" data-ctl="toggle" ${a} ${v ? "checked" : ""}><span></span></label>`;
    case "slider":
      return `<div class="slider"><input type="range" data-ctl="slider" ${a} min="${s.min}" max="${s.max}" step="${s.step || 1}" value="${v}"><output>${esc(fmt(s, v))}</output></div>`;
    case "choice": {
      const opts = (s.options || []).map(o => typeof o === "object" ? o : { value: o, label: o });
      return `<select data-ctl="choice" ${a}>${opts.map(o => `<option value="${esc(o.value)}" ${String(o.value) === String(v) ? "selected" : ""}>${esc(o.label)}</option>`).join("")}</select>`;
    }
    case "key": {
      const listening = S.capture && S.capture.mod === mod.id && S.capture.key === s.key;
      const clash = clashes().has(norm(v));
      return `<button class="keycap ${listening ? "listen" : ""} ${clash ? "clash" : ""}" data-act="capture" ${a}>${listening ? "Press a key…" : esc(v || "None")}</button>
        ${v ? `<button class="btn small" data-act="clearKey" ${a}>Clear</button>` : ""}`;
    }
    case "color": {
      const sw = (s.swatches || ["#e3b75c", "#c8102e", "#ece6d6", "#4fc77b", "#5aa2ff", "#a970ff"]);
      return `<div class="swatches">${sw.map(c => `<button style="background:${esc(c)}" data-act="swatch" data-color="${esc(c)}" ${a}></button>`).join("")}</div>
        <label><button class="swatch" style="background:${esc(v)}" data-act="pickColor" ${a}></button><input type="color" data-ctl="color" ${a} value="${esc(v)}"></label>`;
    }
    case "text":
      return `<input type="text" data-ctl="text" ${a} value="${esc(v)}" maxlength="${s.maxLength || 120}">`;
    case "number":
      return `<input type="number" data-ctl="number" ${a} value="${esc(v)}" ${s.min != null ? `min="${s.min}"` : ""} ${s.max != null ? `max="${s.max}"` : ""} step="${s.step || "any"}">${s.suffix ? `<span class="muted">${esc(s.suffix)}</span>` : ""}`;
    case "list": {
      const items = Array.isArray(v) ? v : [];
      return `<div class="listedit">${items.map((it, i) => `<div class="li"><input type="text" data-ctl="listItem" data-i="${i}" ${a} value="${esc(it)}">
        <button class="btn small" data-act="listUp" data-i="${i}" ${a} ${i === 0 ? "disabled" : ""}>▲</button>
        <button class="btn small" data-act="listDown" data-i="${i}" ${a} ${i === items.length - 1 ? "disabled" : ""}>▼</button>
        <button class="btn small" data-act="listDel" data-i="${i}" ${a}>Remove</button></div>`).join("")}
        <div><button class="btn small" data-act="listAdd" ${a}>Add row</button></div></div>`;
    }
    case "picker": {
      const opts = (s.options || []).map(o => typeof o === "object" ? o : { value: o, label: o });
      const sel = s.multi ? (Array.isArray(v) ? v : []) : [v];
      return `<div class="picker"><input type="search" placeholder="Search…" data-ctl="pickSearch" ${a}>
        <div class="opts">${opts.map(o => `<button class="opt ${sel.includes(o.value) ? "on" : ""}" data-act="pick" data-value="${esc(o.value)}" data-label="${esc((o.label || "").toLowerCase())}" ${a}>
        ${o.icon ? `<img src="https://mods.relic/${encodeURIComponent(mod.folder)}/${esc(o.icon)}" alt="">` : ""}<span>${esc(o.label || o.value)}</span></button>`).join("")}</div></div>`;
    }
    default:
      return `<span class="muted small">Unknown setting type "${esc(s.type)}"</span>`;
  }
}

function viewBrowse(mod) {
  const pages = Array.isArray(mod.m.browse) ? mod.m.browse : [];
  if (!pages.length) return `<div class="empty">This mod has no browse pages yet.</div>`;
  return pages.map(p => `<div class="group"><h3>${esc(p.label || p.id)}</h3><div class="setting wide"><div class="tip">${esc(p.description || "")}</div></div></div>`).join("");
}

function viewAboutManager() {
  const row = (t, d) => `<div class="setting wide"><div><div class="label">${t}</div><div class="tip">${d}</div></div></div>`;
  return `<div class="group"><h3>What is Relic Mod Manager?</h3>
    <div class="setting wide"><div>Relic Mod Manager is a free mod manager made only for <b>RuneScape: Dragonwilds</b>. It is here so anyone can mod the game without knowing where files go:
      find a mod, drop it in, press Play. No Mod Organizer 2, no Vortex and no setup.</div></div></div>
    <div class="group"><h3>What it does for you</h3>
    ${row("Install Mods", "Drag and drop a mod's .zip, .7z or .rar file. Relic Mod Manager reads it and puts every file in the right game folder: UE4SS Lua mods, paks, RuneSchema mods, even UE4SS itself.")}
    ${row("Library", "All your mods in one list. Turn a mod on or off with one switch, change the load order, Auto-Sort it, keep notes, and switch between the Modded and Vanilla profiles.")}
    ${row("Your mods stay safe", "Mods are kept in their own storage folder and linked into the game. Turning a mod off never deletes it, and the game's own files are backed up before anything is replaced.")}
    ${row("Relic mod settings", "Change the settings of every Relic mod in one window, even while you play. Press Shift+F7 in game to open or hide it.")}
    ${row("Troubleshooting", "After a crash it reads the game's crash files and tells you, in plain words, what went wrong and which mod most likely caused it.")}
    ${row("Nexus Mods", "Browse RuneScape: Dragonwilds mods on Nexus, newest first, and open any mod in your own browser.")}</div>
    <div class="group"><h3>Made by</h3>
    <div class="setting wide"><div>Relic Hunter, with Claude Ai. Help, updates and new mods are posted in the Relic Mods Discord.</div>
      <div class="ctl"><button class="btn red small" data-act="open" data-url="${DISCORD}">Join the Discord</button></div></div></div>`;
}

function viewAbout(mod) {
  const files = S.files[mod.id];
  return (mod.isCore ? viewAboutManager() : "") + `<div class="group"><h3>${mod.isCore ? "Files" : "About"}</h3>
    ${mod.isCore ? "" : `<div class="setting wide"><div>${esc(mod.m.description || "No description.")}</div></div>`}
    <div class="setting"><div class="label">Folder</div><div class="ctl muted">${esc(mod.folder)}</div></div>
    <div class="setting"><div class="label">Saved settings</div><div class="ctl muted">${files ? "RelicCore\\settings\\" + esc(mod.id) + ".json" : "Not saved yet (defaults)"}</div></div>
    ${mod.m.imported ? "" : ""}
    <div class="setting"><div class="label">Report</div><div class="ctl">${mod.hasReport ? `<button class="btn small" data-act="openReport" data-mod="${esc(mod.id)}">Show report</button>` : `<span class="muted">No report yet</span>`}</div></div></div>`;
}

/* ---------- keys ---------- */

function norm(k) { return String(k || "").replace(/\s+/g, "").toLowerCase(); }

function allKeys() {
  const out = [{ mod: null, s: { key: "_window", label: "Open / hide Relic Mod Manager" }, value: WINDOW_KEY, fixed: true }];
  for (const mod of S.mods) {
    if (S.coreSet.disabled[mod.id]) continue;
    for (const s of mod.settings) if (s.type === "key") out.push({ mod, s, value: S.values[mod.id][s.key] });
    for (const k of mod.m.keys || []) out.push({ mod, s: { key: k.action, label: k.label }, value: k.default, fixed: true });
  }
  return out;
}

function clashes() {
  const count = new Map();
  for (const k of allKeys()) { const n = norm(k.value); if (n) count.set(n, (count.get(n) || 0) + 1); }
  return new Set([...count].filter(([, c]) => c > 1).map(([n]) => n));
}

function viewKeys() {
  return `<h1>Key Bindings</h1><p class="muted" style="margin:6px 0 16px">Every Relic mod's hotkeys. Keys used twice are marked in red. Click a key to change it.</p>` +
    viewKeyTable(allKeys().map(k => Object.assign({}, k)), true);
}

function viewKeyTable(rows, showMod) {
  if (!rows.length) return `<div class="empty">No key bindings here.</div>`;
  const bad = clashes();
  return `<div class="group" style="padding:4px 8px"><table><tr>${showMod ? "<th>Mod</th>" : ""}<th>Action</th><th>Key</th><th></th></tr>
    ${rows.map(r => {
      const mod = r.mod, v = r.value !== undefined ? r.value : S.values[mod.id][r.s.key];
      const clash = bad.has(norm(v));
      return `<tr class="${clash ? "clash" : ""}">${showMod ? `<td>${esc(mod ? mod.name : "Relic Mod Manager")}</td>` : ""}<td>${esc(r.s.label || r.s.key)}</td>
        <td>${r.fixed || !mod ? `<span class="keycap ${clash ? "clash" : ""}" style="cursor:default">${esc(v || "None")}</span>` : control(mod, r.s, v)}</td>
        <td>${clash ? `<span class="badge restart">Used twice</span>` : ""}</td></tr>`;
    }).join("")}</table></div>`;
}

function keyName(e) {
  const map = { " ": "Space", ArrowUp: "Up", ArrowDown: "Down", ArrowLeft: "Left", ArrowRight: "Right", Escape: "Esc" };
  let k = map[e.key] || (e.key.length === 1 ? e.key.toUpperCase() : e.key);
  if (["Shift", "Control", "Alt", "Meta"].includes(k)) return null;
  const mods = [];
  if (e.ctrlKey) mods.push("Ctrl");
  if (e.shiftKey) mods.push("Shift");
  if (e.altKey) mods.push("Alt");
  return mods.concat(k).join("+");
}

/* ---------- troubleshooting ---------- */

function viewTrouble() {
  const withReport = S.mods.filter(m => m.hasReport);
  let h = `<h1>Troubleshooting</h1><p class="muted" style="margin:6px 0 16px">What crashed your game, each mod's report and the errors from UE4SS.log, in one place.</p>` + Crash.html() + `<div class="section"><h2>Mod reports</h2></div>
    <div class="toolbar"><span class="grow">Asking for help? Copy this summary into the Relic Mods Discord.</span>
    <button class="btn gold" data-act="copySummary">Copy for Discord</button><button class="btn" data-act="refreshLog">Refresh</button></div>`;
  h += `<div class="split"><div class="list">${withReport.length ? withReport.map(m =>
      `<button class="navitem ${S.reportMod === m.id ? "on" : ""}" data-act="openReport" data-mod="${esc(m.id)}"><span class="ico">${ICONS.gem}</span><span class="txt">${esc(m.name)}</span></button>`).join("")
      : `<div class="empty">No reports yet. Reports appear after you play.</div>`}</div><div>`;
  if (S.reportMod) h += `<h2 style="margin-bottom:8px">${esc((S.byId[S.reportMod] || {}).name || S.reportMod)} report</h2><pre class="report">${esc(S.reports[S.reportMod] || "Loading…")}</pre>`;
  else h += `<div class="empty">Pick a mod on the left to read its report.</div>`;
  h += `</div></div><div class="section"><h2>UE4SS.log problems</h2></div>`;
  if (!S.log) h += `<div class="empty">Loading…</div>`;
  else if (!S.log.found) h += `<div class="empty">UE4SS.log was not found next to the Mods folder.</div>`;
  else h += `<p class="muted small" style="margin-bottom:8px">${esc(S.log.head)}</p><pre class="report">${S.log.lines.length ? esc(S.log.lines.join("\n")) : "No errors found."}</pre>`;
  return h;
}

function summary() {
  const lines = [`Relic Mod Manager ${S.core.version} · game: ${phaseText()} · core link: ${dllText()}`];
  for (const m of S.mods) lines.push(`- ${m.name} ${m.version}${S.coreSet.disabled[m.id] ? " (off)" : ""}`);
  if (S.log && S.log.found) {
    lines.push(S.log.head.split("\n").filter(l => /UE4SS - v/.test(l)).join("") || "");
    const errs = S.log.lines.slice(-15);
    if (errs.length) lines.push("UE4SS.log errors:", ...errs);
  }
  return "```\n" + lines.filter(Boolean).join("\n") + "\n```";
}

/* ---------- presets ---------- */

function packPresets() {
  const names = new Set();
  for (const m of S.mods) Object.keys(m.m.presets || {}).forEach(n => names.add(n));
  return [...names];
}

function applyPreset(mod, values) {
  for (const s of mod.settings) if (values && s.key in values && !s.personal) S.values[mod.id][s.key] = clone(values[s.key]);
  save(mod.id);
}

/* ---------- events ---------- */

document.addEventListener("click", e => {
  const oa = e.target.closest("[data-oract]");
  if (oa && e.target.closest("input, select") == null && OR.onClick(oa)) { e.preventDefault(); return; }
  const ca = e.target.closest("[data-cract]");
  if (ca && Crash.onClick(ca)) { e.preventDefault(); return; }
  const rb = e.target.closest("[data-rbact]");
  if (rb && RB.onClick(rb)) { e.preventDefault(); return; }
  const ma = e.target.closest("[data-mact]");
  if (ma && window.Mgr && e.target.closest("input, select") == null && Mgr.onClick(ma)) { e.preventDefault(); return; }
  const v = e.target.closest("[data-view]");
  if (v && v.tagName === "A") e.preventDefault();
  if (v && !e.target.closest("[data-act]") && !e.target.closest(".switch")) {
    S.view = v.dataset.view;
    S.tab = "settings";
    if (S.view === "trouble" && !S.log) send({ op: "log" });
    render();
    $("#main").scrollTop = 0;
    return;
  }
  const fh = e.target.closest("[data-foldkey]");
  if (fh) { const k = fh.dataset.foldkey; S.fold[k] = !(S.fold[k] !== undefined ? S.fold[k] : true); render(); return; }
  const fa = e.target.closest("[data-fold]");
  if (fa) {
    const mod = S.byId[fa.dataset.mod];
    if (mod) { const groups = Array.isArray(mod.m.groups) && mod.m.groups.length ? mod.m.groups : [{ id: "", label: "Settings" }]; for (const g of groups) S.fold[mod.id + "|" + (g.id || g.label || "")] = fa.dataset.fold === "close"; render(); }
    return;
  }
  const t = e.target.closest("[data-tab]");
  if (t) { S.tab = t.dataset.tab; render(); return; }
  const a = e.target.closest("[data-act]");
  if (!a) return;
  const d = a.dataset, mod = S.byId[d.mod];
  const s = mod && mod.settings.find(x => x.key === d.key);
  switch (d.act) {
    case "open": send({ op: "open", url: d.url }); break;
    case "capture": S.capture = { mod: d.mod, key: d.key }; render(); break;
    case "clearKey": setValue(d.mod, d.key, ""); break;
    case "swatch": setValue(d.mod, d.key, d.color); break;
    case "pickColor": { const inp = a.parentElement.querySelector("input[type=color]"); if (inp) inp.click(); break; }
    case "resetOne": setValue(d.mod, d.key, clone(mod.defaults[d.key])); toast("Back to default"); break;
    case "resetAll": S.values[d.mod] = clone(mod.defaults); save(d.mod); render(); toast("Page reset"); break;
    case "listAdd": { const l = (S.values[d.mod][d.key] || []).slice(); l.push(""); setValue(d.mod, d.key, l); break; }
    case "listDel": { const l = S.values[d.mod][d.key].slice(); l.splice(+d.i, 1); setValue(d.mod, d.key, l); break; }
    case "listUp": case "listDown": {
      const l = S.values[d.mod][d.key].slice(), i = +d.i, j = d.act === "listUp" ? i - 1 : i + 1;
      [l[i], l[j]] = [l[j], l[i]]; setValue(d.mod, d.key, l); break;
    }
    case "pick": {
      if (s && s.multi) {
        const l = (S.values[d.mod][d.key] || []).slice(), k = l.indexOf(d.value);
        if (k >= 0) l.splice(k, 1); else l.push(d.value);
        setValue(d.mod, d.key, l);
      } else setValue(d.mod, d.key, d.value);
      break;
    }
    case "loadPreset": {
      const sel = $("#presetSel").value;
      if (!sel) return toast("Choose a preset first");
      const name = sel.slice(2), vals = sel[0] === "b" ? (mod.m.presets || {})[name] : (S.coreSet.presets[mod.id] || {})[name];
      applyPreset(mod, vals); render(); toast("Preset loaded: " + name); break;
    }
    case "savePreset": {
      const name = (prompt("Name for this preset:") || "").trim().slice(0, 40);
      if (!name) return;
      S.coreSet.presets[mod.id] = S.coreSet.presets[mod.id] || {};
      S.coreSet.presets[mod.id][name] = clone(S.values[mod.id]);
      save("_core"); render(); toast("Preset saved"); break;
    }
    case "delPreset": {
      const sel = $("#presetSel").value;
      if (!sel || sel[0] !== "o") return toast("Choose one of your own presets");
      delete S.coreSet.presets[mod.id][sel.slice(2)];
      save("_core"); render(); toast("Preset deleted"); break;
    }
    case "applyPack": {
      const name = $("#packSel").value; let n = 0;
      for (const m of S.mods) if ((m.m.presets || {})[name]) { applyPreset(m, m.m.presets[name]); n++; }
      render(); toast(`${name} applied to ${n} mod${n === 1 ? "" : "s"}`); break;
    }
    case "openReport": S.view = "trouble"; S.reportMod = d.mod; send({ op: "report", mod: d.mod }); if (!S.log) send({ op: "log" }); render(); break;
    case "refreshLog": send({ op: "log" }); if (S.reportMod) send({ op: "report", mod: S.reportMod }); break;
    case "copySummary": send({ op: "copy", text: summary() }); if (!HOST) toast("Copy works in the Relic Mod Manager window"); break;
  }
});

document.addEventListener("change", e => {
  const el = e.target, d = el.dataset;
  if (OR.onChange(el) || RB.onChange(el)) return;
  if (d.mact && window.Mgr && Mgr.onChange(el)) return;
  if (d.act === "modOn") {
    if (el.checked) delete S.coreSet.disabled[d.mod]; else S.coreSet.disabled[d.mod] = true;
    save("_core"); render(); toast(el.checked ? "On from next launch" : "Off from next launch");
    return;
  }
  const mod = S.byId[d.mod];
  if (!mod || !d.ctl) return;
  const s = mod.settings.find(x => x.key === d.key);
  switch (d.ctl) {
    case "toggle": setValue(d.mod, d.key, el.checked); break;
    case "choice": {
      const opt = (s.options || []).map(o => typeof o === "object" ? o.value : o).find(o => String(o) === el.value);
      setValue(d.mod, d.key, opt !== undefined ? opt : el.value); break;
    }
    case "color": setValue(d.mod, d.key, el.value); break;
    case "text": setValue(d.mod, d.key, el.value); break;
    case "number": {
      let n = Number(el.value); if (!isFinite(n)) n = mod.defaults[d.key];
      if (s.min != null) n = Math.max(s.min, n); if (s.max != null) n = Math.min(s.max, n);
      setValue(d.mod, d.key, n); break;
    }
    case "listItem": { const l = S.values[d.mod][d.key].slice(); l[+d.i] = el.value; setValue(d.mod, d.key, l, true); break; }
    case "slider": render(); break;
  }
});

document.addEventListener("input", e => {
  const el = e.target, d = el.dataset;
  if (OR.onInput(el)) return;
  if (d.ctl === "slider") {
    const mod = S.byId[d.mod], s = mod.settings.find(x => x.key === d.key);
    const n = Number(el.value);
    S.values[d.mod][d.key] = n;
    el.nextElementSibling.textContent = fmt(s, n);
    paintSlider(el);
    save(d.mod);
  } else if (d.ctl === "pickSearch") {
    const q = el.value.trim().toLowerCase();
    el.parentElement.querySelectorAll(".opt").forEach(o => { o.style.display = !q || o.dataset.label.includes(q) || o.dataset.value.toLowerCase().includes(q) ? "" : "none"; });
  }
});

document.addEventListener("keydown", e => {
  if (OR.onKey(e)) return;
  if (S.capture) {
    e.preventDefault();
    if (e.key === "Escape") { S.capture = null; render(); return; }
    const name = keyName(e);
    if (!name) return;
    const c = S.capture; S.capture = null;
    if (norm(name) === norm(WINDOW_KEY)) { toast(WINDOW_KEY + " opens Relic Mod Manager"); render(); return; }
    setValue(c.mod, c.key, name);
    return;
  }
  if (e.key === "Escape" && !e.target.matches("input, select")) { e.preventDefault(); send({ op: "hide" }); }
});

function paintSlider(el) {
  const min = +el.min, max = +el.max, p = max > min ? ((+el.value - min) / (max - min)) * 100 : 0;
  el.style.setProperty("--p", p + "%");
}
function paintSliders() { document.querySelectorAll("input[type=range]").forEach(paintSlider); }

/* ---------- host messages ---------- */

const HASH = decodeURIComponent(location.hash.slice(1));
if (HASH && HASH !== "selftest") { const [view, tab] = HASH.split("|"); S.view = view; if (tab) S.tab = tab; }

function selfTest() {
  const mod = S.mods.find(m => !m.isCore && m.settings.some(s => s.type === "toggle"));
  if (!mod) return send({ op: "selftest", ok: false, text: "no mod with a toggle" });
  const s = mod.settings.find(x => x.type === "toggle");
  const want = !S.values[mod.id][s.key];
  const box = document.querySelector(`input[data-ctl="toggle"][data-key="${s.key}"]`);
  S.view = "mod:" + mod.id; S.tab = "settings"; render();
  const el = document.querySelector(`input[data-ctl="toggle"][data-mod="${mod.id}"][data-key="${s.key}"]`) || box;
  if (!el) return send({ op: "selftest", ok: false, text: "toggle not on the page" });
  el.click();
  setTimeout(() => send({ op: "selftest", ok: S.values[mod.id][s.key] === want, mod: mod.id, key: s.key, value: want }), 500);
}

if (HOST) {
  HOST.addEventListener("message", ev => {
    const m = ev.data;
    if (!m || !m.op) return;
    switch (m.op) {
      case "snapshot": if (!m.core.fromGame && S.view === "home" && !applySnapshot.done && !HASH) S.view = "library"; applySnapshot.done = true; applySnapshot(m); if (HASH === "selftest") setTimeout(selfTest, 300); break;
      case "game": Object.assign(S.core, { phase: m.phase, dll: m.dll, gameRunning: m.gameRunning }); render(); break;
      case "settings": {
        if (m.mod === "_core") { S.coreSet = Object.assign({ schema: 1, presets: {}, disabled: {} }, parse(m.text, {})); render(); break; }
        const mod = S.byId[m.mod];
        if (!mod) { send({ op: "init" }); break; }
        S.files[m.mod] = parse(m.text, null);
        const fresh = mergeValues(mod, S.files[m.mod]);
        if (!same(fresh, S.values[m.mod])) { S.values[m.mod] = fresh; if (!document.activeElement || !document.activeElement.matches("input[type=text], input[type=number]")) render(); }
        break;
      }
      case "report": S.reports[m.mod] = m.text || "(empty report)"; render(); break;
      case "log": S.log = m; render(); break;
      case "toast": toast(m.text, m.bad); break;
      case "crash.report": Crash.onReport(m); break;
      case "crash.list": Crash.onList(m); break;
      case "files.tree": FF.onTree(m); break;
      case "error": toast("Problem: " + m.text); break;
      case "shown": break;
      default: if (window.Mgr) Mgr.onMessage(m);
    }
  });
  send({ op: "init" });
} else {
  fetch("demo.json").then(r => r.json()).then(applySnapshot).catch(() => applySnapshot({ core: { version: "preview" }, mods: [] }));
}
