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
    /// 框架设置非泛型基类，用于编辑器侧类型擦除访问。
    /// </summary>
    public abstract partial class FrameworkSettings : ScriptableObject
    {
        /// <summary>
        /// 重置设置为默认值。
        /// </summary>
        /// <remarks>一般用于编辑器相关操作</remarks>
        protected internal virtual void Reset() { }

        /// <summary>
        /// 公共重置入口，供编辑器工具调用。
        /// </summary>
        public void ResetToDefaults()
        {
            Reset();
        }

        /// <summary>
        /// 根据类型全名创建并返回指定接口的实现类实例。
        /// </summary>
        /// <typeparam name="T">接口类型。</typeparam>
        /// <param name="typeName">实现类的完整类型名称（包括命名空间）。</param>
        /// <returns>类型为 <typeparamref name="T"/> 的实例。</returns>
        /// <exception cref="GameException">当类型名为空、类型不存在或实例化失败时抛出。</exception>
        /// <remarks>
        /// 此方法通过反射动态创建对象。在 Unity 2019.3+ 环境中，
        /// 建议使用 <see cref="UnityEngine.SerializeReference"/> 直接序列化抽象基类引用，Unity 会自动处理派生类的序列化与反序列化，
        /// 无需手动调用 Activator.CreateInstance。
        /// </remarks>
        public static T ResolveTypeOption<T>(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new GameException("Type can not be null or empty!.");
            }

            Type helperType = AssemblyUtility.GetType(typeName);
            if (helperType == null)
            {
                throw new GameException(StringUtility.Format("Can not find helper type '{0}'.", typeName));
            }

            T instance = (T)Activator.CreateInstance(helperType);
            if (instance == null)
            {
                throw new GameException(StringUtility.Format("Can not create helper instance '{0}'.", typeName));
            }

            return instance;
        }
    }

    /// <summary>
    /// 框架设置基类。提供统一的元数据查询、类型注册和实例加载。
    /// 所有框架设置 ScriptableObject 应继承此类。
    /// </summary>
    public abstract class FrameworkSettings<T> : FrameworkSettings where T : FrameworkSettings<T>
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
                    s_Instance = LoadSettingSO<T>(filePath, t => t.ResetToDefaults());
#else
                    Log.Error($"Could not find {type.Name} at path '{filePath}'!");
#endif
                }
                return s_Instance;
            }
        }
    }
}
