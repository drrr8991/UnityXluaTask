using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;

public static class StringUtil
{
    private static StringBuilder _stringBuffer = new StringBuilder(4096);

    public static string Format(string _Format, params object[] _Args)
    {
        _stringBuffer.Length = 0;
        _stringBuffer.AppendFormat(_Format, _Args);
        return _stringBuffer.ToString();
    }
}
