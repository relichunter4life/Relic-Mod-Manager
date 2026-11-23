-- Made by Claude Ai & Relic Hunter
local VERSION = "0.7.1"
local NAME = "Relic Mod Manager"
local TAG = "[RelicCore] "
local function log(m) print(TAG .. tostring(m) .. "\n") end

local script_dir = ((debug.getinfo(1, "S").source or ""):gsub("^@", "")):gsub("\\", "/"):match("^(.*)/[^/]*$") or "."
local core_dir = script_dir:match("^(.*)/[^/]*$") or script_dir

local function now() return os.clock() end
local function get(fn) local ok, v = pcall(fn); if ok then return v end return nil end
local function valid(o)
    if type(o) ~= "userdata" and type(o) ~= "table" then return false end
    local ok, v = pcall(function() return o:IsValid() end)
    return ok and v == true
end
local function full(o) return get(function() return o:GetFullName() end) end

local function read(path)
    local f = io.open(path, "rb")
    if not f then return nil end
    local t = f:read("a")
    f:close()
    return t
end
local function write(path, text)
    local f = io.open(path, "wb")
    if not f then return false end
    f:write(text)
    f:close()
    return true
end
local function exists(path) local f = io.open(path, "rb"); if f then f:close(); return true end return false end

local S = {
    phase = "boot", seq = 0, rev = nil, link = "files", exe = "not started yet", launched = false,
    pcs = {}, pc = nil, problems = {}, started = os.date("%Y-%m-%d %H:%M:%S"),
    rev_at = 0, phase_at = 0, report_at = nil, phases = 0, slowest = 0,
}
local function problem(key, text)
    if not S.problems[key] then S.problems[key] = text; log(text) end
end

local okj, json = pcall(dofile, script_dir .. "/json.lua")
if not okj or type(json) ~= "table" then
    problem("json", "json.lua missing: " .. tostring(json))
    json = { encode = function() return "{}" end }
end

local function share(k, v) pcall(function() ModRef:SetSharedVariable(k, v) end) end
share("RelicCore.version", VERSION)
share("RelicCore.dir", core_dir)

local function core_setting(key, default)
    local t = read(core_dir .. "/settings/RelicCore.json")
    if not t then return default end
    local ok, d = pcall(json.decode, t)
    if ok and type(d) == "table" and type(d.values) == "table" and d.values[key] ~= nil then return d.values[key] end
    return default
end

local function write_game()
    S.seq = S.seq + 1
    local text = json.encode({ seq = S.seq, phase = S.phase, dll = S.link, core = VERSION, time = os.time() })
    if not write(core_dir .. "/bridge/game.json", text) then problem("bridge", "couldn't write RelicCore/bridge/game.json") end
end

local EXE = core_dir .. "/App/RelicModManager.exe"
local function launch()
    if S.launched then return end
    S.launched = true
    if core_setting("LaunchWindow", true) == false then S.exe = "turned off in settings"; return end
    if not exists(EXE) then S.exe = "missing: App/RelicModManager.exe"; problem("exe", "App/RelicModManager.exe is missing"); return end
    local win = EXE:gsub("/", "\\")
    local ok, err = pcall(os.execute, 'start "" "' .. win .. '" --from-game')
    S.exe = ok and "started" or ("couldn't start: " .. tostring(err))
    log("window " .. S.exe)
end

local function local_pc()
    S.pc = nil
    for i = #S.pcs, 1, -1 do
        local pc = S.pcs[i]
        if not valid(pc) then
            table.remove(S.pcs, i)
        elseif get(function() return pc:IsLocalController() end) then
            local n = full(pc) or ""
            if not n:find("Default__", 1, true) then S.pc = pc; return pc end
        end
    end
    return nil
end

local function detect_phase()
    local pc = local_pc()
    if not pc then return nil end
    local n = full(pc) or ""
    if n:find("MainMenu", 1, true) then return "menu" end
    local pawn = get(function() return pc.Pawn end)
    if valid(pawn) then return "world" end
    return nil
end

local write_report

local MM = { seen = nil, rev = {}, actn = 0, queue = {}, next_at = 0, sent = 0 }
local function mm_loaded()
    return type(get(function() return ModRef:GetSharedVariable("ModMenu.version") end)) == "string"
