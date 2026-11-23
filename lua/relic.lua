-- Made by Claude Ai & Relic Hunter
local R = { VERSION = "0.7.1" }

local function parent(p) return (p or ""):gsub("\\", "/"):match("^(.*)/[^/]*$") end

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

local function copy(v)
    if type(v) ~= "table" then return v end
    local t = {}
    for k, x in pairs(v) do t[k] = copy(x) end
    return setmetatable(t, getmetatable(v))
end

local function same(a, b)
    if type(a) ~= type(b) then return false end
    if type(a) ~= "table" then return a == b end
    for k, v in pairs(a) do if not same(v, b[k]) then return false end end
    for k in pairs(b) do if a[k] == nil then return false end end
    return true
end

local function shared(name)
    local ok, v = pcall(function() return ModRef:GetSharedVariable(name) end)
    if ok then return v end
    return nil
end

function R.client(id, mod_dir, defaults, opts)
    opts = opts or {}
    defaults = copy(defaults)
    mod_dir = (mod_dir or ""):gsub("\\", "/")
    local core = (parent(mod_dir) or ".") .. "/RelicCore"
    local ok, json = pcall(dofile, core .. "/Scripts/json.lua")
    if not ok or type(json) ~= "table" then error("Relic Core json.lua missing: " .. tostring(json)) end

    local C = {
        id = id, core = core, values = copy(defaults), defaults = copy(defaults),
        file = core .. "/settings/" .. id .. ".json",
        status = "defaults", disabled = false, imported = nil,
        rev = nil, at = -100, file_at = -100, listeners = {}, loads = 0, saves = 0, problem = nil,
    }

    local function coerce(key, v)
        local d = defaults[key]
        if d == nil then return nil end
        if type(d) == "number" then
            local n = tonumber(v)
            if n == nil then return nil end
            local lim = opts.limits and opts.limits[key]
            if lim then n = math.max(lim[1], math.min(lim[2], n)) end
            return n
        end
        if type(d) == "boolean" then
            if type(v) == "boolean" then return v end
            return nil
        end
        if type(d) == "string" then
            if type(v) == "string" then return v end
            if type(v) == "number" then return tostring(v) end
            return nil
        end
        if type(d) == "table" and type(v) == "table" then return v end
        return nil
    end

    function C.load()
        C.loads = C.loads + 1
        local fresh = copy(defaults)
        local text = read(C.file)
        if text then
            local okd, data = pcall(json.decode, text)
            if okd and type(data) == "table" and type(data.values) == "table" then
                for k, v in pairs(data.values) do
                    local c = coerce(k, v)
                    if c ~= nil then fresh[k] = c end
                end
                C.status = "Relic Mod Manager settings"
            else
                C.status = "settings file unreadable, defaults used"
                C.problem = tostring(data)
            end
        else
            C.status = "no saved settings yet, defaults used"
        end
        local ctext = read(core .. "/settings/_core.json")
        local was_disabled = C.disabled
        C.disabled = false
        if ctext then
            local okc, cd = pcall(json.decode, ctext)
            if okc and type(cd) == "table" and type(cd.disabled) == "table" and cd.disabled[id] == true then C.disabled = true end
        end
        local changed = {}
        if was_disabled ~= C.disabled then changed[#changed + 1] = "_disabled" end
        for k, v in pairs(fresh) do if not same(v, C.values[k]) then changed[#changed + 1] = k end end
        for k in pairs(C.values) do C.values[k] = nil end
        for k, v in pairs(fresh) do C.values[k] = v end
        return changed
    end

    function C.save()
        local data = { schema = 1, mod = id, version = opts.version, values = C.values }
        local okw = write(C.file, json.encode(data))
        if okw then C.saves = C.saves + 1 else C.problem = "couldn't write " .. C.file end
        return okw
    end

    function C.set(key, value)
        local c = coerce(key, value)
        if c == nil then return false end
        C.values[key] = c
        return C.save()
    end

    function C.import(path, parse)
        if read(C.file) then return false end
        local text = read(path)
        if not text then return false end
        local got = 0
        if parse then
            for k, v in pairs(parse(text) or {}) do
                local c = coerce(k, v)
                if c ~= nil then C.values[k] = c; got = got + 1 end
            end
        else
            for line in text:gmatch("[^\r\n]+") do
                local k, v = line:match("^%s*([%w_]+)%s*=%s*(.-)%s*$")
                if k and v and defaults[k] ~= nil then
                    v = v:gsub("%s+[#;].*$", "")
                    local d = defaults[k]
                    local c
                    if type(d) == "boolean" then
                        local l = v:lower()
                        if l == "true" or l == "false" then c = (l == "true") end
                    else
                        c = coerce(k, v)
                    end
                    if c ~= nil then C.values[k] = c; got = got + 1 end
                end
            end
        end
        if got == 0 then return false end
        C.imported = got
        C.status = "Relic Mod Manager settings"
        return C.save()
    end

    function C.on_change(fn) C.listeners[#C.listeners + 1] = fn end

    local function fire(changed)
        if #changed == 0 then return end
        for _, fn in ipairs(C.listeners) do pcall(fn, changed, C.values) end
    end

    function C.tick(now)
        now = now or os.clock()
        if now < C.at then return end
        C.at = now + 0.25
        local rev = shared("RelicCore.rev")
        if rev == nil then
            if now < C.file_at then return end
            C.file_at = now + 1
            rev = read(core .. "/bridge/rev.txt") or ""
        end
        if C.rev == nil then C.rev = rev; return end
        if rev == C.rev then return end
        C.rev = rev
        fire(C.load())
    end

    function C.line()
        local s = "settings: " .. C.status .. " (RelicCore/settings/" .. id .. ".json)"
        if C.imported then s = s .. ", imported " .. C.imported .. " values from the old config" end
        if C.disabled then s = s .. ", TURNED OFF in Relic Mod Manager" end
        if C.problem then s = s .. ", problem: " .. C.problem end
        local core_ver = shared("RelicCore.version")
        s = s .. "; Relic Core " .. (core_ver and tostring(core_ver) or "not running (live changes checked every second)")
        return s
    end

    C.load()
    return C
end

return R
