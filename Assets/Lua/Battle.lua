local task = require("Task")

local command = {}

command.ADD = function(...)
    local t = {...}
    local res = 0
    for _, v in ipairs(t) do
        res = res + v
    end
    return res
end

command.ADD2 = function(...)
    assert(false)
    local t = {...}
    local res = 0
    for _, v in ipairs(t) do
        res = res + v
    end
    return res
end

task.dispatch('lua',function(sender, session, cmd, ...)
    --task.print(sender, session, cmd, ...)
    local fn = command[cmd]
    task.response("lua", sender, session, fn(...))
end)