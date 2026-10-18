-- Zinc's shell integration for cmd under Clink. Zinc puts this folder on CLINK_PATH for the cmd tabs it starts (and
-- sets ZINC_CMD_PROMPT), and gives cmd a PROMPT that carries the prompt marks itself, for cmd without Clink. Under
-- Clink the marks come from here instead, which adds what PROMPT can't give (the exit code, the command's text) and
-- keeps them when a prompt theme (oh-my-posh, starship, flexprompt) replaces the whole prompt:
--   before each prompt: D with the last command's exit code (when it has one: see keepsErrorlevel), A, and the folder
--   (OSC 9;9);
--   after the prompt, whatever drew it: B (CLINK_PROMPT_SUFFIX, which Clink adds after every filter);
--   on Enter: C with the command line (Clink writes it after the Enter's line break).
-- Tried and dropped: restoring the original PROMPT with os.setenv at load stopped Clink after the first command.

if not os.getenv("ZINC_CMD_PROMPT") then
    return -- not a cmd Zinc set up
end

local esc = "\027"
local st = esc .. "\\"

local suffix = os.getenv("CLINK_PROMPT_SUFFIX") or ""
if not suffix:find("133;B", 1, true) then -- (a script reload runs this again)
    os.setenv("CLINK_PROMPT_SUFFIX", suffix .. "$e]133;B$e\\")
end

-- First of all filters: PROMPT's own marks out (the hooks below send them), so a theme after it sees a plain prompt.
local strip = clink.promptfilter(-1000000)
function strip:filter(prompt)
    local plain = prompt:gsub(esc .. "%]133;[^" .. esc .. "]*" .. esc .. "\\", "")
    plain = plain:gsub(esc .. "%]9;9;[^" .. esc .. "]*" .. esc .. "\\", "")
    return plain
end

-- A line of only built-ins that leave the errorlevel as it was (echo, title, rem, cls), or no command at all: the
-- errorlevel is still the one before, so D gives no exit code (a plain "echo hi" after a failure isn't marked failed).
-- cd, set, ver and the rest reset it, and a line with & or | runs more than one command: those give it.
local function keepsErrorlevel(line)
    if line:find("[&|]") then
        return false
    end

    local word, rest = line:match("^[%s@]*(%a*)(.*)$")
    word = word:lower()
    local after = rest:sub(1, 1)
    if word == "" then
        return after == ""
    elseif word == "echo" then
        return after == "" or after:find("[%s%.:%(/,;=]") ~= nil -- (echo. and echo( print an empty line)
    end

    return (word == "title" or word == "rem" or word == "cls") and (after == "" or after:find("%s") ~= nil)
end

local sameErrorlevel = false

clink.onbeginedit(function()
    io.write(esc .. "]133;D" .. (sameErrorlevel and "" or ";" .. os.geterrorlevel()) .. st ..
        -- No zinc_c=enter, because the C below would come after Zinc's own and be dropped. redraw=0 because Clink
        -- doesn't draw the prompt again after a resize, so Zinc keeps it.
        esc .. "]133;A;redraw=0" .. st ..
        esc .. "]9;9;" .. os.getcwd() .. st)
    io.flush()
end)

clink.onendedit(function(line)
    sameErrorlevel = keepsErrorlevel(line)
    local url = line:gsub("[^%w%-%._~/]", function(c) return string.format("%%%02X", c:byte()) end)
    io.write(esc .. "]133;C;cmdline_url=" .. url .. st)
    io.flush()
end)
