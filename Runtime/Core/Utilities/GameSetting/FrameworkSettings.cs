using System;
using System.Reflection;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 标记框架设置类的元数据，用于在 Framework Settings 窗口中自动发现和排序显示。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class FrameworkSettingAttribute : Attribute
    {
        public const string DEFAULT_SAVE_FOLDER = "Assets/Settings/Framework/Resources/";

        /// <summary>显示标题</summary>
        public string Title { get; }

        /// <summary>描述说明</summary>
        public string Description { get; }

        /// <summary>排序顺序（越小越靠前）</summary>
        public int Order { get; }

        /// <summary>配置所在的文件夹</summary>
        public string SaveFolder { get; }

        public FrameworkSettingAttribute(string title, string description = null, int order = 0, string saveFolder = DEFAULT_SAVE_FOLDER)
        {
            Title = title;
            Description = description;
            Order = order;
            SaveFolder = saveFolder;
        }
    }

    /// <summary>
    /// 框架设置基类。提供统一的元数据查询、类型注册和实例加载。
    /// 所有框架设置 ScriptableObject 应继承此类。
    /// </summary>
    /// <remarks>
    /// <para><b>本类的加载路径禁止使用 <c>LogUtility</c></b>：各 Utility 的 Handler 懒加载会经
    /// <c>GetHandlerFromSettings()</c> 回读本设置资产，而 <c>s_Instance</c> 在加载完成前恒为 null，
    /// 于是"报错说资产缺失"这一步会再次进入本 getter 并无限递归（StackOverflow，不可捕获）。
    /// 加载失败只能走 <c>Debug.LogError</c>。</para>
    /// </remarks>
    public abstract partial class FrameworkSettings<T> : ScriptableObject where T : FrameworkSettings<T>
    {
        private static T s_Instance;
        /// <summary>获取设置实例。</summary>
        public static T Instance
        {
            get
            {
                if (s_Instance != null) return s_Instance;

                // 新建配置SO

                var type = typeof(T);
                var attr = type.GetCustomAttribute<FrameworkSettingAttribute>();
                var saveFolder = attr != null ? attr.SaveFolder : FrameworkSettingAttribute.DEFAULT_SAVE_FOLDER;

                const string keyword = "/Resources/";
                int index = saveFolder.IndexOf(keyword);
                if (index != -1)
                {
                    s_Instance = Resources.Load<T>(saveFolder.Substring(index + keyword.Length) + type.Name);
                }

                if (s_Instance == null)
                {
                    string filePath = saveFolder + type.Name + ".asset";
#if UNITY_EDITOR
                    s_Instance = LoadSettingSO<T>(filePath);
#else
                    // Player 构建缺资产不得返回 null——下游（如 GameAppSettings.Initiation）会随即 NRE，
                    // 且每次访问都会重复 Resources.Load。兜底为代码默认值实例并缓存：报错一次、按默认值继续跑。
                    Debug.LogError($"Could not find {type.Name} at path '{filePath}'! Falling back to code defaults.");
                    s_Instance = CreateInstance<T>();
#endif
                }
                return s_Instance;
            }
        }
    }
}
