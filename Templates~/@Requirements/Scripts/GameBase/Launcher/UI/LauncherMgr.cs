using System;
using System.Collections.Generic;
using Moirai.Atropos;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Main
{
    /// <summary>
    /// 热更界面加载管理器。
    /// </summary>
    public static class LauncherMgr
    {
        private static string s_UIRootPath = "UIRoot/UICanvas";
        private static Transform s_UIRoot;
        private static readonly Dictionary<string, UIBase> s_UIMapDict = new Dictionary<string, UIBase>(4);

        public static void Initialize()
        {
            s_UIRoot = GameObject.Find(s_UIRootPath)?.transform;

            if (s_UIRoot == null)
            {
                Debug.LogError($"======== 找不到 UIRoot 节点 请检查资源路径或Hierarchy窗口中的游戏对象 ========");
                return;
            }

            // Debug.Log("======== 初始化 LauncherMgr 完成 ========");
        }

        public static void ShowUI<T>(object param = null) where T : UIBase, new()
        {
            string uiName = typeof(T).Name;
            if (string.IsNullOrEmpty(uiName))
            {
                Debug.LogWarning($"======== LauncherMgr.ShowUI UIName is null ========");
                return;
            }

            if (!s_UIMapDict.TryGetValue(uiName, out var uiBase))
            {
                Object obj = Resources.Load(UpdateSettings.UIWindowPath + uiName);
                if (obj != null)
                {
                    var uiWindow = Object.Instantiate(obj) as GameObject;

                    if (uiWindow != null)
                    {
                        uiWindow.transform.SetParent(s_UIRoot.transform);
                        uiWindow.name = uiName;
                        uiWindow.transform.localScale = Vector3.one;
                        uiWindow.transform.localPosition = Vector3.zero;
                        uiWindow.transform.localRotation = Quaternion.identity;
                        RectTransform rectTransform = uiWindow.GetComponent<RectTransform>();
                        rectTransform.sizeDelta = Vector2.zero;

                        uiBase = new T();
                        uiBase.gameObject = uiWindow;
                        uiBase.OnInit();
                        s_UIMapDict[uiName] = uiBase;
                    }
                }
            }

            uiBase?.Show(param);
        }

        public static void CloseUI(UIBase uiWindow)
        {
            CloseUI(uiWindow.GetType().Name);
        }

        public static void CloseUI<T>() where T : UIBase
        {
            CloseUI(typeof(T).Name);
        }

        public static void CloseUI(string uiName)
        {
            if (string.IsNullOrEmpty(uiName))
            {
                Debug.LogWarning($"======== LauncherMgr.HideUI UIName is null ========");
                return;
            }

            if (!s_UIMapDict.TryGetValue(uiName, out var uiWindow))
            {
                return;
            }

            uiWindow?.Hide();
            Object.DestroyImmediate(uiWindow?.gameObject);
            s_UIMapDict.Remove(uiName);
        }

        public static T GetActiveUI<T>() where T : UIBase
        {
            return GetActiveUI(typeof(T).Name) as T;
        }

        public static UIBase GetActiveUI(string uiName)
        {
            return s_UIMapDict.GetValueOrDefault(uiName);
        }

        public static void HideAllUI()
        {
            foreach (var ui in s_UIMapDict.Values)
            {
                ui?.Hide();
                Object.Destroy(ui?.gameObject);
            }
            s_UIMapDict.Clear();
        }

        #region UI调用

        public static void ShowMessageBox(string desc, Action onConfirm = null,
            Action onCancel = null, Action onUpdate = null)
        {
            ShowUI<LoadTipsUI>(desc);
            var ui = GetActiveUI<LoadTipsUI>();
            ui?.SetAllCallback(onConfirm, onUpdate, onCancel);
        }

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