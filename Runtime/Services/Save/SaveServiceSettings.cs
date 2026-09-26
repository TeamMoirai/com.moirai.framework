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
        /// <summary>
        /// 存档处理器实例（由 Inspector 序列化配置，可替换存储管线策略）。
        /// </summary>
        public static SaveServiceHandler SaveServiceHandler => Instance.m_SaveServiceHandler;

        /// <summary>
        /// 配置自检：加密处理器的密钥是否仍是出厂占位值。
        /// <para>刻意做成实例成员、不经 <see cref="FrameworkSettings{T}.Instance"/>——构建期校验器只读地取这份资产，
        /// 缺资产时不该由一次检查替工程新建一份。</para>
        /// </summary>
        internal bool UsesPlaceholderSaveKey =>
            m_SaveServiceHandler is AESEncryptedSaveHandler handler &&
            handler.KeyProvider.UsesPlaceholderCredentials;

        [Tooltip("默认序列化后端：未显式声明后端的数据块（无 SaveDataAttribute）使用该后端。二进制后端要求项目已引入对应 NuGet 包。")]
        [SerializeField] private ESaveBackend m_DefaultBackend = ESaveBackend.Json;
        /// <summary>
        /// 默认序列化后端（未显式声明后端的数据块使用该后端）。
        /// </summary>
        public static ESaveBackend DefaultBackend => Instance.m_DefaultBackend;
        
        [SerializeField] private string m_SaveFileExtension = ".sav";
        /// <summary>
        /// 存档文件扩展名（保存时取 fileName 去扩展名部分后重新追加）。
        /// </summary>
        public static string SaveFileExtension => Instance.m_SaveFileExtension;
        
        [Tooltip("资产引用目录：无代码保存的资产引用字段（Texture/SO/Material 等）按目录双向解析定位串——被引用资产须登记入册，空 = 资产引用字段捕获恒写 Null。")]
        [SerializeField] internal SaveAssetCatalog m_AssetCatalog;
        /// <summary>
        /// 资产引用目录（由 Inspector 序列化配置；<c>null</c> = 无资产引用解析能力）。
        /// </summary>
        public static SaveAssetCatalog AssetCatalog => Instance.m_AssetCatalog;
        
        [Tooltip("预制体注册表：可持久化动态实体的预制体登记（稳定键 → ResourceService 定位串）。空 = InstantiatePersistent 不可用、实体生成记录恢复时按未登记键跳过。")]
        [SerializeField] private SavePrefabRegistry m_PrefabRegistry;
        /// <summary>
        /// 预制体注册表（由 Inspector 序列化配置；<c>null</c> = 动态实体持久化不可用）。
        /// </summary>
        public static SavePrefabRegistry PrefabRegistry => Instance.m_PrefabRegistry;
        
        [Tooltip("保存时截图：块保存（SaveBlockAsync）与组件保存（SaveComponentsAsync）成功后自动捕获屏幕截图并镜像到存档 sidecar 与元数据（仅运行态主线程生效）。")]
        [SerializeField] private bool m_CaptureScreenshotOnSave;
        /// <summary>
        /// 保存时截图开关（默认关；开时块保存/组件保存成功后自动捕获截图并镜像 sidecar 与元数据）。
        /// </summary>
        public static bool CaptureScreenshotOnSave => Instance.m_CaptureScreenshotOnSave;
        
        [Tooltip("截图缩略图最长边（像素，保纵横比不放大）。")]
        [MinValue(16)]
        [SerializeField] private int m_ScreenshotMaxDimension = SaveScreenshotUtility.DEFAULT_MAX_DIMENSION;
        /// <summary>
        /// 截图缩略图最长边（像素）。
        /// </summary>
        public static int ScreenshotMaxDimension => Instance.m_ScreenshotMaxDimension;
    }
}