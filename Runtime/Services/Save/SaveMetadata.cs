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
        /// 自定义扩展键值（截图文件名、关卡名、游玩时长等由游戏层自行约定）。
        /// </summary>
        public Dictionary<string, string> Custom;
    }
}
