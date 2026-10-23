using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.ConfigTable;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 精灵引用门面：数据为 <see cref="AssetReference{TObject}"/>（GUID 寻址），取像统一走 <see cref="ResourceService"/>， <br />
    /// 合图子精灵按名经绑定服务挂目标元素，Config 模式经 <see cref="ConfigTableService"/> 取配置表精灵。
    /// </summary>
    /// <remarks>
    /// 普通与配置表精灵取像为保活窗口口径，不长期持租约；合图子精灵的租约随目标元素生命周期。 <br />
    /// <see cref="DataSave(Stream)"/> / <see cref="DataLoad(Stream)"/> 自带版本号，版本高于当前抛 <see cref="InvalidDataException"/>。 <br />
    /// 子图身份由引用行拖入实捕获（框架弱引用只落文件 GUID）；检视面为属性驱动，无自绘抽屉。
    /// </remarks>
    [Serializable]
    public class SpriteReference
    {
        /// <summary>
        /// 精灵打包方式
        /// </summary>
        public enum ESpritePackageType
        {
            /// <summary>普通精灵，包括散图和合图</summary>
            [LabelText("Common (默认)")]
            Common,
            /// <summary>Unity 的 SpriteAtlas 图集，配置表设置</summary>
            [LabelText("Config (配置表 ID)")]
            Config
        }

        #region 字段 [FIELDS]

        internal const int CURRENT_VERSION = 1;

        // 两行各占一半、标签定宽，避免 Odin 自动标签宽在同行成员间互相污染
        private const string REFERENCE_ROW_GROUP = "ReferenceRow";
        private const string MODE_ROW_GROUP = "ModeRow";
        private const float ROW_MEMBER_WIDTH = 0.5f;
        private const float ROW_LABEL_WIDTH = 64f;

        [Tooltip("Common：精灵资源弱引用；Config 模式不使用")]
        [HideInInspector]
        [SerializeField] private AssetReference<Sprite> m_SpriteRef = new AssetReference<Sprite>();

        [Tooltip("精灵打包方式。Common 常规散图和合图；Config SpriteAtlas 图集，配置表设置")]
        [HorizontalGroup(MODE_ROW_GROUP, ROW_MEMBER_WIDTH, LabelWidth = ROW_LABEL_WIDTH)]
        [SerializeField] private ESpritePackageType m_PackageType = ESpritePackageType.Common;

        [Tooltip("合图/多精灵图内的子精灵名，留空取整资产精灵")]
        [HideInInspector] // 只由引用行拖入实捕获，不在检视编辑
        [SerializeField] private string m_SpriteName;

        [Tooltip("Config：配置表中的 ID")]
        [HorizontalGroup(MODE_ROW_GROUP, ROW_MEMBER_WIDTH, LabelWidth = ROW_LABEL_WIDTH)]
        [ShowIf(nameof(m_PackageType), ESpritePackageType.Config)]
        [SerializeField] private string m_ConfigId;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>精灵打包方式。</summary>
        public ESpritePackageType PackageType { get => m_PackageType; internal set => m_PackageType = value; }

        /// <summary>普通模式的精灵弱引用。</summary>
        public AssetReference<Sprite> SpriteRef { get => m_SpriteRef; internal set => m_SpriteRef = value; }

        /// <summary>合图/多精灵图内的子精灵名，留空取整资产精灵。</summary>
        public string SpriteName { get => m_SpriteName; internal set => m_SpriteName = value; }

        /// <summary>Config 模式的配置表 ID。</summary>
        public string ConfigId { get => m_ConfigId; internal set => m_ConfigId = value; }

        #endregion

        #region 检视支撑 [INSPECTOR SUPPORT]

#if UNITY_EDITOR
        /// <summary>引用行句柄：显示按 GUID + 子图名解析到的精灵，拖入或选择即写回弱引用并按导入器模式捕子图名。</summary>
        [ShowInInspector, PropertyOrder(-999)]
        [LabelText("SpriteRef")]
        [HorizontalGroup(REFERENCE_ROW_GROUP, ROW_MEMBER_WIDTH, LabelWidth = ROW_LABEL_WIDTH)]
        private Sprite SpriteFieldHandle
        {
            get
            {
                string guid = m_SpriteRef != null ? m_SpriteRef.GUID : null;
                if (string.IsNullOrEmpty(guid))
                {
                    return null;
                }

                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    return null;
                }

                return string.IsNullOrEmpty(m_SpriteName)
                    ? UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path)
                    : LoadEditorSubSprite(guid, m_SpriteName);
            }
            set
            {
                string path = value != null ? UnityEditor.AssetDatabase.GetAssetPath(value) : string.Empty;
                if (m_SpriteRef == null)
                {
                    m_SpriteRef = new AssetReference<Sprite>();
                }

                m_SpriteRef.GUID = string.IsNullOrEmpty(path)
                    ? string.Empty
                    : UnityEditor.AssetDatabase.AssetPathToGUID(path);
                m_SpriteName = ResolveSubSpriteName(value, path);
            }
        }

        /// <summary>包名句柄：写进弱引用自己的 <c>PackageName</c> 槽，下拉由框架 <c>ResourcePackageDropdownAttribute</c> 的绘制器接管。</summary>
        [ShowInInspector, PropertyOrder(-998)]
        [LabelText("Package")]
        [Tooltip("资源包名称，留空使用后端配置的默认资源包")]
        [ResourcePackageDropdown(AllowEmpty = true)]
        [HorizontalGroup(REFERENCE_ROW_GROUP, ROW_MEMBER_WIDTH, LabelWidth = ROW_LABEL_WIDTH)]
        private string PackageHandle
        {
            get => m_SpriteRef != null ? m_SpriteRef.PackageName : string.Empty;
            set
            {
                if (m_SpriteRef == null)
                {
                    m_SpriteRef = new AssetReference<Sprite>();
                }

                m_SpriteRef.PackageName = value ?? string.Empty;
            }
        }
        
        /// <summary>
        /// 子精灵语义按导入器模式判别（勿按主资产身份或文件名比对）：Multiple 记实捕获的子图名、Single/Polygon 留空取整资产；非纹理源（.asset/.fbx 内精灵）回退主资产身份判定。
        /// </summary>
        internal static string ResolveSubSpriteName(Sprite sprite, string spritePath)
        {
            if (sprite == null || string.IsNullOrEmpty(spritePath))
            {
                return string.Empty;
            }

            if (UnityEditor.AssetImporter.GetAtPath(spritePath) is UnityEditor.TextureImporter importer)
            {
                return importer.spriteImportMode == UnityEditor.SpriteImportMode.Multiple ? sprite.name : string.Empty;
            }

            return UnityEditor.AssetDatabase.IsSubAsset(sprite) ? sprite.name : string.Empty;
        }

