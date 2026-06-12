#if !UNITY_EDITOR && !UNITY_6000_0_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;

namespace Moirai.Main
{
    /// <summary>
    /// Unity 6 可以在设置中直接关闭<br/>
    /// Project Settings -> Player -> Slash Image -> Show Splash Screen = False
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public class SkipUnityLogo
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void BeforeSplashScreen()
        {
#if UNITY_WEBGL
            Application.focusChanged += Application_focusChanged;
#else
            System.Threading.Tasks.Task.Run(AsyncSkip);
#endif
        }

#if UNITY_WEBGL
        private static void Application_focusChanged(bool obj)
        {
            Application.focusChanged -= Application_focusChanged;
            SplashScreen.Stop(SplashScreen.StopBehavior.StopImmediate);
        }
#else
        private static void AsyncSkip()
        {
            SplashScreen.Stop(SplashScreen.StopBehavior.StopImmediate);
        }
#endif
    }
}
#endif