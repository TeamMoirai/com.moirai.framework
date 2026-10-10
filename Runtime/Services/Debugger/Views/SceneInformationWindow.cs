using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 场景信息窗口。
    /// </summary>
    public sealed class SceneInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "Scene Information");
            AddRow(card, "Scene Count", SceneManager.sceneCount.ToString());
            AddRow(card, "Scene Count In Build Settings", SceneManager.sceneCountInBuildSettings.ToString());

            UnityEngine.SceneManagement.Scene activeScene = SceneManager.GetActiveScene();
            AddRow(card, "Active Scene Handle", activeScene.handle.ToString());
            AddRow(card, "Active Scene Name", activeScene.name);
            AddRow(card, "Active Scene Path", activeScene.path);
            AddRow(card, "Active Scene Build Index", activeScene.buildIndex.ToString());
            AddRow(card, "Active Scene Is Dirty", activeScene.isDirty.ToString());
            AddRow(card, "Active Scene Is Loaded", activeScene.isLoaded.ToString());
            AddRow(card, "Active Scene Is Valid", activeScene.IsValid().ToString());
            AddRow(card, "Active Scene Root Count", activeScene.rootCount.ToString());
            AddRow(card, "Active Scene Is Sub Scene", activeScene.isSubScene.ToString());
        }

        #endregion
    }
}
