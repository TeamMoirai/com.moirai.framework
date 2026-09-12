using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档槽位元数据（持久化于保留块 <c>__meta</c>，JSON 后端）。
    /// <para>供存档列表 UI 展示游戏版本与自定义信息；扩展字段走 <see cref="Custom"/> 字典，避免频繁变更模式。</para>
    /// </summary>
    [Serializable]
    public sealed class SaveMetadata
    {
        /// <summary>
        /// 游戏版本号（写入时的 <c>Application.version</c> 由调用方填入）。
        /// </summary>
        public string GameVersion;

        /// <summary>
        /// 存档数据版本（迁移总线判定基准，int 递增）。
        /// <para>版本化激活（<c>SaveMigrationManager.CurrentVersion &gt; 0</c>）时由框架在写盘管线自动盖章为当前版本；
        /// 无元数据块或缺省该字段的旧档按版本 0 处理。游戏层不应手动赋值。</para>
        /// </summary>
        public int SaveVersion;

        /// <summary>
        /// 迁移历史审计（迁移总线追加：每条记录一次迁移步，格式 <c>"{起始版本}->{目标版本}|{迁移器类型名}|{UTC 时间 ISO-8601}"</c>）。
        /// <para>随迁移回写持久化；游戏层只读使用，不应手动修改。</para>
        /// </summary>
        public List<string> MigrationHistory;

        /// <summary>
        /// 自定义扩展键值（截图文件名、关卡名、游玩时长等由游戏层自行约定）。
        /// </summary>
        public Dictionary<string, string> Custom;
    }
}
