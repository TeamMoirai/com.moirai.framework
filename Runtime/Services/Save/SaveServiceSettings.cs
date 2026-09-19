using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档服务设置：存档处理器（存储管线策略）、存储后端（IO 下沉目标）、压缩提供方、默认序列化后端与文件扩展名。
    /// </summary>
    [FrameworkSetting("[服务]存档设置", "存档格式与加密配置", -410)]
    public sealed class SaveServiceSettings : FrameworkSettings<SaveServiceSettings>
    {
        [Tooltip("存储管线处理器（PlainSaveHandler / AESEncryptedSaveHandler）。密钥提供方内嵌在 AES 处理器上配置，不在本设置平铺。")]
        [ProviderDropdown]
        [SerializeReference] private SaveServiceHandler m_SaveServiceHandler = SaveService.CreateDefaultHandler();
        /// <summary>
        /// 存档处理器实例（由 Inspector 序列化配置，可替换存储管线策略）。
        /// </summary>
        public static SaveServiceHandler SaveServiceHandler => Instance.m_SaveServiceHandler;
        
        [Tooltip("存储后端：存档 IO 的下沉目标（默认本地文件；云存档等自定义后端继承 SaveStorageBackend 接入）。置空时回退本地文件后端。")]
        [ProviderDropdown]
        [SerializeReference] private SaveStorageBackend m_StorageBackend = new FileSaveStorageBackend();
        /// <summary>
        /// 存储后端实例（由 Inspector 序列化配置，可替换 IO 下沉目标；未配置时为 <c>null</c>，由处理器回退本地文件后端）。
        /// </summary>
        public static SaveStorageBackend StorageBackend => Instance.m_StorageBackend;
        
        [Tooltip("压缩提供方：容器字节在加密前压缩（空 = 不压缩）。写出档的文件头记录提供方 ID；自定义提供方须注册到 SaveCompressionRegistry 才能读回旧档。")]
        [ProviderDropdown]
        [SerializeReference] private SaveCompressionProvider m_CompressionProvider;
        /// <summary>
        /// 压缩提供方实例（由 Inspector 序列化配置；<c>null</c> = 不压缩）。
        /// </summary>
        public static SaveCompressionProvider CompressionProvider => Instance.m_CompressionProvider;

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
        
        [Tooltip("迁移回写：加载触发版本迁移成功后将迁移结果惰性回写存档（默认开）。关闭时迁移仅作用于当次加载的内存数据，同文件同会话不重复迁移（经会话级缓存），但存档文件保持旧版本。")]
        [SerializeField] private bool m_MigrationWriteBack = true;
        /// <summary>
        /// 迁移回写开关（加载触发版本迁移成功后是否将迁移结果惰性回写存档；默认开）。
        /// </summary>
        public static bool MigrationWriteBack => Instance.m_MigrationWriteBack;
        
        [Tooltip("资产引用目录：无代码保存的资产引用字段（Texture/SO/Material 等）按目录双向解析定位串——被引用资产须登记入册，空 = 资产引用字段捕获恒写 Null。")]
        [SerializeField] private SaveAssetCatalog m_AssetCatalog;
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