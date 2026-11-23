// Made by Claude Ai & Relic Hunter
"use strict";

const OR = {
  list: [], byFolder: {}, vals: {}, pend: {}, timers: {}, folded: {}, capture: null, modMenuKey: null,

  load(snap) {
    this.modMenuKey = snap.modMenuKey || null;
    this.list = [];
    this.byFolder = {};
    for (const e of snap.others || []) {
      let m = null;
      try { m = JSON.parse(e.manifest || "null"); } catch (x) { m = null; }
      const settings = m && Array.isArray(m.settings) ? m.settings.filter(s => s && s.type) : [];
      const mod = { folder: e.folder, id: e.id || e.folder, name: e.name || e.folder, author: e.author || "", ours: !!e.ours, enabled: !!e.enabled,
        error: e.error || (m ? null : "modmenu file couldn't be read"), m: m || {}, settings };
      const raw = e.values || {};
      const v = {};
      for (const s of settings) {
        if (!s.key || s.type === "header" || s.type === "button") continue;
        v[s.key] = this.typed(s, raw[s.key] !== undefined ? raw[s.key] : s.default);
      }
      if (!this.pend[mod.folder]) this.vals[mod.folder] = v;
      this.list.push(mod);
      this.byFolder[mod.folder] = mod;
    }
    this.list.sort((a, b) => a.name.localeCompare(b.name));
    if (S.view.startsWith("mm:") && !this.byFolder[S.view.slice(3)]) S.view = "home";
  },

  typed(s, v) {
    if (v === undefined || v === null) {
      if (s.type === "toggle") return false;
      if (s.type === "slider" || s.type === "number") return s.min || 0;
      return "";
    }
    if (s.type === "toggle") return v === true || String(v).toLowerCase() === "true";
    if (s.type === "slider" || s.type === "number") { const n = Number(v); return isFinite(n) ? n : (s.default || 0); }
    return String(v);
  },

  get mine() { return this.list.filter(m => m.ours); },
  get others() { return this.list.filter(m => !m.ours); },
  get hasModMenu() { return this.list.some(m => m.folder.toLowerCase() === "modmenu" && m.enabled); },

  navItems(item) {
    let h = "";
    for (const m of this.mine) h += item("mm:" + m.folder, ICONS.gem, m.name, `<span class="dot ${m.enabled ? "ok" : "off"}" title="${m.enabled ? "On" : "Turned off"}"></span>`);
    return h;
  },

  shown(mod, s) {
    if (!s.showIf || !s.showIf.key) return true;
    const cur = this.vals[mod.folder][s.showIf.key];
    const want = Array.isArray(s.showIf.is) ? s.showIf.is : [s.showIf.is];
    return want.some(w => String(w) === String(cur));
  },

  view(folder) {
    const mod = this.byFolder[folder];
    if (!mod) return `<div class="empty">This mod is no longer installed.</div>`;
    let h = `<div class="modhead"><span class="modicon" style="display:grid;place-items:center">${mod.ours ? ICONS.gem : ICONS.others}</span><div class="grow"><h1>${esc(mod.name)}</h1>
      <div class="meta">${mod.m.version ? "Version " + esc(mod.m.version) + " · " : ""}${mod.author ? "by " + esc(mod.author) + " · " : ""}settings from its modmenu.json${mod.enabled ? "" : " · <b style='color:var(--red)'>turned off</b>"}</div></div></div>`;
    if (mod.m.description) h += `<p class="muted" style="margin:-6px 0 14px">${esc(mod.m.description)}</p>`;
    if (this.hasModMenu && !mod.ours) {
      h += `<div class="infobar"><span class="grow">Mod Menu is installed too. They work together: changes here reach your mods live through Mod Menu.${this.modMenuKey ? " Don't need Mod Menu's in-game menu any more? You can turn it off." : ""}</span>
        ${this.modMenuKey ? `<button class="btn small" data-oract="mmOff">Turn off Mod Menu</button>` : ""}</div>`;
    }
    if (mod.error) return h + `<div class="warnbox"><div><b>Can't show this mod's settings:</b> ${esc(mod.error)}</div></div>`;
    if (!mod.settings.length) return h + `<div class="empty">This mod has no settings.</div>`;
    h += `<div class="toolbar"><span class="grow muted small">Changes save to the mod's own settings file right away${HOST && S.core.gameRunning ? " and reach the game live where the mod supports it" : ""}. Rows marked ✱ apply after restarting the game.</span>
      <button class="btn small" data-oract="foldAll" data-v="0" data-folder="${esc(mod.folder)}">Expand all</button><button class="btn small" data-oract="foldAll" data-v="1" data-folder="${esc(mod.folder)}">Collapse all</button>
      <button class="btn small" data-oract="resetAll" data-folder="${esc(mod.folder)}">Reset page</button></div>`;
    let open = null, folded = false;
    const close = () => (open !== null ? `</div>` : "");
    for (const s of mod.settings) {
      if (s.type === "header") {
        h += close();
        const key = mod.folder + "|" + s.label;
        folded = this.folded[key] !== undefined ? this.folded[key] : true;
        h += `<div class="group ${folded ? "folded" : ""}"><h3 class="fold" data-oract="fold" data-fkey="${esc(key)}">${folded ? "▸" : "▾"} ${esc(s.label || "")}</h3>`;
        open = key;
        continue;
      }
      if (open === null) {
        const key = mod.folder + "|";
        folded = this.folded[key] !== undefined ? this.folded[key] : true;
        h += `<div class="group ${folded ? "folded" : ""}"><h3 class="fold" data-oract="fold" data-fkey="${esc(key)}">${folded ? "▸" : "▾"} Settings</h3>`; open = key;
      }
      if (folded || !this.shown(mod, s)) continue;
      h += this.row(mod, s);
    }
    return h + close();
  },

  row(mod, s) {
    const a = `data-folder="${esc(mod.folder)}" data-key="${esc(s.key || "")}"`;
    const tags = (s.restart ? `<span class="badge restart" title="Applies after restarting the game">✱ restart</span>` : "") +
      (s.runsOn || mod.m.runsOn ? `<span class="badge personal" title="Where this setting counts in co-op">${esc(s.runsOn || mod.m.runsOn)}</span>` : "");
    if (s.type === "button")
      return `<div class="setting"><div><div class="label">${esc(s.label || s.action)} ${tags}</div>${s.tooltip ? `<div class="tip">${esc(s.tooltip)}</div>` : ""}</div>
        <div class="ctl"><button class="btn small gold" data-oract="action" data-id="${esc(mod.id)}" data-action="${esc(s.action || "")}">${esc(s.label || "Run")}</button></div></div>`;
    const v = this.vals[mod.folder][s.key];
    const def = this.typed(s, s.default);
    const changed = String(v) !== String(def);
    const reset = changed ? `<button class="reset" title="Back to default" data-oract="reset" ${a}>↺</button>` : "";
    return `<div class="setting"><div><div class="label">${esc(s.label || s.key)} ${tags}${reset}</div>${s.tooltip ? `<div class="tip">${esc(s.tooltip)}</div>` : ""}</div>
      <div class="ctl">${this.control(mod, s, v, a)}</div></div>`;
  },

  label(s, v) {
    const vl = s.valueLabels || {};
    const d = s.decimals != null ? s.decimals : (String(s.step || 1).split(".")[1] || "").length;
    const key = String(Number(v));
    if (vl[key] !== undefined) return vl[key];
    return Number(v).toFixed(d) + (s.suffix || "");
  },

  control(mod, s, v, a) {
    switch (s.type) {
      case "toggle": return `<label class="switch"><input type="checkbox" data-orctl="toggle" ${a} ${v ? "checked" : ""}><span></span></label>`;
      case "slider": case "number":
        return `<div class="slider"><input type="range" data-orctl="slider" ${a} min="${Number(s.min) || 0}" max="${s.max != null ? Number(s.max) : 100}" step="${Number(s.step) || 1}" value="${v}"><output>${esc(this.label(s, v))}</output></div>`;
      case "choice": {
        const vl = s.valueLabels || {};
        return `<select data-orctl="choice" ${a}>${(s.options || []).map(o => `<option value="${esc(o)}" ${String(o) === String(v) ? "selected" : ""}>${esc(vl[o] !== undefined ? vl[o] : o)}</option>`).join("")}</select>`;
      }
      case "key": {
        const listening = this.capture && this.capture.folder === mod.folder && this.capture.key === s.key;
        return `<button class="keycap ${listening ? "listen" : ""}" data-oract="capture" ${a}>${listening ? "Press a key…" : esc(v && v !== "none" ? v : "None")}</button>
          ${v && v !== "none" ? `<button class="btn small" data-oract="clearKey" ${a}>Clear</button>` : ""}`;
      }
      case "text": return `<input type="text" data-orctl="text" ${a} value="${esc(v)}" maxlength="${Math.min(200, s.maxLength || 200)}">`;
      default: return `<span class="muted small">Unknown setting type "${esc(s.type)}"</span>`;
    }
  },

  set(folder, key, value, quiet) {
    this.vals[folder][key] = value;
    this.pend[folder] = this.pend[folder] || {};
    this.pend[folder][key] = value;
    clearTimeout(this.timers[folder]);
    this.timers[folder] = setTimeout(() => {
      const values = this.pend[folder]; delete this.pend[folder];
      send({ op: "mm.save", folder, values });
    }, 200);
    if (!quiet) render();
  },

  ue4ssKey(e) {
    const k = e.key, c = e.code || "";
    if (/^F([1-9]|1\d|2[0-4])$/.test(k)) return k;
    if (/^Key[A-Z]$/.test(c)) return c.slice(3);
    const digits = ["ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE"];
    if (/^Digit\d$/.test(c)) return digits[+c.slice(5)];
    if (/^Numpad\d$/.test(c)) return "NUM_" + digits[+c.slice(6)];
    const map = { Space: "SPACE", Enter: "RETURN", NumpadEnter: "RETURN", Tab: "TAB", Backspace: "BACKSPACE", Insert: "INS", Delete: "DEL", Home: "HOME", End: "END",
      PageUp: "PAGE_UP", PageDown: "PAGE_DOWN", ArrowUp: "UP_ARROW", ArrowDown: "DOWN_ARROW", ArrowLeft: "LEFT_ARROW", ArrowRight: "RIGHT_ARROW",
      Semicolon: "OEM_ONE", Equal: "OEM_PLUS", Comma: "OEM_COMMA", Minus: "OEM_MINUS", Period: "OEM_PERIOD", Slash: "OEM_TWO", Backquote: "OEM_THREE",
      BracketLeft: "OEM_FOUR", Backslash: "OEM_FIVE", BracketRight: "OEM_SIX", Quote: "OEM_SEVEN", NumpadMultiply: "MULTIPLY", NumpadAdd: "ADD",
      NumpadSubtract: "SUBTRACT", NumpadDecimal: "DECIMAL", NumpadDivide: "DIVIDE", Pause: "PAUSE", CapsLock: "CAPS_LOCK", ScrollLock: "SCROLL_LOCK", PrintScreen: "PRINT_SCREEN" };
    return map[c] || null;
  },

  onClick(a) {
    const d = a.dataset;
    switch (d.oract) {
      case "fold": this.folded[d.fkey] = !(this.folded[d.fkey] !== undefined ? this.folded[d.fkey] : true); render(); return true;
      case "foldAll": {
        const mod = this.byFolder[d.folder]; if (!mod) return true;
        this.folded[mod.folder + "|"] = d.v === "1";
        for (const s of mod.settings) if (s.type === "header") this.folded[mod.folder + "|" + s.label] = d.v === "1";
        render(); return true;
      }
      case "action": send({ op: "mm.action", id: d.id, action: d.action }); return true;
      case "capture": this.capture = { folder: d.folder, key: d.key }; render(); return true;
      case "clearKey": this.set(d.folder, d.key, "none"); return true;
      case "reset": { const mod = this.byFolder[d.folder], s = mod.settings.find(x => x.key === d.key); this.set(d.folder, d.key, this.typed(s, s.default)); toast("Back to default"); return true; }
      case "resetAll": {
        const mod = this.byFolder[d.folder];
        for (const s of mod.settings) if (s.key && s.type !== "header" && s.type !== "button") this.set(d.folder, s.key, this.typed(s, s.default), true);
        render(); toast("Page reset"); return true;
      }
      case "mmOff": if (this.modMenuKey && confirm("Turn off Mod Menu? Relic Mod Manager keeps every mod's settings here. You can turn it back on in the Library.")) send({ op: "mm.modmenuOff" }); return true;
    }
    return false;
  },

  onChange(el) {
    const d = el.dataset;
    if (!d.orctl) return false;
    const mod = this.byFolder[d.folder]; if (!mod) return true;
    const s = mod.settings.find(x => x.key === d.key); if (!s) return true;
    if (d.orctl === "toggle") this.set(d.folder, d.key, el.checked);
    else if (d.orctl === "choice") this.set(d.folder, d.key, el.value);
    else if (d.orctl === "text") this.set(d.folder, d.key, el.value, true);
    else if (d.orctl === "slider") render();
    return true;
  },

  onInput(el) {
    const d = el.dataset;
    if (d.orctl !== "slider") return false;
    const mod = this.byFolder[d.folder], s = mod.settings.find(x => x.key === d.key);
    const n = Number(el.value);
    el.nextElementSibling.textContent = this.label(s, n);
    paintSlider(el);
    this.set(d.folder, d.key, n, true);
    return true;
  },

  onKey(e) {
    if (!this.capture) return false;
    e.preventDefault();
    if (e.key === "Escape") { this.capture = null; render(); return true; }
    if (["Shift", "Control", "Alt", "Meta"].includes(e.key)) return true;
    if (e.ctrlKey || e.altKey || e.shiftKey) { toast("These mods take plain keys only, without Shift, Ctrl or Alt", true); return true; }
    const name = this.ue4ssKey(e);
    if (!name) { toast("That key can't be used here", true); return true; }
    const c = this.capture; this.capture = null;
    this.set(c.folder, c.key, name);
    return true;
  }
};

ICONS.others = '<svg viewBox="0 0 24 24" fill="none" stroke="url(#gg)" stroke-width="1.7"><path d="M12 3.5 19.5 8v8L12 20.5 4.5 16V8Z"/><path d="M12 8.5v7M8.5 12h7"/></svg>';
window.OR = OR;
