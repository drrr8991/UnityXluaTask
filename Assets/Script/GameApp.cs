using UnityEngine;
using System.Collections.Generic;
using XLua;
using System;
using UnityEngine.U2D;
using LuaAPI = XLua.LuaDLL.Lua;

public class GameApp : MonoBehaviour
{
    private static LuaEnv s_luaenv = null;
    public static LuaEnv LuaEnvironment
    {
        get { return s_luaenv; }
    }
     private void Awake()
    {
        //SpriteAtlasManager.atlasRequested += AddressableWrap.LoadSpriteAtlasAsync;
        Application.runInBackground = true;
#if UNITY_EDITOR
        Debug.Log("GameApp Awake.");
#endif
    }
    private void Start()
    {
        StartGame();
    }
    // Start is called before the first frame update
    private void StartGame()
    {
        StartEnv();
    }

    private LuaTask.TaskManager task;
    private LuaFunction updateFun;
    void StartEnv()
    {
        s_luaenv = new LuaEnv();
        s_luaenv.AddLoader(ScriptLoader.LoadScript);

        task = new LuaTask.TaskManager(s_luaenv);
        task.OpenLibs = (LuaEnv luaenv)=>{
            luaenv.AddLoader(ScriptLoader.LoadScript);
        };
        s_luaenv.DoString("require [[GameStartup]]");
        updateFun = s_luaenv.Global.Get<LuaFunction>("UpdateFunction");

    }

    // Update is called once per frame
    void Update()
    {
        if (updateFun!=null){
            updateFun.Call();
        }
    }

    void OnDestroy(){
        updateFun = null;
        task = null;
        s_luaenv.Dispose();
    }
}
