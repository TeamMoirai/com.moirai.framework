using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 窗口模式
    /// </summary>
    public partial class GraphicsSettings
    {
        private List<FullScreenMode> _windowModeValues;
        private List<string> _windowModeLabels;

        private FullScreenMode? _lastKnownWindowMode = null;
        private int _lastSetWindowModeFrame = 0;

        public static event Action<int> OnWindowModeChanged;

        public static List<string> GetWindowModeOptionLabels()
        {
            if (Instance._windowModeLabels == null || Instance._windowModeLabels.Count == 0)
            {
                Instance._windowModeLabels = new List<string>();

                Instance._windowModeLabels.Add("Full Screen");
                Instance._windowModeLabels.Add("Window");
                Instance._windowModeLabels.Add("Exclusive (Windows)");
                Instance._windowModeLabels.Add("Maximized (Windows/MacOS)");
            }

            return Instance._windowModeLabels;
        }

        public static void SetWindowModeOptionLabels(List<string> optionLabels)
        {
            var values = Instance.GetWindowModeOptions();
            if (optionLabels == null || optionLabels.Count != values.Count)
            {
                Debug.LogError("Invalid new labels. Need to be " + values.Count + ".");
                return;
            }

            Instance._windowModeLabels = new List<string>(optionLabels);
        }

        public static void RefreshWindowModeOptionLabels()
        {
            Instance._windowModeLabels = null;
            GetWindowModeOptionLabels();
        }

        protected List<FullScreenMode> GetWindowModeOptions()
        {
            if (_windowModeValues == null ||  _windowModeValues.Count == 0)
            {
                _windowModeValues = new List<FullScreenMode>();

                _windowModeValues.Add(FullScreenMode.FullScreenWindow);
                _windowModeValues.Add(FullScreenMode.Windowed);
                _windowModeValues.Add(FullScreenMode.ExclusiveFullScreen);
                _windowModeValues.Add(FullScreenMode.MaximizedWindow);
            }

            return _windowModeValues;
        }

        /// <summary>
        /// 返回当前窗口模式索引。
        /// </summary>
        /// <returns></returns>
        public static int GetWindowModeIndex()
        {
            // 在N帧后重置。假设此时Screen.fullScreenMode已更新完成。
            if (Time.frameCount - Instance._lastSetWindowModeFrame > 3)
                Instance._lastKnownWindowMode = null;

            FullScreenMode currentMode = Screen.fullScreenMode;
            if (Instance._lastKnownWindowMode.HasValue)
            {
                currentMode = Instance._lastKnownWindowMode.Value;
            }

            var option = Instance.GetWindowModeOptions();
            for (int i = 0; i < option.Count; i++)
            {
                if (option[i] == currentMode)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// 设置窗口模式
        /// </summary>
        /// <param name="index"></param>
        public static void SetWindowModeIndex(int index)
        {
            var options = Instance.GetWindowModeOptions();
            index = Mathf.Clamp(index, 0, options.Count - 1);
            var mode = options[index];

            // 请求变更但将实际执行委托给协调器。
            ScreenOrchestrator.Instance.RequestFullScreenMode(mode);

            // 缓存
            Instance._lastSetWindowModeFrame = Time.frameCount;
            Instance._lastKnownWindowMode = mode;

            OnWindowModeChanged?.Invoke(index);
        }
    }
}