#endif

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 异步取像并赋给目标 <see cref="Image"/>；等待期间目标被销毁或取空且不允许空时静默返回。
        /// </summary>
        /// <param name="target">目标 Image。</param>
        /// <param name="allowNull">取像为空时是否允许置空目标。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async UniTaskVoid SetSprite(Image target, bool allowNull = false, CancellationToken cancellationToken = default)
        {
            if (target == null) return;

            if (m_PackageType == ESpritePackageType.Common && !string.IsNullOrEmpty(m_SpriteName))
            {
                await BindSubSpriteAsync(target, allowNull, cancellationToken);
                return;
            }

            Sprite sprite = await GetSpriteAsync(cancellationToken);
            if (target == null) return; // 等待期间目标可能已被销毁
            if (sprite == null && !allowNull) return;

            target.sprite = sprite;
        }

        /// <summary>
        /// 异步取像并赋给目标 <see cref="SpriteRenderer"/>；等待期间目标被销毁或取空且不允许空时静默返回。
        /// </summary>
        /// <param name="target">目标 SpriteRenderer。</param>
        /// <param name="allowNull">取像为空时是否允许置空目标。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async UniTaskVoid SetSprite(SpriteRenderer target, bool allowNull = false, CancellationToken cancellationToken = default)
        {
            if (target == null) return;

            if (m_PackageType == ESpritePackageType.Common && !string.IsNullOrEmpty(m_SpriteName))
            {
                await BindSubSpriteAsync(target, allowNull, cancellationToken);
                return;
            }

            Sprite sprite = await GetSpriteAsync(cancellationToken);
            if (target == null) return; // 等待期间目标可能已被销毁
            if (sprite == null && !allowNull) return;

            target.sprite = sprite;
        }

        /// <summary>
        /// 克隆此对象（引用数据独立，加载状态不共享）。
        /// </summary>
        /// <returns>等值的新实例。</returns>
        public SpriteReference Clone()
        {
            SpriteReference result = new SpriteReference();

            result.m_PackageType = m_PackageType;
            result.m_SpriteRef = m_SpriteRef != null
                ? new AssetReference<Sprite> { GUID = m_SpriteRef.GUID, PackageName = m_SpriteRef.PackageName }
                : new AssetReference<Sprite>();
            result.m_SpriteName = m_SpriteName;
            result.m_ConfigId = m_ConfigId;

            return result;
        }

        /// <summary>
        /// 从流加载数据；旧引用的租约先释放。
        /// </summary>
        /// <param name="stream">要读取的流。</param>
        /// <exception cref="InvalidDataException">流版本高于当前版本。</exception>
        public void DataLoad(Stream stream)
        {
            int version = ReadInt32(stream);
            if (version > CURRENT_VERSION) throw new InvalidDataException("Invalid version");

            m_PackageType = (ESpritePackageType)ReadInt32(stream);
            m_SpriteRef?.ReleaseAsset();
            m_SpriteRef = new AssetReference<Sprite>
            {
                GUID = ReadStringPacket(stream),
                PackageName = ReadStringPacket(stream)
            };
            m_SpriteName = ReadStringPacket(stream);
            m_ConfigId = ReadStringPacket(stream);
        }

        /// <summary>
        /// 将数据保存到流（版本 + 打包方式 + GUID/包名/子图名/配置表 ID）。
        /// </summary>
        /// <param name="stream">要写入的流。</param>
        public void DataSave(Stream stream)
        {
            WriteInt32(stream, CURRENT_VERSION);
            WriteInt32(stream, (int)m_PackageType);
            WriteStringPacket(stream, m_SpriteRef?.GUID);
            WriteStringPacket(stream, m_SpriteRef?.PackageName);
            WriteStringPacket(stream, m_SpriteName);
            WriteStringPacket(stream, m_ConfigId);
        }

        /// <summary>
        /// 全字段等值比较（字符串按 null 归一为空串）。
        /// </summary>
        /// <param name="other">比较对象。</param>
        /// <returns>全字段相等为 <c>true</c>。</returns>
        public bool Matches(SpriteReference other)
        {
            return other?.m_PackageType == m_PackageType
                && (other.m_SpriteRef?.GUID ?? string.Empty) == (m_SpriteRef?.GUID ?? string.Empty)
                && (other.m_SpriteRef?.PackageName ?? string.Empty) == (m_SpriteRef?.PackageName ?? string.Empty)
                && (other.m_SpriteName ?? string.Empty) == (m_SpriteName ?? string.Empty)
                && (other.m_ConfigId ?? string.Empty) == (m_ConfigId ?? string.Empty);
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 普通精灵与配置表精灵的取像：保活窗口口径，不长期持租约。
        /// </summary>
        private async UniTask<Sprite> GetSpriteAsync(CancellationToken cancellationToken)
        {
            if (m_PackageType == ESpritePackageType.Common)
            {
                if (string.IsNullOrEmpty(m_SpriteRef?.GUID))
                {
                    return null;
                }

                if (!m_SpriteRef.TryGetLocation(out string location))
                {
                    LogUtility.Warning("SpriteReference sprite load failed: GUID not resolved: {0}", m_SpriteRef.GUID);
                    return null;
                }

                return await ResourceService.TryLoadAssetAsync<Sprite>(location, cancellationToken, m_SpriteRef.PackageName);
            }

            if (string.IsNullOrEmpty(m_ConfigId))
            {
                return null;
            }

            return await ConfigTableService.LoadSpriteByID(m_ConfigId, cancellationToken);
        }

        /// <summary>
        /// 合图子精灵按名索取：经绑定服务挂目标元素，租约随元素生命周期。
        /// </summary>
        private async UniTask BindSubSpriteAsync(Image target, bool allowNull, CancellationToken cancellationToken)
        {
            IResourceBindingService bindingService = ResourceService.BindingService;
            if (bindingService == null)
            {
#if UNITY_EDITOR
                Sprite editorSprite = LoadEditorSubSprite(m_SpriteRef?.GUID, m_SpriteName);
                if (editorSprite != null || allowNull)
                {
                    target.sprite = editorSprite;
                }
#endif
                return;
            }

            if (!m_SpriteRef.TryGetLocation(out string location))
            {
                LogUtility.Warning("SpriteReference sub-sprite bind failed: GUID not resolved: {0}", m_SpriteRef.GUID);
                if (allowNull) target.sprite = null;
                return;
            }

            ResourceOwner owner = ResourceOwner.EnsureFor(target, bindingService);
            var key = new ResourceKey(location, m_SpriteRef.PackageName, typeof(Sprite), EResourceAssetKind.SubAssets);
            EResourceBindStatus status = await bindingService.BindSubSpriteAsync(owner, target, key, m_SpriteName,
                EResourceBindingOption.None, cancellationToken);
            if (target == null) return; // 等待期间目标可能已被销毁

            if (status != EResourceBindStatus.Success && status != EResourceBindStatus.Cancelled)
            {
                LogUtility.Warning("SpriteReference sub-sprite bind failed: {0}/{1} ({2})", location, m_SpriteName, status);
                if (allowNull)
                {
                    target.sprite = null;
                }
            }
        }

        /// <summary>
        /// 合图子精灵按名索取：经绑定服务挂目标元素，租约随元素生命周期。
        /// </summary>
        private async UniTask BindSubSpriteAsync(SpriteRenderer target, bool allowNull, CancellationToken cancellationToken)
        {
            IResourceBindingService bindingService = ResourceService.BindingService;
            if (bindingService == null)
            {
#if UNITY_EDITOR
                Sprite editorSprite = LoadEditorSubSprite(m_SpriteRef?.GUID, m_SpriteName);
                if (editorSprite != null || allowNull)
                {
                    target.sprite = editorSprite;
                }
#endif
                return;
            }

            if (!m_SpriteRef.TryGetLocation(out string location))
            {
                LogUtility.Warning("SpriteReference sub-sprite bind failed: GUID not resolved: {0}", m_SpriteRef.GUID);
                if (allowNull) target.sprite = null;
                return;
            }

            ResourceOwner owner = ResourceOwner.EnsureFor(target, bindingService);
            var key = new ResourceKey(location, m_SpriteRef.PackageName, typeof(Sprite), EResourceAssetKind.SubAssets);
            EResourceBindStatus status = await bindingService.BindSubSpriteAsync(owner, target, key, m_SpriteName,
                EResourceBindingOption.None, cancellationToken);
            if (target == null) return; // 等待期间目标可能已被销毁

            if (status != EResourceBindStatus.Success && status != EResourceBindStatus.Cancelled)
            {
                LogUtility.Warning("SpriteReference sub-sprite bind failed: {0}/{1} ({2})", location, m_SpriteName, status);
                if (allowNull)
                {
                    target.sprite = null;
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器面（预览/测试）按名直取子精灵：绑定服务未就绪时的回退，窗口与工具面共用此通道。
        /// </summary>
        /// <param name="guid">资产 GUID。</param>
        /// <param name="spriteName">子精灵名。</param>
        /// <returns>命中的子精灵；GUID 或名字未命中为 <c>null</c>。</returns>
        public static Sprite LoadEditorSubSprite(string guid, string spriteName)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            UnityEngine.Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite sprite && sprite.name == spriteName)
                {
                    return sprite;
                }
            }

            return null;
        }
#endif

        /// <summary>
        /// 读 4 字节小端整数（与玩法侧 IOExtensions 的 <c>ReadInt</c> 字节兼容）。
        /// </summary>
        private static int ReadInt32(Stream stream)
        {
            byte[] b = new byte[4];
            stream.Read(b, 0, 4);
            return BitConverter.ToInt32(b, 0);
        }

        /// <summary>
        /// 写 4 字节小端整数（与玩法侧 IOExtensions 的 <c>WriteInt</c> 字节兼容）。
        /// </summary>
        private static void WriteInt32(Stream stream, int value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 读 8 字节小端长度前缀的 UTF-8 字符串（与玩法侧 IOExtensions 的 <c>ReadStringPacket</c> 字节兼容）。
        /// </summary>
        private static string ReadStringPacket(Stream stream)
        {
            byte[] lengthBytes = new byte[8];
            stream.Read(lengthBytes, 0, 8);
            long length = BitConverter.ToInt64(lengthBytes, 0);

            byte[] b = new byte[length];
            stream.Read(b, 0, b.Length);
            return Encoding.UTF8.GetString(b);
        }

        /// <summary>
        /// 写 8 字节小端长度前缀的 UTF-8 字符串，null 归一为空串（与玩法侧 IOExtensions 的 <c>WriteStringPacket</c> 字节兼容）。
        /// </summary>
        private static void WriteStringPacket(Stream stream, string value)
        {
            byte[] b = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] lengthBytes = BitConverter.GetBytes((long)b.Length);
            stream.Write(lengthBytes, 0, lengthBytes.Length);
            stream.Write(b, 0, b.Length);
        }

        #endregion

    }
}
