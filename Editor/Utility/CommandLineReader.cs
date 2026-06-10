#region Using
using System;
using System.Collections.Generic;
using System.Linq;
using Debug = UnityEngine.Debug;
#endregion

/// <summary>
/// Unity 命令行拓展帮助类。
/// <para>提供了访问通过命令行传递的 [自定义参数] 的功能。
/// 只需在关键字 -CustomArgs: 后添加自定义参数，并使用分号 ; 分隔各个参数即可。
/// </para>
/// </summary>
/// <remarks>可以用来制定自己项目的打包、编辑器工作流。</remarks>
/// <example>
/// <code><![CDATA[
/// C:\Program Files (x86)\Unity\Editor\Unity.exe [ProjectLocation] -executeMethod [Your entrypoint] -quit -CustomArgs:Language=en_US;Version=1.02
/// ]]></code>
/// </example>
public static class CommandLineReader
{
    // Config
    private const string CUSTOM_ARGS_PREFIX = "-CustomArgs:";
    private const char CUSTOM_ARGS_SEPARATOR = ';';

    public static string[] GetCommandLineArgs()
    {
        return Environment.GetCommandLineArgs();
    }

    public static string GetCommandLine()
    {
        string[] args = GetCommandLineArgs();

        if (args.Length > 0)
        {
            return string.Join(" ", args);
        }
        else
        {
            Debug.LogError("CommandLineReader.cs - GetCommandLine() - Can't find any command line arguments!");
            return "";
        }
    }

    public static Dictionary<string,string> GetCustomArguments()
    {
        Dictionary<string, string> customArgsDict = new Dictionary<string, string>();
        string[] commandLineArgs = GetCommandLineArgs();
        string[] customArgs;
        string[] customArgBuffer;
        string customArgsStr = "";
        
        try
        {
            customArgsStr = commandLineArgs.Where(row => row.Contains(CUSTOM_ARGS_PREFIX)).Single();
        }
        catch (Exception e)
        {
            Debug.LogError("CommandLineReader.cs - GetCustomArguments() - Can't retrieve any custom arguments in the command line [" + commandLineArgs + "]. Exception: " + e);
            return customArgsDict;
        }

        customArgsStr = customArgsStr.Replace(CUSTOM_ARGS_PREFIX, "");
        customArgs = customArgsStr.Split(CUSTOM_ARGS_SEPARATOR);

        foreach (string customArg in customArgs)
        {
            customArgBuffer = customArg.Split('=');
            if (customArgBuffer.Length == 2)
            {
                customArgsDict.Add(customArgBuffer[0], customArgBuffer[1]);
            }
            else
            {
                Debug.LogWarning("CommandLineReader.cs - GetCustomArguments() - The custom argument [" + customArg + "] seem to be malformed.");
            }
        }

        return customArgsDict;
    }

    /// <summary>
    /// 获取cmd输入的自定义参数数值。
    /// </summary>
    /// <param name="argumentName">自定义参数名称。</param>
    /// <returns>自定义参数数值。</returns>
    public static string GetCustomArgument(string argumentName)
    {
        Dictionary<string, string> customArgsDict = GetCustomArguments();

        if (customArgsDict.TryGetValue(argumentName, out var argument))
        {
            return argument;
        }
        else
        {
            Debug.LogError("CommandLineReader.cs - GetCustomArgument() - Can't retrieve any custom argument named [" + argumentName + "] in the command line [" + GetCommandLine() + "].");
            return "";
        }
    }
}
