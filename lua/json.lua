-- Made by Claude Ai & Relic Hunter
local J = {}

local escapes = { ['"'] = '\\"', ['\\'] = '\\\\', ['\b'] = '\\b', ['\f'] = '\\f', ['\n'] = '\\n', ['\r'] = '\\r', ['\t'] = '\\t' }

local function is_array(t)
    local n = 0
    for k in pairs(t) do
        if type(k) ~= "number" or k < 1 or k % 1 ~= 0 then return false end
        n = n + 1
    end
    for i = 1, n do if t[i] == nil then return false end end
    return true, n
end

local encode
encode = function(v, out)
    local t = type(v)
    if v == nil or v == J.null then
        out[#out + 1] = "null"
    elseif t == "boolean" then
        out[#out + 1] = v and "true" or "false"
    elseif t == "number" then
        if v ~= v or v == math.huge or v == -math.huge then
            out[#out + 1] = "null"
        elseif math.type and math.type(v) == "integer" then
            out[#out + 1] = string.format("%d", v)
        elseif v % 1 == 0 and math.abs(v) < 1e15 then
            out[#out + 1] = string.format("%d", math.floor(v))
        else
            out[#out + 1] = string.format("%.14g", v)
        end
    elseif t == "string" then
        out[#out + 1] = '"' .. v:gsub('[%c"\\]', function(c)
            return escapes[c] or string.format("\\u%04x", c:byte())
        end) .. '"'
    elseif t == "table" then
        local arr, n = is_array(v)
        if arr and (n > 0 or getmetatable(v) == J.array_mt) then
            out[#out + 1] = "["
            for i = 1, n do
                if i > 1 then out[#out + 1] = "," end
                encode(v[i], out)
            end
            out[#out + 1] = "]"
        else
            local keys = {}
            for k in pairs(v) do keys[#keys + 1] = tostring(k) end
            table.sort(keys)
            out[#out + 1] = "{"
            for i, k in ipairs(keys) do
                if i > 1 then out[#out + 1] = "," end
                encode(k, out)
                out[#out + 1] = ":"
                local val = v[k]
                if val == nil then val = v[tonumber(k)] end
                encode(val, out)
            end
            out[#out + 1] = "}"
        end
    else
        error("can't encode " .. t)
    end
end

function J.encode(v)
    local out = {}
    encode(v, out)
    return table.concat(out)
end

J.null = setmetatable({}, { __tostring = function() return "null" end })
J.array_mt = {}
function J.array(t) return setmetatable(t or {}, J.array_mt) end

local function utf8char(cp)
    if cp < 0x80 then return string.char(cp) end
    if cp < 0x800 then return string.char(0xC0 | (cp >> 6), 0x80 | (cp & 0x3F)) end
    if cp < 0x10000 then return string.char(0xE0 | (cp >> 12), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)) end
    return string.char(0xF0 | (cp >> 18), 0x80 | ((cp >> 12) & 0x3F), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F))
end

function J.decode(s)
    if type(s) ~= "string" then error("json: expected a string") end
    local i = 1
    if s:sub(1, 3) == "\239\187\191" then i = 4 end
    local function fail(what) error(string.format("json: %s at character %d", what, i), 0) end
    local function ws()
        while true do
            local c = s:sub(i, i)
            if c == " " or c == "\t" or c == "\r" or c == "\n" then i = i + 1 else return end
        end
    end
    local value
    local function str()
        i = i + 1
        local out = {}
        while true do
            local c = s:sub(i, i)
            if c == "" then fail("unfinished string") end
            if c == '"' then i = i + 1; return table.concat(out) end
            if c == "\\" then
                local e = s:sub(i + 1, i + 1)
                local map = { ['"'] = '"', ["\\"] = "\\", ["/"] = "/", b = "\b", f = "\f", n = "\n", r = "\r", t = "\t" }
                if map[e] then
                    out[#out + 1] = map[e]; i = i + 2
                elseif e == "u" then
                    local hex = s:sub(i + 2, i + 5)
                    local cp = tonumber(hex, 16) or fail("bad \\u escape")
                    i = i + 6
                    if cp >= 0xD800 and cp <= 0xDBFF and s:sub(i, i + 1) == "\\u" then
                        local lo = tonumber(s:sub(i + 2, i + 5), 16)
                        if lo and lo >= 0xDC00 and lo <= 0xDFFF then
                            cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00)
                            i = i + 6
                        end
                    end
                    out[#out + 1] = utf8char(cp)
                else
                    fail("bad escape")
                end
            else
                local j = s:find('["\\]', i)
                if not j then fail("unfinished string") end
                out[#out + 1] = s:sub(i, j - 1)
                i = j
            end
        end
    end
    value = function()
        ws()
        local c = s:sub(i, i)
        if c == "{" then
            i = i + 1
            local t = {}
            ws()
            if s:sub(i, i) == "}" then i = i + 1; return t end
            while true do
                ws()
                if s:sub(i, i) ~= '"' then fail("expected a key") end
                local k = str()
                ws()
                if s:sub(i, i) ~= ":" then fail("expected ':'") end
                i = i + 1
                local v = value()
                if v ~= J.null then t[k] = v end
                ws()
                local d = s:sub(i, i)
                i = i + 1
                if d == "}" then return t end
                if d ~= "," then fail("expected ',' or '}'") end
            end
        elseif c == "[" then
            i = i + 1
            local t = J.array({})
            ws()
            if s:sub(i, i) == "]" then i = i + 1; return t end
            local n = 0
            while true do
                local v = value()
                n = n + 1
                if v ~= J.null then t[n] = v end
                ws()
                local d = s:sub(i, i)
                i = i + 1
                if d == "]" then return t end
                if d ~= "," then fail("expected ',' or ']'") end
            end
        elseif c == '"' then
            return str()
        elseif s:sub(i, i + 3) == "true" then
            i = i + 4; return true
        elseif s:sub(i, i + 4) == "false" then
            i = i + 5; return false
        elseif s:sub(i, i + 3) == "null" then
            i = i + 4; return J.null
        else
            local num = s:match("^-?%d+%.?%d*[eE]?[-+]?%d*", i)
            if not num or num == "" or num == "-" then fail("unexpected '" .. c .. "'") end
            i = i + #num
            return tonumber(num) or fail("bad number")
        end
    end
    local v = value()
    ws()
    if i <= #s then fail("extra text after the value") end
    if v == J.null then return nil end
    return v
end

return J
