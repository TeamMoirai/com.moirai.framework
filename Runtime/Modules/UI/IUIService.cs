using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI服务接口。
    /// </summary>
    public interface IUIService
    {
        /// <summary>
        /// UI专用摄像机。
        /// </summary>
        Camera UICamera { get; }

        /// <summary>
        /// 当前模态遮挡窗口。
        /// </summary>
        UIWindow CurrentModal { get; }

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        void ShowUIAsync<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData) where T : UIWindow, new();

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        void ShowUI<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData) where T : UIWindow, new();

        /// <summary>
        /// 异步打开窗口。
        /// </summary>
        void ShowUIAsync(Type type, string windowName = null, string assetName = null, bool fromResources = false, params object[] userData);

        /// <summary>
        /// 同步打开窗口。
        /// </summary>
        void ShowUI(Type type, string windowName = null, string assetName = null, bool fromResources = false, params object[] userData);

        /// <summary>
        /// 异步打开窗口并等待加载完成。
        /// </summary>
        UniTask<UIWindow> ShowUIAsyncAwait<T>(string windowName = null, string assetName = null, bool fromResources = false, params object[] userData) where T : UIWindow, new();

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        void CloseUI<T>(string windowName = null) where T : UIWindow;

        /// <summary>
        /// 关闭窗口。
        /// </summary>
        void CloseUI(Type type, string windowName = null);

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        void HideUI<T>(string windowName = null) where T : UIWindow;

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        void HideUI(Type type, string windowName = null);

        /// <summary>
        /// 关闭所有窗口。
        /// </summary>
        void CloseAll(bool isShutDown = false);

        /// <summary>
        /// 关闭所有窗口除了指定窗口。
        /// </summary>
        void CloseAllWithOut(UIWindow withOut);

        /// <summary>
        /// 关闭所有窗口除了指定类型。
        /// </summary>
        void CloseAllWithOut<T>() where T : UIWindow;

        /// <summary>
        /// 关闭所有窗口除了指定层级。
        /// </summary>
        void CloseAllWithOut(UILayer withOut);

        /// <summary>
        /// 获取所有层级下顶部的窗口。
        /// </summary>
        UIWindow GetTopWindow();

        /// <summary>
        /// 获取指定层级下顶部的窗口。
        /// </summary>
        UIWindow GetTopWindow(int layer);

        /// <summary>
        /// 获取指定层级下顶部的窗口名称。
        /// </summary>
        string GetTopWindowName(int layer);

        /// <summary>
        /// 是否有任意窗口正在加载。
        /// </summary>
        bool IsAnyLoading();

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        bool HasWindow<T>(string windowName = null) where T : UIWindow;

        /// <summary>
        /// 查询窗口是否存在。
        /// </summary>
        bool HasWindow(Type type, string windowName = null);

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        UniTask<T> GetUIAsyncAwait<T>() where T : UIWindow;

        /// <summary>
        /// 异步获取窗口。
        /// </summary>
        void GetUIAsync<T>(Action<T> callback) where T : UIWindow;

        /// <summary>
        /// 获取指定类型和名称的窗口。
        /// </summary>
        T GetWindow<T>(string windowName) where T : UIWindow;

        /// <summary>
        /// 判断指定 UI 对象是否被模态窗口遮挡。
        /// </summary>
        bool IsBlockedByModal(GameObject obj);

        /// <summary>
        /// 判断窗口是否为模态窗口。
        /// </summary>
        bool IsModal(UIWindow window);
    }
}
