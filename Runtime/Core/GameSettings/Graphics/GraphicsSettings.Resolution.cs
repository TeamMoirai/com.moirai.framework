using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 自定义分辨率
    /// </summary>
    [Serializable]
    public class CustomResolution
    {
        public int width = 1024;
        public int height = 768;

        // 自 Unity 2022.2 起，刷新率由分子和分母定义（例如 60 Hz 为 60000 和 1001）
#if UNITY_2022_2_OR_NEWER
        public uint refreshNumerator = 60000;
        public uint refreshDenominator = 1001;
#else
        public int refreshRate = 60;
#endif

        public Resolution ToResolution()
        {
            var res = new Resolution();
            res.width = width;
            res.height = height;
#if UNITY_2022_2_OR_NEWER
            var r = new RefreshRate();
            r.numerator = refreshNumerator;
            r.denominator = refreshDenominator;
            res.refreshRateRatio = r;
#else
            res.refreshRate = refreshRate;
#endif

            return res;
        }

        public static Resolution[] ToResolutions(List<CustomResolution> customResolutions)
        {
            var resolutions = new Resolution[customResolutions.Count];
            for (int i = 0; i < customResolutions.Count; i++)
            {
                resolutions[i] = customResolutions[i].ToResolution();
            }
            return resolutions;
        }
    }

    /// <summary>
    /// 分辨率
    /// </summary>
    public partial class GraphicsSettings
    {
        [Header("分辨率 [Resolution]")]

        [Tooltip("如果可选择的分辨率经常变化，请禁用此功能。")]
        [SerializeField] private bool m_CacheResolutions = true;

        [Tooltip("如果启用，则仅列出与当前分辨率刷新率匹配的分辨率选项。该列表可能比完整列表短得多。")]
        [SerializeField] private bool m_LimitToCurrentRefreshRate = false;

        [Tooltip("如果启用，则每个频率只列出一个分辨率。\n例如，可能有两个分辨率：640x480 @60Hz 和 640x480 @75Hz。\n如果启用，则列表中只会显示其中一个。它将选择与当前使用的频率最接近的那个。")]
        [SerializeField] private bool m_LimitToUniqueResolutions = true;

        [Tooltip("如果启用，则任何比最大屏幕（硬件分辨率）的宽度或高度更大的分辨率都将被跳过。\n注意：在编辑器中此功能无效，因为 API 在那里不会返回正确的尺寸。请在真实构建中进行测试。")]
        [SerializeField] private bool m_LimitMaxResolutionToDisplayResolution = true;

        [Tooltip("如果启用，当存在 60 Hz 的替代选项时，刷新率为 59 Hz 的分辨率将被跳过（仅在此情况下）。")]
        [SerializeField] private bool m_SkipRefreshRatesWith59Hz = true;

        [Tooltip("是否应该将刷新率（频率）添加到标签中。\n“false：1024x768” \n“true：1024x768（60Hz）")]
        [SerializeField] private bool m_AddRefreshRateToLabels = false;
        [Tooltip("如果启用了 AddRefreshRateToLabels，将附加到普通分辨率字符串。{0} 是刷新率的整数值。")]
        [ShowIf(nameof(m_AddRefreshRateToLabels))]
        [SerializeField] private string m_RefreshRateFormat = " ({0}Hz)";

        [Tooltip("纵横比列表（x = 宽度，y = 高度），用于作为分辨率列表的正向过滤条件。\n如果列表为空，则不会进行过滤，所有分辨率都会被列出。")]
        [SerializeField] private List<Vector2Int> m_AllowedAspectRatios = new List<Vector2Int>();

        [Tooltip("分辨率与定义的允许纵横比之间可以差异的阈值。\n例如，如果允许的纵横比是16:9（宽:高），即1.77，而这个阈值是0.02，那么所有在1.75到1.79之间的比例都是有效的。")]
        [SerializeField] private float m_AllowedAspectRatioDelta = 0.02f;

        [Tooltip("自定义分辨率。如果不为空，则将使用此列表作为基础分辨率列表，而不是 Unity 检测到的所有分辨率。")]
        [SerializeField] private List<CustomResolution> m_CustomResolutions = new List<CustomResolution>();

        [Tooltip("分辨率格式 {0} = 宽度（像素）。{1} = 高度（像素）。")]
        [SerializeField] private string m_ResolutionFormat = "{0}x{1}";

        [Tooltip("如果启用，则自定义分辨率选项将作为第一个选项添加。")]
        [SerializeField] private bool m_AddCustomResolutionOptionIfWindowed = false;

        /// <summary>
        /// 不建议在移动设备上更改分辨率设置。
        /// 这样做可能会引发意外的副作用，而且通常移动设备只支持单一分辨率。
        /// </summary>
        public static bool AllowResolutionChangeOnMobile = false;

        private List<Resolution> _resolutionValues;
        private List<string> _resolutionLabels;
        // 使用这个值来检测可用分辨率是否发生了变化。
        // 这种情况通常发生在应用程序被移动到另一个显示器时。
        private Vector2Int _lastMonitorMaxResolution;

        public static event Action OnMaxResolutionChanged;
        public static event Action<int> OnResolutionChanged;

        /// <summary>
        /// 是否处于窗口模式
        /// </summary>
        private bool IsWindowed => Screen.fullScreenMode == FullScreenMode.Windowed;

        private Resolution[] GetResolutions()
        {
            if (m_CustomResolutions.Count > 0)
            {
                return CustomResolution.ToResolutions(m_CustomResolutions);
            }
            return Screen.resolutions;
        }

        public static Vector2Int GetCurrentMaxResolution()
        {
            var resolutions = Instance.GetResolutions();
            return new Vector2Int(resolutions[resolutions.Length - 1].width, resolutions[resolutions.Length - 1].height);
        }

        // 缓存当前窗口化分辨率。同时作为标记，用于判断是否已将其添加到 _resolutionValues 列表中（若为NULL则表示未添加）。
        private Resolution? _windowedResolution;

        private List<Resolution> GetUniqueResolutions()
        {
            if (_resolutionValues == null || _resolutionValues.Count == 0 || !Instance.m_CacheResolutions)
            {
                _resolutionValues = new List<Resolution>();

                // 生成一个与当前分辨率刷新率相同的分辨率列表。
                Resolution[] resolutions = GetResolutions();
                FilterResolutionsAndAddToValues(resolutions, limitAspectRatios: true);
                // 如果未找到任何解决方案，则不进行过滤。
                if (_resolutionValues.Count == 0)
                {
                    Log.Warning("Resolution aspect ratio limiting resulted in an empty list. Disabling filtering (all resolutions will be listed).");
                    FilterResolutionsAndAddToValues(resolutions, limitAspectRatios: false);
                }

                // 如果启用了窗口模式，始终将当前窗口分辨率作为首个分辨率添加。
                // 稍后再做这件事，以免被过滤掉。
                if (Instance.m_AddCustomResolutionOptionIfWindowed)
                {
                    ScreenSizeObserver.Instance.onScreenSizeChanged -= OnScreenSizeChanged;
                    ScreenSizeObserver.Instance.onScreenSizeChanged += OnScreenSizeChanged;
                    if (IsWindowed)
                    {
                        var res = ScreenOrchestrator.GetCurrentResolution();
                        AddOrRemoveCustomResolutionValue(res);
                    }
                }

                // 硬回退（Hard fallback）
                if (_resolutionValues.Count == 0)
                {
                    var res = new Resolution();
                    res.width = 1024;
                    res.height = 768;
#if UNITY_2022_2_OR_NEWER
                    var r = new RefreshRate();
                    r.numerator = 60000;
                    r.denominator = 1001;
                    res.refreshRateRatio = r;
#else
                    res.refreshRate = 60;
#endif
                    _resolutionValues.Add(res);
                }
            }

            return _resolutionValues;
        }

        private void FilterResolutionsAndAddToValues(Resolution[] resolutions, bool limitAspectRatios)
        {
#if UNITY_EDITOR
            // 遗憾的是，Display.systemWidth API在编辑器中无法返回原生分辨率。因此禁用了此功能。
            // 详见：https://forum.unity.com/threads/display-systemwidth-returns-game-view-width-in-editor.1456138/
            Instance.m_LimitMaxResolutionToDisplayResolution = false;
#endif
            float maxSystemWidth = 0f;
            float maxSystemHeight = 0f;
            if (Instance.m_LimitMaxResolutionToDisplayResolution)
            {
                foreach (var display in Display.displays)
                {
                    maxSystemWidth = Mathf.Max(maxSystemWidth, display.systemWidth);
                    maxSystemHeight = Mathf.Max(maxSystemHeight, display.systemHeight);
                }
            }

            foreach (var res in resolutions)
            {
                // 如果有60赫兹的选项，则跳过59赫兹的分辨率。
                if (Instance.m_SkipRefreshRatesWith59Hz && !Instance.m_LimitToCurrentRefreshRate && GetRoundedRefreshRate(res) == 59)
                {
                    var resWith60Hz = FindResolution(resolutions, res.width, res.height, 60);
                    // 发现 60 Hz alternative > skip.
                    if (resWith60Hz.HasValue)
                        continue;
                }

                // 将选项限制为相同的刷新率：
                // 当前刷新率有时显示为59（实际为59.94），但在其他分辨率下报告的刷新率却是60。
                // 为避免在开启LimitToCurrentRefreshRate时出现空分辨率列表，允许±1的偏差范围。
                if (Instance.m_LimitToCurrentRefreshRate && Mathf.Abs(GetRoundedRefreshRate(Screen.currentResolution) - GetRoundedRefreshRate(res)) > 1)
                    continue;

                if (Instance.m_LimitToUniqueResolutions)
                {
                    // 跳过完全重复的内容
                    if (Contains(_resolutionValues, res))
                        continue;
                }

                if (Instance.m_LimitMaxResolutionToDisplayResolution && maxSystemWidth > 0f)
                {
                    if (res.width > maxSystemWidth || res.height > maxSystemHeight)
                        continue;
                }

                // 按宽高比筛选结果
                if (limitAspectRatios && Instance.m_AllowedAspectRatios != null && Instance.m_AllowedAspectRatios.Count > 0)
                {
                    float ratio = (float)res.width / res.height;
                    foreach (var aspect in Instance.m_AllowedAspectRatios)
                    {
                        float allowedRatio = (float)aspect.x / aspect.y;
                        if (Mathf.Abs(ratio - allowedRatio) <= Instance.m_AllowedAspectRatioDelta)
                        {
                            _resolutionValues.Add(res);
                            break;
                        }
                    }
                }
                else
                {
                    // 不过滤
                    _resolutionValues.Add(res);
                }
            }

            // 必须在列表填充完成后进行高级重复分辨率检查，
            // 否则“检查是否存在另一个更小差异的分辨率”也会包含已经被过滤掉的分辨率。
            if (Instance.m_LimitToUniqueResolutions)
            {
                for (int i = _resolutionValues.Count-1; i >= 0; i--)
                {
                    var res = _resolutionValues[i];

                    // 跳过重复的分辨率设置，但保留刷新率最接近的那一项。
                    //   例如可能有两种分辨率选项：640x480 @60Hz 和 640x480 @75Hz
                    //   如果启用，则列表中仅会保留其中一个选项。系统将选择与当前使用频率最接近的频率项。
                    int refreshRateDelta = Mathf.Abs(GetRoundedRefreshRate(Screen.currentResolution) - GetRoundedRefreshRate(res));
                    // 检查是否存在另一个更小差异的分辨率
                    int smallerDelta = int.MaxValue;
                    foreach (var r in _resolutionValues)
                    {
                        if (r.width != res.width || r.height != res.height)
                            continue;

                        smallerDelta = Mathf.Abs(GetRoundedRefreshRate(Screen.currentResolution) - GetRoundedRefreshRate(r));
                        if (smallerDelta < refreshRateDelta)
                            break;
                    }
                    // 若找到刷新率差值更小的其他分辨率则跳过。
                    if (smallerDelta < refreshRateDelta)
                    {
                        _resolutionValues.RemoveAt(i);
                        continue;
                    }
                }
            }
        }

        private Resolution? FindResolution(Resolution[] resolutions, int width, int height, int refreshRate)
        {
            foreach (var res in resolutions)
            {
                // 将选项限制为相同的刷新率。
                int roundedRefreshRate = GetRoundedRefreshRate(res);
                if (res.width == width && res.height == height && roundedRefreshRate == refreshRate)
                {
                    return res;
                }
            }

            return null;
        }

        private int GetRoundedRefreshRate(Resolution res)
        {
#if UNITY_2022_2_OR_NEWER
            return Mathf.RoundToInt((float)res.refreshRateRatio.value);
#else
            return res.refreshRate;
#endif
        }

        private bool Contains(List<Resolution> resolutions, Resolution resolution)
        {
            if (resolutions == null || resolutions.Count == 0)
                return false;

            for (int i = 0; i < resolutions.Count; i++)
            {
                // 将59赫兹和60赫兹视为相同。
                bool rateIsSimilar = Mathf.Abs(GetRoundedRefreshRate(resolutions[i]) - GetRoundedRefreshRate(resolution)) <= 1;
                if (resolution.width == resolutions[i].width && resolution.height == resolutions[i].height && rateIsSimilar)
                    return true;
            }

            return false;
        }

        public static List<string> GetResolutionOptionLabels()
        {
            // 如果显示器最大分辨率发生变化，则重置数值和标签。
            var maxResolution = GetCurrentMaxResolution();
            if (maxResolution != Instance._lastMonitorMaxResolution)
            {
                Instance._lastMonitorMaxResolution = maxResolution;

                Instance._resolutionValues = null;
                Instance._resolutionLabels = null;

                OnMaxResolutionChanged?.Invoke();
            }

            if (Instance._resolutionLabels == null || Instance._resolutionLabels.Count == 0 || !Instance.m_CacheResolutions)
            {
                Instance._resolutionLabels = new List<string>();

                var resolutions = Instance.GetUniqueResolutions();
                foreach (var res in resolutions)
                {
                    string name = string.Format(Instance.m_ResolutionFormat, res.width, res.height);
                    if (Instance.m_AddRefreshRateToLabels)
                    {
                        name += string.Format(Instance.m_RefreshRateFormat, Instance.GetRoundedRefreshRate(res));
                    }
                    Instance._resolutionLabels.Add(name);
                }
            }

            return Instance._resolutionLabels;
        }

        public static void RefreshResolutionOptionLabels()
        {
            Instance._resolutionLabels = null;
            GetResolutionOptionLabels();
        }

        public static void SetResolutionOptionLabels(List<string> optionLabels)
        {
            var resolutions = Instance.GetUniqueResolutions();
            if (optionLabels == null || optionLabels.Count != resolutions.Count)
            {
                Log.Error("Invalid new labels. Need to be " + resolutions.Count + ".");
                return;
            }

            Instance._resolutionLabels = new List<string>(optionLabels);
        }

        private void OnScreenSizeChanged(Resolution resolution)
        {
            // 更新选项
            AddOrRemoveCustomResolutionValue(ScreenOrchestrator.GetCurrentResolution());
            RefreshResolutionOptionLabels();
        }

        private void AddOrRemoveCustomResolutionValue(Resolution resolution)
        {
            if (IsWindowed && Instance.m_AddCustomResolutionOptionIfWindowed)
            {
                if (_windowedResolution.HasValue)
                {
                    // 更新
                    _windowedResolution = resolution;
                    _resolutionValues[0] = resolution;
                }
                else
                {
                    // 更改为 windowed? > Add custom res
                    _windowedResolution = resolution;
                    _resolutionValues.Insert(0, resolution);
                }
            }
            else
            {
                if (_windowedResolution.HasValue)
                {
                    // 更改为 not windowed? > remove custom res
                    _resolutionValues.RemoveAt(0);
                    _windowedResolution = null;
                }
            }
        }

        private Resolution? _lastKnownResolution = null;
        private int _lastSetResolutionFrame = 0;

        /// <summary>
        /// 获取当前分辨率索引
        /// </summary>
        /// <returns></returns>
        public static int GetResolutionIndex()
        {
            // 每N帧后重置，以便ScreenOrchestrator有时间调整分辨率。
            if (Time.frameCount - Instance._lastSetResolutionFrame > 3)
                Instance._lastKnownResolution = null;

            // 定义要查找的宽度和高度，因为在窗口模式下，
            // Screen.currentResolution 仍会返回显示器的分辨率，而非窗口的分辨率。
            int width = Instance.IsWindowed ? Screen.width : Screen.currentResolution.width;
            int height = Instance.IsWindowed ? Screen.height : Screen.currentResolution.height;

            // 在编辑器中，Screen.width/height的值取决于代码的调用来源，因此尝试使用更可靠的替代方案。
#if UNITY_EDITOR
            if (Instance.IsWindowed && Camera.main != null)
            {
                width = Camera.main.pixelWidth;
                height = Camera.main.pixelHeight;
            }
#endif

            if (Instance._lastKnownResolution.HasValue)
            {
                width = Instance._lastKnownResolution.Value.width;
                height = Instance._lastKnownResolution.Value.height;
            }

            // 寻找最接近的分辨率。通常情况下它们会完全匹配，但在显示器更换后可能不一致，因此需要搜索最佳匹配方案。
            var resolutions = Instance.GetUniqueResolutions();
            int minDelta = int.MaxValue;
            int closestResolutionIndex = 0;
            for (int i = 0; i < resolutions.Count; i++)
            {
                int delta = Mathf.Abs(resolutions[i].width - width) + Mathf.Abs(resolutions[i].height - height);
                if (delta < minDelta)
                {
                    minDelta = delta;
                    closestResolutionIndex = i;

                    // 完全匹配则返回
                    if (minDelta == 0)
                    {
                        return i;
                    }
                }
            }

            return closestResolutionIndex;
        }

        /// <summary>
        /// 根据索引设置分辨率。<br/>
        /// 详见：https://docs.unity3d.com/ScriptReference/Screen.SetResolution.html
        /// </summary>
        /// <param name="index"></param>
        /// <remarks>
        /// 注意：在编辑器中无效。<br/>
        /// 注意：分辨率切换不会立即生效，而是在当前帧渲染完成后才会执行。<br/>
        /// </remarks>
        public static void SetResolutionIndex(int index)
        {
#if UNITY_ANDROID || UNITY_IOS || UNITY_SWITCH
            if (!AllowResolutionChangeOnMobile)
            {
                Log.Warning("Allow resolution change on mobile is disabled. It is not advisable to change the resolution on mobile. It may have unexpected sideeffects and there usually is just one anyways. If you are on URP then use the renderScale instead.");
                return;
            }
#endif

            var resolutions = Instance.GetUniqueResolutions();
            index = Mathf.Clamp(index, 0, Mathf.Max(0, resolutions.Count - 1));
            var resolution = resolutions[index];

            // 请求变更但将实际执行委托给协调器。
            ScreenOrchestrator.Instance.RequestResolution(resolution);

            // 缓存
            Instance._lastSetResolutionFrame = Time.frameCount;
            Instance._lastKnownResolution = resolution;

            OnResolutionChanged?.Invoke(index);
        }
    }
}