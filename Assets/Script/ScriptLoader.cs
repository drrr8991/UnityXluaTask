using System;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
// using ICSharpCode.SharpZipLib.Zip;

public static class ScriptLoader
{
    public static byte[] LoadScript(ref string fileName)
    {
        //Debug.Log("Lua fileName => " + fileName);
        string path = GetFilePath(ref fileName);
        //Debug.Log("Lua Path => " + path);


#if EXISTING
        var enumer = mLuaScripts.GetEnumerator();
        while (enumer.MoveNext())
        {
            if (enumer.Current.Key == path)
                return enumer.Current.Value;
        }
        return null;
#elif UNITY_EDITOR || UNITY_STANDALONE
        if (FileExists(ref path))
        {
            return LoadFileBytes(ref path);
        }
        return null;
#else
        var enumer = mLuaScripts.GetEnumerator();
        while (enumer.MoveNext())
        {
            if (enumer.Current.Key == path)
                return enumer.Current.Value;
        }
        return null;
#endif
    }

    private static string GetFilePath(ref string fileName)
    {
#if EXISTING
        return StringUtil.Format("Lua/{0}.lua", fileName.Replace('.', '/'));
#elif UNITY_EDITOR || UNITY_STANDALONE
        return StringUtil.Format(@"{0}/Lua/{1}.lua",
            #if UNITY_EDITOR
                Application.dataPath
            #else
                Application.streamingAssetsPath
            #endif
                , fileName.Replace('.', '/'));
#else
        return StringUtil.Format("Lua/{0}.lua", fileName.Replace('.', '/'));
#endif
    }

    private static bool FileExists(ref string filePath)
    {
        return File.Exists(filePath);
    }

    private static byte[] LoadFileBytes(ref string filePath)
    {

#if UNITY_EDITOR || UNITY_STANDALONE
        return File.ReadAllBytes(filePath);
#else
        return null;
#endif

    }

    static Dictionary<string, byte[]> mLuaScripts = new Dictionary<string, byte[]>();
    public static void LoadZipPath(Action callBack = null)
    {
        // AddressableWrap.LoadAssetText("Luabytes", (_bytes) =>
        // {
        //     var compressed = new MemoryStream(_bytes);
        //     using (ZipInputStream decompressor = new ZipInputStream(compressed))
        //     {
        //         ZipEntry entry;

        //         while ((entry = decompressor.GetNextEntry()) != null)
        //         {
        //             string name = entry.Name;
        //             if (!name.Contains(".meta") && name.Contains(".lua"))
        //             {
        //                 byte[] data = new byte[entry.Size];
        //                 int bytesRead = 0;
        //                 bytesRead = decompressor.Read(data, 0, data.Length);

        //                 mLuaScripts.Add(name, data);
        //             }
        //         }
        //     }
        //     callBack?.Invoke();
        // });


        /*
        var async = Addressables.LoadAssetAsync<TextAsset>("Luabytes");
        async.Completed += (op) =>
          {
              Debug.Log(op.Status.ToString());

              if (op.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
              {
                  Debug.LogErrorFormat("Zip/Bytes [Luabytes] load failed.");
                  return;
              }
              var zipTextAsset = op.Result;

              var compressed = new MemoryStream(zipTextAsset.bytes);
              using (ZipInputStream decompressor = new ZipInputStream(compressed))
              {
                  ZipEntry entry;

                  while ((entry = decompressor.GetNextEntry()) != null)
                  {
                      string name = entry.Name;
                      if (!name.Contains(".meta") && name.Contains(".lua"))
                      {
                          byte[] data = new byte[entry.Size];
                          int bytesRead = 0;
                          bytesRead = decompressor.Read(data, 0, data.Length);

                          //Debug.Log(name);
                          //if (name.Contains("GameStartup"))
                          //    Debug.Log(name);
                          mLuaScripts.Add(name, data);
                      }
                  }
              }
              callBack?.Invoke();
          };
          */
    }
}
