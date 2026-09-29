using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表处理器抽象基类（策略模式抽象策略），定义 <see cref="ConfigTableService"/> 外观调用的配置表后端契约。
    /// </summary>
    /// <remarks>
    /// 游戏侧的配置表生成代码继承本类，经 <c>ConfigTableService.Handler = new XxxConfigTableServiceHandler()</c> 安装。 <br />
    /// 未安装自定义处理器时使用默认实现 <see cref="DefaultConfigTableHandler"/>（记录错误并返回空结果）。
    /// </remarks>
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
        /// 顺序必须与 <see cref="GetAllLocalizedStrings"/> 里每条文本的列顺序一致，本地化侧据此校验列数与定位各语言列。
        /// <b>语言必须随表自报</b>：返回空时本地化侧以「数据未就绪」整批拒载并保持重试，不存在可回落的全局注册表。
        /// 认不出的 Code 按自定义语言直通，列序不被重排。
        /// </remarks>
        public abstract IReadOnlyList<string> GetLocalizationLanguageCodes();

        /// <summary>
        /// 本表能否按语言单独取一列（<b>可选契约</b>）。
        /// </summary>
        /// <remarks>
        /// 默认 <c>false</c>：本地化侧走 <see cref="GetAllLocalizedStrings"/> 整批加载，全部语言列常驻内存。 <br />
        /// 覆写为 <c>true</c> 并实现 <see cref="GetLocalizedStringsByLanguage"/> 后改用「语言头 + 当前语言列」稀疏存储，缺译直接露 key。 <br />
        /// 语言头即 <see cref="GetLocalizationLanguageCodes"/> 的自报结果，两处顺序必须一致，否则语言列错位。 <br />
        /// 开启后 <see cref="GetAllLocalizedStrings"/> 不再被运行期调用，但仍被编辑器预览调用，须给出可用整批结果。
        /// </remarks>
        public virtual bool SupportsPerLanguageLocalizationLoad => false;

        /// <summary>
        /// 取单一语言的词条列：key → 译文（<b>按语言取列时必须实现</b>）。
        /// </summary>
        /// <param name="languageCode"><see cref="GetLocalizationLanguageCodes"/> 自报过的语言码。</param>
        /// <returns>取不到时返回 <c>null</c>（视为数据未就绪，下次访问重试）；空字典是「已加载的空列」，不再重试。</returns>
        /// <remarks>缺译写成空字符串而不要省掉键：列按键对齐，省键会让后续语言整体错位。</remarks>
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
    }
}
