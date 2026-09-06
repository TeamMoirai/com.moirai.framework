using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 路径信息窗口。
    /// </summary>
    public sealed class PathInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "Path Information");
            AddRow(card, "Current Directory", PathUtility.FormatToUnityPath(Environment.CurrentDirectory));
            AddRow(card, "Data Path", PathUtility.FormatToUnityPath(Application.dataPath));
            AddRow(card, "Persistent Data Path", PathUtility.FormatToUnityPath(Application.persistentDataPath));
            AddRow(card, "Streaming Assets Path", PathUtility.FormatToUnityPath(Application.streamingAssetsPath));
            AddRow(card, "Temporary Cache Path", PathUtility.FormatToUnityPath(Application.temporaryCachePath));
            AddRow(card, "Console Log Path", PathUtility.FormatToUnityPath(Application.consoleLogPath));
        }

        #endregion
    }
}
