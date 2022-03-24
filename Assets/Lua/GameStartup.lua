print("lua start")

local luaTask = require("Task")
local addr = luaTask.new("Battle","Battle")
luaTask.async(function()
    while true do
        luaTask.print("LUA_TASK", luaTask.call("lua", addr, "ADD", 1,2,3,4,5,6,7))
        luaTask.print("LUA_TASK", luaTask.call("lua", addr, "ADD2", 11,22,13,14,15,16,17))
        luaTask.sleep(1000)
    end
end)

function UpdateFunction()
    luaTask.update()
end