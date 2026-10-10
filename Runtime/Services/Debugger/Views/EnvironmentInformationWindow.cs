using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Moirai.Atropos.Resource;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 运行环境信息窗口。
    /// </summary>
    public sealed class EnvironmentInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "Environment Information");
            AddRow(card, "Product Name", Application.productName);
            AddRow(card, "Company Name", Application.companyName);
            AddRow(card, "Game Identifier", Application.identifier);
            AddRow(card, "Game Version", StringUtility.Format("{0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion));
            AddRow(card, "Resource Version", StringUtility.Format("{0} ({1})", VersionUtility.ResourceVersion, VersionUtility.InternalResourceVersion));
            AddRow(card, "Application Version", Application.version);
            AddRow(card, "Unity Version", Application.unityVersion);
            AddRow(card, "Platform", Application.platform.ToString());
            AddRow(card, "System Language", Application.systemLanguage.ToString());
            AddRow(card, "Cloud Project Id", Application.cloudProjectId);
            AddRow(card, "Build Guid", Application.buildGUID);
            AddRow(card, "Target Frame Rate", Application.targetFrameRate.ToString());
            AddRow(card, "Internet Reachability", Application.internetReachability.ToString());
            AddRow(card, "Background Loading Priority", Application.backgroundLoadingPriority.ToString());
            AddRow(card, "Is Playing", Application.isPlaying.ToString());
            AddRow(card, "Splash Screen Is Finished", SplashScreen.isFinished.ToString());
            AddRow(card, "Run In Background", Application.runInBackground.ToString());
            AddRow(card, "Install Name", Application.installerName);
            AddRow(card, "Install Mode", Application.installMode.ToString());
            AddRow(card, "Sandbox Type", Application.sandboxType.ToString());
            AddRow(card, "Is Mobile Platform", Application.isMobilePlatform.ToString());
            AddRow(card, "Is Console Platform", Application.isConsolePlatform.ToString());
            AddRow(card, "Is Editor", Application.isEditor.ToString());
            AddRow(card, "Is Debug Build", Debug.isDebugBuild.ToString());
            AddRow(card, "Is Focused", Application.isFocused.ToString());
            AddRow(card, "Is Batch Mode", Application.isBatchMode.ToString());
        }

        #endregion
    }
}
