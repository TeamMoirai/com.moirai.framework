using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 运行操作窗口（池冲刷、资源卸载、GC 与框架关停）。
    /// </summary>
    public sealed class OperationsWindow : ScrollableDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement poolCard = AddSection(root, "Object Pool");
            poolCard.Add(DebuggerUI.CreateActionButton("GameObject Pool Flush All", GameObjectPoolService.FlushAll));

            VisualElement resourceCard = AddSection(root, "Resource");
            resourceCard.Add(DebuggerUI.CreateActionButton("Unload Unused Assets", () => UnloadUnusedAssets(false)));
            resourceCard.Add(DebuggerUI.CreateActionButton("Unload Unused Assets and Garbage Collect", () => UnloadUnusedAssets(true)));

            VisualElement timeCard = AddSection(root, "Time");
            AddTimeScaleSlider(timeCard);

            VisualElement shutdownCard = AddSection(root, "Shutdown Game Framework");
            shutdownCard.Add(DebuggerUI.CreateActionButton("Shutdown (None)", () => GameApp.Shutdown()));
            shutdownCard.Add(DebuggerUI.CreateActionButton("Shutdown (Restart)", () =>
            {
                GameApp.Shutdown();
                SceneManager.LoadScene(0);
            }));
            shutdownCard.Add(DebuggerUI.CreateActionButton("Shutdown (Quit)", () =>
            {
                GameApp.Shutdown();
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                    }, DebuggerUI.EButtonStyle.Danger));
            }

        #endregion

        #region 私有 [PRIVATE]

        private static void UnloadUnusedAssets(bool garbageCollect)
        {
            ResourceServiceHandler resourceService = ResourceService.Handler;
            if (resourceService == null)
            {
                LogUtility.Warning("ResourceService is not initialized.");
                return;
            }

            resourceService.ForceUnloadUnusedAssets(garbageCollect);
        }

        private static void AddTimeScaleSlider(VisualElement card)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-slider-row");

            Label label = new Label(StringUtility.Format("Time Scale: {0:F2}", Time.timeScale));
            label.AddToClassList("dbg-slider-row__title");
            label.style.minWidth = 140f;

            Slider slider = DebuggerUI.CreateSlider(0f, 4f, Time.timeScale, value =>
            {
                Time.timeScale = value;
                label.text = StringUtility.Format("Time Scale: {0:F2}", value);
            });

            row.Add(label);
            row.Add(slider);
            card.Add(row);
        }

        #endregion
    }
}
