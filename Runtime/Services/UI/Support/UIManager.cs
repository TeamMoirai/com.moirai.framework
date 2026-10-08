using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Events;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 界面管理器基础实现
    /// </summary>
    public class UIManager : SingletonMono<UIManager>
    {
        [Tooltip("是否从配置表获取弹窗预制体的定位地址，否则从Resources加载。")]
        [SerializeField] private bool m_LoadByConfig = true;
        
        [Tooltip("UI置于 Resources 文件夹下的父文件夹")]
        [HideIf(nameof(m_LoadByConfig))]
	    [FolderPath(ParentFolder = "Assets/Resources")]
        [SerializeField] private string m_UIFolder = "UI";
        
        /// <summary>
        /// 弹窗资源是否从资源中加载
        /// </summary>
        protected virtual bool FromResources => !m_LoadByConfig;

        #region 引擎方法 [UNITY METHODS]

        protected virtual void OnEnable()
        {
            EventManager.RegisterCallback<UIWindowEvent>(OnUIWindowEvent);
        }

        protected virtual void OnDisable()
        {
            EventManager.UnregisterCallback<UIWindowEvent>(OnUIWindowEvent);
        }
        
        #endregion
        
        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 根据 id 加载弹窗。
        /// </summary>
        /// <param name="configKey">LoadByConfig: 配置表id；Resources下的预制体名称。</param>
        public virtual void LoadUGUI<T>(string configKey) where T : UGUIWindow, new()
        {
            UIService.ShowUIAsync<T>(configKey, GetWindowLocation(configKey), FromResources);
        }

        #endregion
        
        #region 私有方法 [PRIVATE METHODS]
        
        /// <summary>
        /// 获取弹窗资产的位置
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        protected virtual string GetWindowLocation(string id)
        {
            // LogUtility.Info("Load UI: {0}", id);
            return m_LoadByConfig ?
                ConfigTableService.GetUIWindowLocation(id) :
                StringUtility.Concat(m_UIFolder, "/", id);
        }
        
        #endregion
        
        #region 事件 [EVENTS]

        /// <summary>
        /// 常规弹窗操作
        /// </summary>
        /// <remarks>不传参的简单弹窗</remarks>
        protected virtual void OnUIWindowEvent(UIWindowEvent evt)
        {
            switch (evt.Mode)
            {
                case UIWindowEvent.EMode.Show:
                    UIService.ShowUIAsync(evt.WindowType, evt.WindowId, GetWindowLocation(evt.WindowId), FromResources, evt.Params);
                    // LogUtility.Info($"Show UI {evt.WindowId}");
                    break;

                case UIWindowEvent.EMode.Close:
                    UIService.CloseUI<UIWindow>(evt.WindowId);
                    // LogUtility.Info($"Close UI {evt.WindowId}");
                    break;

                case UIWindowEvent.EMode.Hide:
                    UIService.HideUI<UIWindow>(evt.WindowId);
                    // LogUtility.Info($"Hide {evt.WindowId}");
                    break;

                case UIWindowEvent.EMode.CloseAll:
                    UIService.CloseAll();
                    // LogUtility.Info($"Close all UI");
                    break;
            }
        }
        
        #endregion
        
    }
}