end
local function mm_sync()
    local t = read(core_dir .. "/bridge/modmenu.json")
    if not t then MM.seen = MM.seen or {}; return end
    local ok, d = pcall(json.decode, t)
    if not ok or type(d) ~= "table" then return end
    local mods = type(d.mods) == "table" and d.mods or {}
    local first = MM.seen == nil
    MM.seen = MM.seen or {}
    local via_mm = mm_loaded()
    for id, e in pairs(mods) do
        local r = type(e) == "table" and tonumber(e.rev) or 0
        if first then
            MM.seen[id] = r
        elseif r ~= MM.seen[id] then
            MM.seen[id] = r
            local skip = {}
            for _, k in ipairs(type(e.skip) == "table" and e.skip or {}) do skip[k] = true end
            local set = type(e.set) == "table" and e.set or {}
            if via_mm then
                local parts = {}
                for k, v in pairs(set) do
                    local sv = tostring(v)
                    if not skip[k] and not sv:find("|", 1, true) then parts[#parts + 1] = k .. "=" .. sv end
                end
                if #parts > 0 then MM.queue[#MM.queue + 1] = { id = id, parts = parts } end
            else
                for k, v in pairs(set) do if not skip[k] then share("ModMenu." .. id .. "." .. k, v) end end
                MM.rev[id] = (MM.rev[id] or 1000) + 1
                share("ModMenu." .. id .. ".rev", MM.rev[id])
                MM.sent = MM.sent + 1
            end
        end
    end
    local maxn = MM.actn
    for _, a in ipairs(type(d.actions) == "table" and d.actions or {}) do
        local n = type(a) == "table" and tonumber(a.n) or 0
        if not first and n > MM.actn then
            share("ModMenu." .. tostring(a.id) .. ".action", tostring(a.a) .. "#" .. n)
            MM.sent = MM.sent + 1
        end
        if n > maxn then maxn = n end
    end
    MM.actn = maxn
end
local function mm_pump(t0)
    if #MM.queue == 0 or t0 < MM.next_at then return end
    local q = table.remove(MM.queue, 1)
    MM.next_at = t0 + 0.3
    share("ModMenu.request.set", q.id .. "#" .. string.format("%.3f", t0) .. "|" .. table.concat(q.parts, "|"))
    MM.sent = MM.sent + 1
end

local function tick()
    local t0 = now()
    if t0 >= S.rev_at then
        S.rev_at = t0 + 0.25
        local r = read(core_dir .. "/bridge/rev.txt")
        r = r and r:match("%d+") or "0"
        if r ~= S.rev then
            S.rev = r
            share("RelicCore.rev", r)
            local okm, errm = pcall(mm_sync)
            if not okm then problem("mm", "Other Relics link: " .. tostring(errm)) end
        end
    end
    mm_pump(t0)
    if t0 >= S.phase_at then
        S.phase_at = t0 + 0.5
        local p = detect_phase()
        if p and p ~= S.phase then
            S.phase = p
            S.phases = S.phases + 1
            share("RelicCore.phase", p)
            write_game()
            if p == "menu" then launch() end
            S.report_at = t0 + 2
        end
    end
    if S.report_at and t0 >= S.report_at then
        S.report_at = nil
        pcall(write_report, "phase " .. S.phase)
    end
    local ms = (now() - t0) * 1000
    if ms > S.slowest then S.slowest = ms end
end

write_report = function(why)
    local f = io.open(core_dir .. "/report.txt", "w")
    if not f then return end
    local function line(s) f:write(tostring(s) .. "\n") end
    line(NAME .. " " .. VERSION .. " report (" .. why .. ") " .. os.date("%Y-%m-%d %H:%M:%S"))
    line("folder: " .. core_dir)
    line("link to the Relic Mod Manager window: " .. (S.link == "files" and "files (prototype; the DLL link comes next)" or S.link))
    line("window: " .. S.exe .. "  (Shift+F7 opens and hides it)")
    line("game: " .. S.phase .. " (" .. S.phases .. " changes), settings revision " .. tostring(S.rev))
    line(string.format("speed: slowest tick %.2f ms", S.slowest))
    line("Other Relics: " .. MM.sent .. " live changes sent" .. (mm_loaded() and " (through Mod Menu)" or ""))
    local probs = {}
    for _, v in pairs(S.problems) do probs[#probs + 1] = v end
    line("problems: " .. (#probs > 0 and table.concat(probs, "; ") or "none"))
    f:close()
end

local notify = rawget(_G, "NotifyOnNewObject")
if type(notify) == "function" then
    local ok, err = pcall(notify, "/Script/Engine.PlayerController", function(pc)
        S.pcs[#S.pcs + 1] = pc
        if #S.pcs > 8 then table.remove(S.pcs, 1) end
    end)
    if not ok then problem("notify", "couldn't watch for player controllers: " .. tostring(err)) end
else
    problem("notify", "NotifyOnNewObject missing; the window opens only with Shift+F7")
end

write_game()

local function guarded()
    local ok, err = pcall(tick)
    if not ok then problem("tick" .. tostring(err), "error: " .. tostring(err)) end
end
if not pcall(LoopInGameThreadWithDelay, 100, function() guarded() end) then
    local queued = false
    LoopAsync(100, function()
        if queued then return false end
        queued = true
        if not pcall(ExecuteInGameThread, function() queued = false; guarded() end) then queued = false end
        return false
    end)
end
pcall(RegisterConsoleCommandHandler, "reliccore", function()
    ExecuteInGameThread(function() write_report("console command") end)
    return true
end)
pcall(write_report, "start")
log("loaded v" .. VERSION)
