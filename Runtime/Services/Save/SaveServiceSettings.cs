using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务设置：存档处理器（存储管线策略——存储后端/压缩提供方/迁移回写随处理器内聚配置）、默认序列化后端与文件扩展名、资产目录、预制体注册表与截图配置。
    /// </summary>
    [FrameworkSetting("[服务]存档设置", "存档格式与加密配置", -410)]
    public sealed class SaveServiceSettings : FrameworkSettings<SaveServiceSettings>
    {
        [Tooltip("存储管线处理器（PlainSaveHandler / AESEncryptedSaveHandler）。存储后端/压缩提供方/迁移回写与密钥提供方均内嵌在处理器上配置，不在本设置平铺。")]
        [ProviderDropdown]
        [SerializeReference] private SaveServiceHandler m_SaveServiceHandler = SaveService.CreateDefaultHandler();
        /// <summary>存档处理器实例（由 Inspector 序列化配置，可替换存储管线策略）。</summary>
        public static SaveServiceHandler SaveServiceHandler => Instance.m_SaveServiceHandler;

        /// <summary>配置自检：加密处理器的密钥是否仍是出厂占位值。</summary>
        /// <remarks>
        /// 实例成员，不经 <see cref="FrameworkSettings{T}.Instance"/> 取用：构建期校验器缺资产时只读不新建。
        /// </remarks>
        internal bool UsesPlaceholderSaveKey =>
            m_SaveServiceHandler is AESEncryptedSaveHandler handler &&
            handler.KeyProvider.UsesPlaceholderCredentials;

        [Tooltip("默认序列化后端（实现类全名）：未显式声明后端的数据块（无 SaveDataAttribute）使用该后端。" +
                  "下拉列已编译入包的 ISaveSerializer 实现，缺 NuGet 依赖的后端不在候选里；" +
                  "名字解析不出时回退内置 JSON 并记一次 Fatal（同一份配置不重复报）。")]
        [ProviderDropdown(typeof(ISaveSerializer), "Default Backend")]
        [SerializeField] internal string m_DefaultSerializerTypeName = typeof(JsonSaveSerializer).FullName;

        /// <summary>已解析的默认序列化器（与 <see cref="s_ResolvedTypeName"/> 配对做缓存，仅主线程读写）。</summary>
        private static ISaveSerializer s_DefaultSerializer;

        /// <summary>上次解析所用的配置名；解析失败同样记账，免得每读一次就重报 Fatal 并新建实例。</summary>
        private static string s_ResolvedTypeName;

        /// <summary>默认序列化后端实例（按配置名缓存；解析后确保该 ID 在 <see cref="SaveSerializerRegistry"/> 有主）。</summary>
        /// <remarks>
        /// 仅主线程触达：<see cref="FrameworkSettings{T}.Instance"/> 走 <c>Resources.Load</c>，工作线程调用即崩； <br />
        /// 首次读取（或配置名变更后）解析一次，之后按名命中缓存，实例化不在每次存块上重复发生。 <br />
        /// 内置实现本就占着自己的 ID，此处不换实例——同类型两实例对无状态序列化器等价。
        /// </remarks>
        internal static ISaveSerializer DefaultSerializer
        {
            get
            {
                string configured = Instance.m_DefaultSerializerTypeName;
                if (s_DefaultSerializer != null
                    && string.Equals(s_ResolvedTypeName, configured, StringComparison.Ordinal))
                {
                    return s_DefaultSerializer;
                }

                return ResolveDefaultSerializer(configured);
            }
        }

        /// <summary>默认序列化后端标识（容器逐块落盘的 2 字节 ID）。</summary>
        public static ushort DefaultBackend => DefaultSerializer.BackendId;

        /// <summary>
        /// 按配置名解析默认序列化器，并确保其 ID 在注册表里有主。
        /// </summary>
        /// <param name="configured">配置里的实现类全名（可空，空即内置 JSON）。</param>
        /// <returns>默认序列化器实例。</returns>
        private static ISaveSerializer ResolveDefaultSerializer(string configured)
        {
            ISaveSerializer serializer = ReflectionUtility.ResolveImplType(
                ref s_DefaultSerializer, configured, typeof(JsonSaveSerializer));
            s_ResolvedTypeName = configured;

            if (!SaveSerializerRegistry.TryGet(serializer.BackendId, out ISaveSerializer registered))
            {
                SaveSerializerRegistry.Register(serializer);
            }
            else if (registered.GetType() != serializer.GetType())
            {
                LogUtility.Warning("[SaveService] Default backend '{0}' collides with implementation '{1}' on id {2};" +
                                   " the registered implementation serves the blocks.",
                    serializer.GetType().Name, registered.GetType().Name, serializer.BackendId);
            }

            return serializer;
        }

        /// <summary>配置自检：默认序列化后端的类型名能否解析成 <see cref="ISaveSerializer"/> 实现。</summary>
        /// <remarks>
        /// 实例成员，不经 <see cref="FrameworkSettings{T}.Instance"/>：构建期校验器缺资产时只读不新建。 <br />
        /// 留空算通过——空即出厂的内置 JSON，不是配错。
        /// </remarks>
        /// <param name="configuredName">不通过时回送配置上去的名字。</param>
        /// <returns>可解析或留空返回 <c>true</c>。</returns>
        internal bool DefaultBackendResolves(out string configuredName)
        {
            configuredName = m_DefaultSerializerTypeName;
            if (string.IsNullOrWhiteSpace(configuredName))
            {
                return true;
            }

            Type resolved = AssemblyUtility.GetType(configuredName);
            return resolved != null && !resolved.IsAbstract && typeof(ISaveSerializer).IsAssignableFrom(resolved);
        }
        
        [SerializeField] private string m_SaveFileExtension = ".sav";
        /// <summary>存档文件扩展名（保存时取 fileName 去扩展名部分后重新追加）。</summary>
        public static string SaveFileExtension => Instance.m_SaveFileExtension;
        
        [Tooltip("资产引用目录：无代码保存的资产引用字段（Texture/SO/Material 等）按目录双向解析定位串——被引用资产须登记入册，空 = 资产引用字段捕获恒写 Null。")]
        [SerializeField] internal SaveAssetCatalog m_AssetCatalog;
        /// <summary>资产引用目录（由 Inspector 序列化配置；<c>null</c> = 无资产引用解析能力）。</summary>
        public static SaveAssetCatalog AssetCatalog => Instance.m_AssetCatalog;
        
        [Tooltip("预制体注册表：可持久化动态实体的预制体登记（稳定键 → ResourceService 定位串）。空 = InstantiatePersistent 不可用、实体生成记录恢复时按未登记键跳过。")]
        [SerializeField] private SavePrefabRegistry m_PrefabRegistry;
        /// <summary>预制体注册表（由 Inspector 序列化配置；<c>null</c> = 动态实体持久化不可用）。</summary>
        public static SavePrefabRegistry PrefabRegistry => Instance.m_PrefabRegistry;
        
        [Tooltip("保存时截图：块保存（SaveBlockAsync）与组件保存（SaveComponentsAsync）成功后自动捕获屏幕截图并镜像到存档 sidecar 与元数据（仅运行态主线程生效）。")]
        [SerializeField] private bool m_CaptureScreenshotOnSave;
        /// <summary>保存时截图开关（默认关；开时块保存/组件保存成功后自动捕获截图并镜像 sidecar 与元数据）。</summary>
        public static bool CaptureScreenshotOnSave => Instance.m_CaptureScreenshotOnSave;
        
        [Tooltip("截图缩略图最长边（像素，保纵横比不放大）。")]
        [MinValue(16)]
        [SerializeField] private int m_ScreenshotMaxDimension = SaveScreenshotUtility.DEFAULT_MAX_DIMENSION;
        /// <summary>截图缩略图最长边（像素）。</summary>
        public static int ScreenshotMaxDimension => Instance.m_ScreenshotMaxDimension;
    }
}