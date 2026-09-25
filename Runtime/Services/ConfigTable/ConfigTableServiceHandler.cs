using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表处理器抽象基类（策略模式抽象策略）。定义 <see cref="ConfigTableService"/> 外观调用的配置表后端契约。
    /// <para>游戏侧的配置表生成代码继承本类，并通过 <c>ConfigTableService.Handler = new XxxConfigTableServiceHandler()</c> 安装。</para>
    /// <para>未安装自定义处理器时使用默认实现 <see cref="DefaultConfigTableHandler"/>（记录错误并返回空结果）。</para>
    /// </summary>
    [Serializable]
    public abstract class ConfigTableServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        public abstract Dictionary<string, List<string>> GetAllLocalizedStrings();

        /// <summary>
        /// 自报本表提供哪些语言，返回语言 Name 或 Code。
        /// </summary>
        /// <remarks>
        /// 顺序必须与 <see cref="GetAllLocalizedStrings"/> 里每条文本的列顺序一致——本地化侧据此校验列数、
        /// 并按此定位各语言列。<b>语言必须随表自报</b>：返回空时本地化侧以「数据未就绪」整批拒载并保持重试，
        /// 不存在可回落的全局注册表（双真相源已删）。认不出的 Code 会按自定义语言直通，列序不被重排。
        /// </remarks>
        public abstract IReadOnlyList<string> GetLocalizationLanguageCodes();

        /// <summary>
        /// 本表能否按语言单独取一列（<b>可选契约</b>）。
        /// </summary>
        /// <remarks>
        /// 默认 <c>false</c>：本地化侧走 <see cref="GetAllLocalizedStrings"/> 整批加载，全部语言列常驻内存。
        /// <para>词条按语言分份存储的表覆写为 <c>true</c> 并实现 <see cref="GetLocalizedStringsByLanguage"/>：
        /// 本地化侧改用「语言头 + 按语言列」的稀疏存储：装载与取值都只看当前语言列，缺译直接露 key。
        /// 语言头即 <see cref="GetLocalizationLanguageCodes"/> 的自报结果——两处必须是同一个顺序，
        /// 否则语言列会错位。</para>
        /// <para>开启本模式后 <see cref="GetAllLocalizedStrings"/> 不再被运行期调用，但仍被编辑器预览调用，
        /// 因此仍须给出可用的整批结果。</para>
        /// </remarks>
        public virtual bool SupportsPerLanguageLocalizationLoad => false;

        /// <summary>
        /// 取单一语言的词条列：key → 译文（<b>按语言取列时必须实现</b>）。
        /// </summary>
        /// <param name="languageCode"><see cref="GetLocalizationLanguageCodes"/> 自报过的语言码。</param>
        /// <returns>取不到时返回 <c>null</c>（视为数据未就绪，下次访问重试）；空字典是「已加载的空列」，不再重试。
        /// <para>缺译写成空字符串而不要省掉键：列按键对齐，省键会让后续语言整体错位。</para></returns>
        public virtual Dictionary<string, string> GetLocalizedStringsByLanguage(string languageCode) => null;

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public abstract UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken);

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        public abstract string GetUIWindowLocation(string id);

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器预览专用：取一份多语言文本，<b>不得依赖资源系统与播放态</b>。
        /// </summary>
        /// <remarks>
        /// <para>默认沿用 <see cref="GetAllLocalizedStrings"/>——生成侧的读表器在 <c>!Application.isPlaying</c>
        /// 时本就走 <c>AssetDatabase</c>/磁盘直读，所以存量项目零改也能预览。</para>
        /// <para>若某项目的表只在资源系统里（YooAsset 包内、离线模式不可用），就覆写本方法直接读磁盘上的表文件：
        /// 预览只要求「同样那份表数据」，不要求同一条加载路径，也不为此引入第二份 JSON 中间源——
        /// 中间源迟早与真表漂移，届时编辑器里看到的"对"就不再等于运行期的"对"。</para>
        /// </remarks>
        public virtual Dictionary<string, List<string>> GetLocalizedStringsForEditorPreview()
            => GetAllLocalizedStrings();
#endif
    }
}
