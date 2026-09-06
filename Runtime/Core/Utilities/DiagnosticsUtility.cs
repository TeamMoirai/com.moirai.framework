using System;
using System.Diagnostics;
using System.Reflection;

namespace Moirai.Atropos
{
    /// <summary>
    /// 通知框架的堆栈跟踪（stack trace）记录使用此属性的方法或构造函数的跟踪帧（trace frame）
    /// 指示应将方法或构造函数用作获取特定堆栈帧的参考点。
    /// 使用时，它有助于查找和检索与跟踪目的相关的堆栈帧。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    public sealed class StackTraceFrameAttribute : Attribute
    {
        
    }
    
    /// <summary>
    /// 诊断实用程序
    /// </summary>
    public static class DiagnosticsUtility
    {
        public static StackFrame GetCurrentStackFrame()
        {
            StackTrace stackTrace = new StackTrace(1, true);
            int frameId = 0;

            for (int i = 0; i < stackTrace.FrameCount; i++)
            {
                MethodBase method = stackTrace.GetFrame(i).GetMethod();
                if (method.GetCustomAttribute<StackTraceFrameAttribute>() != null)
                {
                    frameId = i;
                }
            }

            if (frameId < stackTrace.FrameCount - 1) ++frameId;
            return stackTrace.GetFrame(frameId);
        }
        
        public static string GetDelegatePath(Delegate callback)
        {
            var declType = callback.Method.DeclaringType?.Name ?? string.Empty;
            string itemName = $"{declType}.{callback.Method.Name}";
            if (callback.Target != null)
            {
                string objectName = callback.Target.ToString();
                itemName = $"{itemName}>[{objectName}]";
            };
            return itemName;
        }
    }
}