using System;
using XLua;

using LuaAPI = XLua.LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
#if !XLUA_GENERAL
using UnityEngine;
#endif

namespace LuaTask
{
    public enum PTYPE
    {
        Lua = 1,
        Error = 2,
        Timer = 3,
    }

    public class Message
    {
        public PTYPE Type { get; set; }
        public long Sender { get; set; }
        public long Receiver { get; set; }
        public long Session { get; set; }
        public byte[] Data { get; set; }
    }

    class ObjectHolder : IDisposable
    {
        GCHandle handle;

        public ObjectHolder(LuaEnv luaEnv, object obj, byte[] key)
        {
            handle = GCHandle.Alloc(obj);
            var L = luaEnv.L;
            LuaAPI.xlua_pushlstring(L, key, key.Length);
            LuaAPI.lua_pushlightuserdata(L, GCHandle.ToIntPtr(handle));
            LuaAPI.lua_rawset(L, LuaIndexes.LUA_REGISTRYINDEX);
        }

        public static T Get<T>(RealStatePtr L, byte[] key) where T : class
        {
            LuaAPI.xlua_pushlstring(L, key, key.Length);
            if (LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX) == 0)
            {
                LuaAPI.luaL_error(L, typeof(T).Name + " object not found");
                return default(T);
            }
            RealStatePtr p = LuaAPI.lua_topointer(L, -1);
            if (p == RealStatePtr.Zero)
            {
                LuaAPI.luaL_error(L, typeof(T).Name + " object init with invalid key");
                return default(T);
            }
            LuaAPI.lua_pop(L, 1);
            var handle = GCHandle.FromIntPtr(p);
            return handle.Target as T;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                handle.Free();
            }
        }
    }

    class Timer
    {
        long timerUUID = 0;

        struct TimerContext
        {
            public long timeStamp;
            public long timerId;
        }

        class TimerComparer : IComparer<TimerContext>
        {
            public int Compare(TimerContext x, TimerContext y)
            {
                if (x.timeStamp == y.timeStamp)
                {
                    return x.timerId.CompareTo(y.timerId);
                }
                return x.timeStamp.CompareTo(y.timeStamp);
            }
        }

        SortedDictionary<TimerContext, long> timers = new SortedDictionary<TimerContext, long>(new TimerComparer());

        public long Add(long mills)
        {
            mills += DateTimeOffset.Now.ToUnixTimeMilliseconds();

            long timerId = ++timerUUID;

            var ctx = new TimerContext
            {
                timeStamp = mills,
                timerId = timerId
            };

            timers.Add(ctx, timerId);
            return timerId;
        }

        public long Pop()
        {
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (timers.Count == 0)
            {
                return 0;
            }
            var iter = timers.GetEnumerator();
            iter.MoveNext();
            var cur = iter.Current;
            if (cur.Key.timeStamp <= now)
            {
                var timerId = cur.Value;
                timers.Remove(cur.Key);
                return timerId;
            }
            return 0;
        }

        public void Update(Action<long> fn)
        {
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            while (true)
            {
                if (timers.Count == 0)
                {
                    return;
                }

                var iter = timers.GetEnumerator();
                iter.MoveNext();
                var cur = iter.Current;
                if (cur.Key.timeStamp <= now)
                {
                    fn(cur.Value);
                    timers.Remove(cur.Key);
                }
                else
                {
                    break;
                }
            }
        }

    }

    class LuaService
    {
        static readonly byte[] KEY = new byte[]
        {
            (byte)'T',(byte)'A',(byte)'S',(byte)'K',(byte)'_',(byte)'G',(byte)'L',(byte)'O',(byte)'B',(byte)'A',(byte)'L',
        };

        public static byte[] ToBytes(RealStatePtr L, int index)
        {
            RealStatePtr len;
            RealStatePtr p = LuaAPI.lua_tolstring(L, index, out len);
            if (p == RealStatePtr.Zero)
                return null;
            byte[] bytes = new byte[(int)len];
            Marshal.Copy(p, bytes, 0, (int)len);
            return bytes;
        }

        readonly BlockingCollection<Message> queue = new BlockingCollection<Message>();
        readonly CancellationTokenSource cancelToken = new CancellationTokenSource();
        readonly Timer timer = new Timer();

        LuaEnv luaEnv;
        ObjectHolder holder;

        System.Threading.Tasks.Task task;
        TaskManager taskManager;

        int callbackRef = 0;

        public long ID { get; }
        public string Name { get; }

        public LuaService(long id, string name, TaskManager manager, LuaEnv env)
        {
            ID = id;
            Name = name;
            taskManager = manager;
            if (env == null)
            {
                luaEnv = new LuaEnv();
            }
            else
            {
                luaEnv = env;
            }

            holder = new ObjectHolder(luaEnv, this, KEY);

            luaEnv.AddBuildin("task.core", OpenLib);

            if (taskManager.OpenLibs != null)
            {
                taskManager.OpenLibs(luaEnv);
            }
        }

        void HandleMessage(RealStatePtr L, Message m)
        {
            int errFunc = 1;
            int top = LuaAPI.lua_gettop(L);
            if (top == 0)
            {
                LuaAPI.xlua_getglobal(L, "debug");
                LuaAPI.lua_pushstring(L, "traceback");
                LuaAPI.lua_rawget(L, 1);
                LuaAPI.lua_remove(L, 1);
                LuaAPI.xlua_rawgeti(L, LuaIndexes.LUA_REGISTRYINDEX, callbackRef);
            }
            else
            {
                if (top != 2)
                {
                    throw new LuaException("failed");
                }
            }

            LuaAPI.lua_pushvalue(L, 2);

            LuaAPI.lua_pushint64(L, m.Sender);
            LuaAPI.lua_pushstring(L, m.Data);
            LuaAPI.lua_pushint64(L, m.Session);
            LuaAPI.xlua_pushinteger(L, (int)m.Type);

            var r = (LuaThreadStatus)LuaAPI.lua_pcall(L, 4, 0, errFunc);
            if (r == LuaThreadStatus.LUA_OK)
                return;

            string error = "unknown error";
            switch (r)
            {
                case LuaThreadStatus.LUA_ERRRUN:
                    error = string.Format("{0} error :\n{1}", Name, LuaAPI.lua_tostring(L, -1));
                    break;
                case LuaThreadStatus.LUA_ERRMEM:
                    error = string.Format("{0} memory error", Name);
                    break;
                case LuaThreadStatus.LUA_ERRERR:
                    error = string.Format("{0} error in error", Name);
                    break;
            };
            LuaAPI.lua_pop(L, 1);
            if (m.Session == 0)
            {
                taskManager.Log(error);
            }
            else
            {
                taskManager.SendMessage(0, m.Sender, System.Text.Encoding.Default.GetBytes(error), m.Session, PTYPE.Error);
            }
        }

        public void Run(string name)
        {
            luaEnv.DoString(string.Format("require '{0}'", name));

            LuaAPI.lua_settop(luaEnv.L, 0);

            task = System.Threading.Tasks.Task.Run(() =>
            {
                Message mTimer = new Message();
                mTimer.Type = PTYPE.Timer;

                while (!cancelToken.Token.IsCancellationRequested)
                {
                    try
                    {
                        Message m;
                        var L = luaEnv.L;
                        while (queue.TryTake(out m, 10))
                        {
                            HandleMessage(L, m);
                        }

                        timer.Update((timerId) =>
                        {
                            mTimer.Sender = timerId;
                            HandleMessage(L, mTimer);
                        });
                    }
                    catch (Exception ex)
                    {
                        taskManager.Log(ex.Message);
                    }
                }
            });
        }

        public void PushMessage(Message m)
        {
            queue.Add(m);
        }

        public bool SendMessage(long to, byte[] data, long session, PTYPE type)
        {
            return taskManager.SendMessage(ID, to, data, session, type);
        }

        public void Quit()
        {
            cancelToken.Cancel();
            task.Wait();
        }

        #region LuaAPI

        static public int SendMessage(RealStatePtr L)
        {
            if (LuaAPI.lua_type(L, 1) != LuaTypes.LUA_TNUMBER)
                return LuaAPI.luaL_error(L, "param 1 need integer");
            if (LuaAPI.lua_type(L, 2) != LuaTypes.LUA_TSTRING)
                return LuaAPI.luaL_error(L, "param 2 need string");
            if (LuaAPI.lua_type(L, 3) != LuaTypes.LUA_TNUMBER)
                return LuaAPI.luaL_error(L, "param 3 need integer");
            if (LuaAPI.lua_type(L, 4) != LuaTypes.LUA_TNUMBER)
                return LuaAPI.luaL_error(L, "param 3 need integer");
            var S = ObjectHolder.Get<LuaService>(L, KEY);
            bool ok = S.SendMessage(LuaAPI.lua_toint64(L, 1), ToBytes(L, 2), LuaAPI.lua_toint64(L, 3), (PTYPE)LuaAPI.xlua_tointeger(L, 4));
            LuaAPI.lua_pushboolean(L, ok);
            return 1;
        }

        static int PopMessage(RealStatePtr L)
        {
            Message m;
            var S = ObjectHolder.Get<LuaService>(L, KEY);
            if (S.queue.TryTake(out m))
            {
                LuaAPI.lua_pushint64(L, m.Sender);
                LuaAPI.lua_pushstring(L, m.Data);
                LuaAPI.lua_pushint64(L, m.Session);
                LuaAPI.xlua_pushinteger(L, (int)m.Type);
                return 4;
            }

            long timerId = S.timer.Pop();
            if (timerId > 0)
            {
                LuaAPI.lua_pushint64(L, timerId);
                LuaAPI.lua_pushnil(L);
                LuaAPI.lua_pushint64(L, 0);
                LuaAPI.xlua_pushinteger(L, (int)PTYPE.Timer);
                return 4;
            }
            return 0;
        }

        static int FindService(RealStatePtr L)
        {
            var name = LuaAPI.lua_tostring(L, 1);
            var S = ObjectHolder.Get<LuaService>(L, KEY);
            long ID = S.taskManager.FindService(name);
            if (ID > 0)
            {
                LuaAPI.lua_pushint64(L, ID);
                return 1;
            }
            return 0;
        }

        static int AddTimer(RealStatePtr L)
        {
            var mills = LuaAPI.lua_toint64(L, 1);
            var S = ObjectHolder.Get<LuaService>(L, KEY);
            var timerId = S.timer.Add(mills);
            LuaAPI.lua_pushint64(L, timerId);
            return 1;
        }

        static public int Log(RealStatePtr L)
        {
            var logline = LuaAPI.lua_tostring(L, 1);
            var S = ObjectHolder.Get<LuaService>(L, KEY);
            S.taskManager.Log(logline);
            return 1;
        }

        static public int SetCallBack(RealStatePtr L)
        {
            if (!LuaAPI.lua_isfunction(L, 1))
            {
                return LuaAPI.luaL_error(L, "need lua function type");
            }

            var S = ObjectHolder.Get<LuaService>(L, KEY);
            S.callbackRef = LuaAPI.luaL_ref(L);
            return 0;
        }

        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        public static int OpenLib(RealStatePtr L)
        {
            var S = ObjectHolder.Get<LuaService>(L, KEY);

            LuaAPI.lua_newtable(L);
            LuaAPI.xlua_pushasciistring(L, "name");
            LuaAPI.lua_pushstring(L, S.Name);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "id");
            LuaAPI.lua_pushint64(L, S.ID);
            LuaAPI.lua_rawset(L, -3);

            LuaAPI.xlua_pushasciistring(L, "log");
            LuaAPI.lua_pushstdcallcfunction(L, Log);
            LuaAPI.lua_rawset(L, -3);

            LuaAPI.xlua_pushasciistring(L, "send");
            LuaAPI.lua_pushstdcallcfunction(L, SendMessage);
            LuaAPI.lua_rawset(L, -3);

            LuaAPI.xlua_pushasciistring(L, "pop");
            LuaAPI.lua_pushstdcallcfunction(L, PopMessage);
            LuaAPI.lua_rawset(L, -3);

            LuaAPI.xlua_pushasciistring(L, "find");
            LuaAPI.lua_pushstdcallcfunction(L, FindService);
            LuaAPI.lua_rawset(L, -3);

            LuaAPI.xlua_pushasciistring(L, "callback");
            LuaAPI.lua_pushstdcallcfunction(L, SetCallBack);
            LuaAPI.lua_rawset(L, -3);

            LuaAPI.xlua_pushasciistring(L, "timeout");
            LuaAPI.lua_pushstdcallcfunction(L, AddTimer);
            LuaAPI.lua_rawset(L, -3);

            return 1;
        }

        #endregion
    }

    public class TaskManager
    {
        static readonly byte[] KEY = new byte[]
        {
            (byte)'M',(byte)'T',(byte)'A',(byte)'S',(byte)'K',(byte)'_',(byte)'G',(byte)'L',(byte)'O',(byte)'B',(byte)'A',(byte)'L',
        };

        public Action<LuaEnv> OpenLibs;

        public Action<string> Log;
        readonly ConcurrentDictionary<long, LuaService> luaServices = new ConcurrentDictionary<long, LuaService>();
        private readonly ObjectHolder holder;

        const long MainID = 1;

        long serviceUUID = 1;

        public TaskManager(LuaEnv luaEnv)
        {
            Log = (line) =>
            {
#if !XLUA_GENERAL
                Debug.Log(line);
#else
                Console.WriteLine(line);
#endif
            };

            holder = new ObjectHolder(luaEnv, this, KEY);

            luaEnv.AddBuildin("task.manager.core", OpenLib);
            luaServices.TryAdd(MainID, new LuaService(MainID, "MainTask", this, luaEnv));
        }

        public bool SendMessage(long from, long to, byte[] data, long session, PTYPE type)
        {
            Message m = new Message
            {
                Sender = from,
                Receiver = to,
                Data = data,
                Session = -session,
                Type = type
            };

            LuaService S;
            if (luaServices.TryGetValue(m.Receiver, out S))
            {
                S.PushMessage(m);
                return true;
            }
            return false;
        }

        public long FindService(string name)
        {
            foreach (var v in luaServices.Values)
            {
                if (v.Name == name)
                {
                    return v.ID;
                }
            }
            return 0;
        }

        public void CloseAll()
        {
            foreach (var v in luaServices.Values)
            {
                if (v.ID != MainID)
                {
                    v.Quit();
                    luaServices.TryRemove(v.ID, out _);
                }
            }
        }

        static int NewService(RealStatePtr L)
        {
            try
            {
                var taskManager = ObjectHolder.Get<TaskManager>(L, KEY);
                var name = LuaAPI.lua_tostring(L, 1);
                var source = LuaAPI.lua_tostring(L, 2);
                var id = Interlocked.Increment(ref taskManager.serviceUUID);
                var service = new LuaService(id, name, taskManager, null);
                service.Run(source);
                taskManager.luaServices.TryAdd(id, service);
                LuaAPI.lua_pushint64(L, id);
                return 1;
            }
            catch (Exception ex)
            {
                return LuaAPI.luaL_error(L, ex.Message);
            }
        }

        static int RemoveService(RealStatePtr L)
        {
            var taskManager = ObjectHolder.Get<TaskManager>(L, KEY);
            var id = LuaAPI.lua_toint64(L, 1);
            LuaService S;
            if (taskManager.luaServices.TryRemove(id, out S))
            {
                S.Quit();
            }
            return 0;
        }

        static int CloseAll(RealStatePtr L)
        {
            var taskManager = ObjectHolder.Get<TaskManager>(L, KEY);
            taskManager.CloseAll();
            return 0;
        }

        static int ThreadSleep(RealStatePtr L)
        {
            var mills = LuaAPI.xlua_tointeger(L, 1);
            Thread.Sleep(mills);
            return 0;
        }

        [MonoPInvokeCallbackAttribute(typeof(LuaCSFunction))]
        static int OpenLib(RealStatePtr L)
        {
            LuaAPI.lua_newtable(L);
            LuaAPI.xlua_pushasciistring(L, "new");
            LuaAPI.lua_pushstdcallcfunction(L, NewService);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "remove");
            LuaAPI.lua_pushstdcallcfunction(L, RemoveService);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "close_all");
            LuaAPI.lua_pushstdcallcfunction(L, CloseAll);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "thread_sleep");
            LuaAPI.lua_pushstdcallcfunction(L, ThreadSleep);
            LuaAPI.lua_rawset(L, -3);
            return 1;
        }
    }
}

