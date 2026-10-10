using System;
using System.Collections.Generic;
using Moirai.Atropos;
using Moirai.Atropos.UI;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.GameMain.UI
{
    /// <summary>
    /// 热更界面加载管理器：按窗名登记引导窗，装载失败首见必报不静默。
    /// </summary>
    public static class LauncherMgr
    {
        private static readonly Dictionary<string, LauncherUIBase> s_UIMapDict = new(4);
        private static readonly HashSet<string> s_ReportedFailures = new();

        private const string FAILURE_NO_ASSET = ":noAsset";
        private const string FAILURE_BAD_CLONE = ":badClone";
        private const string FAILURE_NO_RECT = ":noRect";

        /// <summary>
        /// 打开引导窗；同名已存在则直接复用登记表里的那一只。
        /// </summary>
        /// <typeparam name="T">窗口类型，其类名即 Resources 下的窗体名。</typeparam>
        /// <param name="param">窗口载荷。</param>
        public static void ShowUI<T>(object param = null) where T : LauncherUIBase, new()
        {
            string uiName = typeof(T).Name;
            if (!s_UIMapDict.TryGetValue(uiName, out var uiBase))
            {
                GameObject window = InstantiateWindow(uiName);
                if (window == null)
                {
                    return;
                }

                uiBase = new T();
                uiBase.gameObject = window;
                uiBase.OnInit();
                s_UIMapDict[uiName] = uiBase;
            }

            uiBase.Show(param);
        }

        /// <summary>
        /// 装载并实例化引导窗面板：先判形，再摆到 UI 根上并摆正尺寸。
        /// </summary>
        /// <param name="uiName">窗名，即 Resources 下的资源名。</param>
        /// <returns>可注入面板；三类装载失败各出一条带窗名的错误日志后回 <c>null</c>，
        /// 同窗同类只首条，已被实例化出来的那一只就地销毁，不留孤儿。</returns>
        private static GameObject InstantiateWindow(string uiName)
        {
            var asset = Resources.Load(UpdateSettings.UIWindowPath + uiName);
            if (asset == null)
            {
                if (ShouldReportFailure(uiName, FAILURE_NO_ASSET))
                {
                    LogUtility.Error("引导窗资源缺失：{0}{1}", UpdateSettings.UIWindowPath, uiName);
                }

                return null;
            }

            var clone = UObject.Instantiate(asset);
            var window = clone as GameObject;
            if (window == null)
            {
                if (ShouldReportFailure(uiName, FAILURE_BAD_CLONE))
                {
                    LogUtility.Error("引导窗预制体根物体不是 {0}：{1}", nameof(GameObject), uiName);
                }

                UObject.Destroy(clone);
                return null;
            }

            var rectTransform = window.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                if (ShouldReportFailure(uiName, FAILURE_NO_RECT))
                {
                    LogUtility.Error("引导窗缺少 {0}：{1}", nameof(RectTransform), uiName);
                }

                UObject.Destroy(window);
                return null;
            }

            window.transform.SetParent(UIService.UIRoot);
            window.name = uiName;
            window.transform.localScale = Vector3.one;
            window.transform.localPosition = Vector3.zero;
            window.transform.localRotation = Quaternion.identity;
            rectTransform.sizeDelta = Vector2.zero;
            return window;
        }

        /// <summary>
        /// 登记一次装载失败；同窗同类只首次返回 <c>true</c>，避免持续失败按帧刷屏。
        /// </summary>
        /// <param name="uiName">窗名。</param>
        /// <param name="failureKind">失败类别。</param>
        /// <returns>首次为 <c>true</c>，重复为 <c>false</c>。</returns>
        private static bool ShouldReportFailure(string uiName, string failureKind)
        {
            return s_ReportedFailures.Add(uiName + failureKind);
        }

        /// <summary>
        /// 关闭传入窗口所属的那一只引导窗。
        /// </summary>
        /// <param name="uiWindow">要关闭的窗口；其类名即登记键。</param>
        public static void CloseUI(LauncherUIBase uiWindow)
        {
            CloseUI(uiWindow.GetType().Name);
        }

        /// <summary>
        /// 关闭指定类型的引导窗。
        /// </summary>
        /// <typeparam name="T">窗口类型，其类名即登记键。</typeparam>
        public static void CloseUI<T>() where T : LauncherUIBase
        {
            CloseUI(typeof(T).Name);
        }

        /// <summary>
        /// 关闭指定窗名的引导窗：隐藏、销毁面板、即刻移掉登记键。
        /// </summary>
        /// <param name="uiName">窗名。</param>
        public static void CloseUI(string uiName)
        {
            if (!s_UIMapDict.TryGetValue(uiName, out var uiWindow))
            {
                return;
            }

            uiWindow.Hide();
            UObject.Destroy(uiWindow.gameObject);
            s_UIMapDict.Remove(uiName);
        }

        /// <summary>
        /// 取回指定类型的引导窗。
        /// </summary>
        /// <typeparam name="T">窗口类型，其类名即登记键。</typeparam>
        /// <returns>登记中的窗口；未登记为 <c>null</c>。</returns>
        public static T GetActiveUI<T>() where T : LauncherUIBase
        {
            return GetActiveUI(typeof(T).Name) as T;
        }

        /// <summary>
        /// 取回指定窗名的引导窗。
        /// </summary>
        /// <param name="uiName">窗名。</param>
        /// <returns>登记中的窗口；未登记为 <c>null</c>。</returns>
        public static LauncherUIBase GetActiveUI(string uiName)
        {
            return s_UIMapDict.GetValueOrDefault(uiName);
        }

        /// <summary>
        /// 隐藏并销毁全部登记中的引导窗，随后清空登记表与失败登记表。
        /// </summary>
        public static void HideAllUI()
        {
            foreach (var ui in s_UIMapDict.Values)
            {
                ui?.Hide();
                UObject.Destroy(ui?.gameObject);
            }

            s_UIMapDict.Clear();
            s_ReportedFailures.Clear();
        }

        #region UI调用

        /// <summary>
        /// 弹出提示窗并挂上三个按钮回调。
        /// </summary>
        /// <param name="desc">提示正文。</param>
        /// <param name="onConfirm">确认回调。</param>
        /// <param name="onCancel">取消回调。</param>
        /// <param name="onUpdate">重试回调。</param>
        public static void ShowMessageBox(string desc, Action onConfirm = null,
            Action onCancel = null, Action onUpdate = null)
        {
            ShowUI<LoadTipsUI>(desc);
            var ui = GetActiveUI<LoadTipsUI>();
            ui?.SetAllCallback(onConfirm, onUpdate, onCancel);
        }

        /// <summary>
        /// 刷新进度窗的进度条。
        /// </summary>
        /// <param name="progress">进度，取值 0 到 1。</param>
        public static void RefreshProgress(float progress)
        {
            ShowUI<LoadUpdateUI>();
            var ui = GetActiveUI<LoadUpdateUI>();
            ui?.RefreshProgress(progress);
        }

        /// <summary>
        /// 刷新UI版本号。
        /// </summary>
        /// <param name="appId">AppID。</param>
        /// <param name="resId">资源ID。</param>
        public static void RefreshVersion(string appId, string resId)
        {
            ShowUI<LoadUpdateUI>();

            var ui = GetActiveUI<LoadUpdateUI>();
            ui?.RefreshAppid(appId);
            ui?.RefreshVersion(resId);
        }

        #endregion
    }
}
