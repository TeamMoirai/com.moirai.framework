using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos;
using Moirai.Atropos.Events;
using Moirai.Atropos.Procedure;
#if OBFUZ_INSTALLED && ENABLE_OBFUZ
using Obfuz;
#endif

namespace GameLogic
{
    /// <summary>
    /// 进入主流程事件，单次流程。
    /// </summary>
    /// <remarks>用于初始化一些初始设定</remarks>
    public class HotfixEntryEvent : EventBase<HotfixEntryEvent>, IProcedureEvent
    {
        public static void Trigger()
        {
            using var evt = GetPooled();
            EventManager.SendEvent(evt);
        }
    }

    /// <summary>
    /// 游戏主程序入口
    /// </summary>
#if OBFUZ_INSTALLED && ENABLE_OBFUZ
    [ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
#endif
    // ReSharper disable once ClassNeverInstantiated.Global
    public sealed partial class HotfixEntry
    {
        private static List<Assembly> s_HotfixAssembly;
        
        /// <summary>
        /// 热更域App主入口。
        /// </summary>
        /// <param name="objects"></param>
        [UnityEngine.Scripting.Preserve]
        public static void Entrance(object[] objects)
        {
            s_HotfixAssembly = (List<Assembly>)objects[0];

            Log.Info("<b><color=orange>======= HotFix Logic Entry =======</color></b>");

            UnityUtility.AddDestroyListener(Release);

            // 保证 UIModule 正常初始化
            GameModule.UI.CloseAll();

            // 初始化多语言配置
            GameModule.Localization.InitLanguageSettings();

            MainThreadDispatcher.Instance.Dispatch(() => { Log.Info("Init UnityMainThreadDispatcher"); });

            // 事件通知
            HotfixEntryEvent.Trigger();

            // 开始游戏相关逻辑
            StartGameLogic();
        }

        private static partial void StartGameLogic();
        
        private static void Release()
        {
            SingletonSystem.Release();
            Log.Warning("======= Release GameApp =======");
        }
    }
}