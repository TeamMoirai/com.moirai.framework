using Moirai.Atropos.ConfigTable;
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

        // 本体留空是刻意的扩展缝：派生类（如项目的界面管理器）往里挂自己的订阅，
        // 空体让派生类可以照旧 base 调用而不必判断上游有没有实现。
        protected virtual void OnEnable()
        {
        }

        protected virtual void OnDisable()
        {
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
        
        #region 定位口 [LOCATION RESOLVERS]

        /// <summary>解析窗口资产定位地址：直调开窗腿的寻址接缝，按本实例的配置表/Resources 档换算。</summary>
        /// <exception cref="GameException">实例不可用（关停窗口期/编辑模式）。</exception>
        public static string ResolveWindowLocation(string windowId)
        {
            var instance = Instance;
            if (instance == null)
            {
                throw new GameException(StringUtility.Format(
                    "{0}.{1} requires a live {0} instance in scene.", nameof(UIManager), nameof(ResolveWindowLocation)));
            }

            return instance.GetWindowLocation(windowId);
        }

        /// <summary>本实例的面板取法（配置表/Resources）：与 <see cref="ResolveWindowLocation"/> 成对使用。</summary>
        /// <exception cref="GameException">实例不可用（关停窗口期/编辑模式）。</exception>
        public static bool ResolveFromResources
        {
            get
            {
                var instance = Instance;
                if (instance == null)
                {
                    throw new GameException(StringUtility.Format(
                        "{0}.{1} requires a live {0} instance in scene.", nameof(UIManager), nameof(ResolveFromResources)));
                }

                return instance.FromResources;
            }
        }

        #endregion
        
    }
}