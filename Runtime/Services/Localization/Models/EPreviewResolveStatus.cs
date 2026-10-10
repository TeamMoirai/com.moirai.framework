namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 预览解析结果的档位：分得开「表内无此 ID」与「该语言留空」，也分得开「数据未就绪」。
    /// </summary>
    public enum EPreviewResolveStatus : byte
    {
        /// <summary>解析到译文。</summary>
        Resolved = 0,

        /// <summary>数据未就绪（服务未起或表未生成）。</summary>
        Unavailable = 1,

        /// <summary>表内无此 ID。</summary>
        MissingId = 2,

        /// <summary>表内有此 ID，但预览语言那一格留空。</summary>
        BlankCell = 3,
    }
}
