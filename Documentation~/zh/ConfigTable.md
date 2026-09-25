# ConfigTable 服务

> Luban 配置表桥接层：框架侧定义接口与单例入口，具体配置代码由 Luban 从 Excel 生成到项目程序集。

`ConfigTable` 服务在 Runtime 侧只包含 `ConfigTableService` 静态外观（`[HandlerHost]`）与 `ConfigTableServiceHandler` 抽象契约：前者提供框架关心的四个能力（多语言文本、语言列自报、图标加载、UI 窗口路径）的静态入口，后者约定后端实现。真正的表数据与 `Tables` 表集合均由 Luban 转表工具从模板生成到项目的 `GameProto` 程序集，游戏侧的 `LubanHandler`（继承 `ConfigTableServiceHandler`）在编辑器脚本重载时经 `[DidReloadScripts]` 调用 `ConfigTableServiceSettings.InjectConfigTableHandler<LubanHandler>()` 写入设置，运行期由外观按设置懒加载安装。配套的编辑器工具（`Tools/Framework Settings` 的 `LubanSettings` 与 `Tools/Config` 菜单）负责配置表工程目录的生成、重定向与转表。

## 核心特性

- 框架与配表解耦：框架仅依赖 `ConfigTableServiceHandler` 抽象契约，Luban 生成代码落在业务程序集，移除配表不影响框架其他服务编译
- 懒加载 `Tables`：首次访问 `ConfigTableService.Tables` 时才加载，按生成代码的 Loader 返回类型自动选择二进制（`ByteBuf`）或 JSON（`JSONNode`）格式
- 编辑器友好：非运行模式下配置 `TextAsset` 直接经 `AssetDatabase` 加载，无需启动资源系统
- 多语言桥接：多语言表按语言分份导出到 `Table/<语言码>/`，bean 只剩一个变体字段，语言不再从生成代码的字段名反推；可用语言由转表期生成的 `L10nLanguages.Codes` 经 `GetLocalizationLanguageCodes()` 自报。后端可选实现 `SupportsPerLanguageLocalizationLoad` + `GetLocalizedStringsByLanguage`，[Localization](Localization.md) 服务据此只装载当前语言列与回退链列；不实现则回落 `GetAllLocalizedStrings()` 整批模式
- 图标与 UI 配置读取：`TbSprite` / `TbSpriteAtlas` / `TbUIWindow` 表驱动 Sprite 加载与窗口资源定位
- 编辑器工作流：一键复制内置 Config 模板（含 Luban 可执行文件、示例表、生成模板）、转表脚本调用、导出路径同步

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `Moirai.Atropos.ConfigTable.ConfigTableService` | 配置表静态外观（`[HandlerHost]`）：`GetAllLocalizedStrings`、`GetLocalizationLanguageCodes`、`SupportsPerLanguageLocalizationLoad`、`GetLocalizedStringsByLanguage`、`LoadSpriteByID`、`GetUIWindowLocation`；查询 API 经 `s_Handler?.` 转发（未就绪时静默降级为 null / 空集，按语言取列的开关降级为 `false`），处理器懒加载优先从 settings 取、再回退默认工厂，两者都取不到才抛异常 |
| `Moirai.Atropos.ConfigTable.ConfigTableServiceHandler` | 配置表处理器抽象基类（继承 `FrameworkHandler`），定义后端契约；未安装自定义处理器时使用 `DefaultConfigTableHandler`（记录错误并返回空结果） |
| `Moirai.GameProto.Config.LubanHandler` | 游戏侧处理器（继承 `ConfigTableServiceHandler`），编辑器脚本重载时经 `ConfigTableServiceSettings.InjectConfigTableHandler<LubanHandler>()` 安装，桥接 Luban 生成代码与框架外观 |
| `Moirai.GameProto.Config.Tables` | Luban 生成的表集合（如 `TbLocalizedStrings`、`TbUIWindow`、`TbSprite`、`TbSpriteAtlas` 及业务表） |
| `Moirai.Atropos.ConfigTable.LubanSettings` | 编辑器设置（`FrameworkSettings`，面板「[框架]Luban 配置」）：配置表根目录、数据/代码导出路径 |
| `Moirai.Atropos.ConfigTable.Editor.LubanTools` | 转表菜单：`Tools/Config/Luban 转表 &X`、`Tools/Config/打开表格目录` |

## 快速上手

```csharp
// 业务代码：直接访问生成的 Tables（懒加载，首次访问自动读表）
Tables tables = LubanHandler.Instance.Tables;

// 读取 UI 窗口配置表（表示例来自内置模板）
if (tables.TbUIWindow.DataMap.TryGetValue("MainWindow", out var uiConfig))
{
    string prefabPath = uiConfig.DefaultRes;
}

// 框架层 API（经 ConfigTableService 外观转发，无需引用 GameProto 程序集）
// 1. 获取所有多语言文本（Localization 服务启动时调用）
Dictionary<string, List<string>> localized = ConfigTableService.GetAllLocalizedStrings();

// 2. 按 ID 异步加载图集 Sprite（TbSprite + TbSpriteAtlas 联查）
Sprite icon = await ConfigTableService.LoadSpriteByID("icon_hero", cancellationToken: this.GetCancellationTokenOnDestroy());

// 3. 获取 UI 窗口资源路径
string location = ConfigTableService.GetUIWindowLocation("MainWindow");
```

## 配置与工作流

### 初始生成

1. 打开菜单 `Tools/Framework Settings`，选 `LubanSettings`（「[框架]Luban 配置」）
2. 点击「生成 Config 到指定目录」：将包内 `Templates~/Config`（含 `Excels` 示例表、`Luban` 可执行文件、`CustomTemplate` 生成模板、`Defines`）复制到所选目录；目录名不含 "Config" 时自动创建 Config 子目录，位于 Assets 内时自动追加 `~` 后缀避免 Unity 导入
3. 初次使用需先执行 build-luban 编译最新版 Luban，或将编译好的 Luban 导入配置目录的 `[Luban]` 文件夹

### 日常转表

- 菜单 `Tools/Config/Luban 转表`（菜单项标记快捷键 `Alt+X`）执行配置目录下的 `gen.bat`（OSX/Linux 为 `gen.sh`），生成数据到 `ClientDataOutPutPath`（默认 `Assets/AssetRaw/Default/Config/Table`）、代码到 `ClientCodeOutPutPath`（默认 `Assets/Scripts/GameProto`）
- 转表是**三趟串行**：常规表（语言无关，`luban.conf`）→ 多语言代码（`luban_l10n.conf`，各语言共用一份类）→ 按语言逐个导数据（同一 conf 加 `--variant default=<语言码>`，输出到 `Table/<语言码>/`）。一次进程只解析一版变体，所以有几种语言就跑几趟；顺序不能颠倒，常规趟的 bin saver 会把输出目录连同语言子目录一起清掉
- 入口只有 `gen.sh` 一份逻辑（bash 驱动），`gen.bat` 只是 Windows 启动器（找到 bash 后调 `gen.sh`）。参数：`gen.sh [client|server|all] [--format=bin|json] [--load=lazy|eager]`，无参数即客户端。服务端输出目录尚不存在时用 `all` 会把它一并建出来
- 两条生成路线由 `config.ini` 的 `DATA_FORMAT` 定缺省：`bin` = `-c cs-bin -d bin`（`ByteBuf`），`json` = `-c cs-simple-json -d json`（SimpleJSON 的 `JSONNode`）。运行期不需要改读表代码——`LubanHandler` 按生成的 `Tables` 构造器里 loader 的返回类型自动选缓冲。一次转表只出一条路线，两条路线写同一批文件名，换路线就整批重跑
- 加载类型由 `config.ini` 的 `LAZY_LOAD` 定缺省（`true`）：走 `CustomTemplate/Client_LazyLoad/<codeTarget>/tables.sbn`，`Tables` 构造期一张表都不读，首次访问某张表才装载并就地解引用。Luban **没有**任何"懒加载"开关或 xarg，这条只能靠覆盖 `tables.sbn`；`--customTemplateDir` 按 code target 分目录找模板，所以 `cs-bin` 与 `cs-simple-json` 各需一份副本，少一份就只是那一趟静默退回内置模板
- 支持的语言清单只在 `config.ini` 的 `L10N_LANGUAGES` 里写一次，`gen.sh` 由它派生变体声明 xml 与运行期常量 `L10nLanguages.cs`；新增语言还要在 `Excels/L10n/*.xlsx` 的子列头补 `<字段>@<语言码>`
- 菜单 `Tools/Config/打开表格目录` 直接打开配置工程
- 移动配置表目录后，在设置界面使用「重定向 Config 目录」重新指定；修改导出路径后点击「更新配置路径」，自动同步 `config.ini` 各键（含 `CODE_OUTPUT_PATH_L10N`、`L10N_LANG_LIST_CODE`）与 `CustomTemplate/LubanHandler_Init.cs` 中的 `CONFIG_PATH` 常量

### 生成产物

| 产物 | 说明 |
|------|------|
| `Gen/` 下的表代码 | 各表 Bean 与 `Tables` 集合；**不含多语言表**，语言无关的表按 key 存译文标识，逐语言重导没有意义 |
| `GenL10n/` 下的表代码 | 多语言表 Bean（单字段变体 bean）与各语言共用的类；必须与 `Gen/` 分根目录，否则其中一趟的代码 saver 会把另一趟的产物当多余文件删掉 |
| `L10nLanguages.cs` | 转表期生成的语言码常量，游戏侧处理器据此自报可用语言 |
| `Table/<语言码>/l10n_*.bytes` | 每种语言一份多语言数据，含全部键（缺译是空串） |
| `LubanHandler.cs` | 游戏侧处理器：实现 `ConfigTableServiceHandler` 契约（按语言取列、Sprite/UI 查询）并自动安装 |
| `ExternalTypeUtil.cs` | Luban 扩展类型工具 |

## 注意事项

- 生成代码为转表产物，手动修改会在下次转表时被覆盖；定制逻辑应写在业务侧或修改 `CustomTemplate` 模板
- 懒加载把"表读不到"的报错时机从启动期移到首次访问：缺 `.bytes`/`.json` 或资源系统未就绪时，异常在那张表第一次被取时才抛（`Config asset is missing` / `not loadable yet`），启动期不再一次性暴露所有配表问题。排查时按访问顺序看，不要按转表顺序看
- 配置数据按 PRELOAD 预加载标签打包，运行时经 `ResourceService` 加载，需确保资源系统已就绪。收集规则是递归的，`Table/<语言码>/` 同样继承 PRELOAD——按语言分份省下的是解析与词条常驻，资产字节仍在启动期全部解码；要连字节一起按语言走，需要先把这些子目录摘出 PRELOAD 并给按语言取列补异步实现
- 未安装游戏侧处理器时 `ConfigTableService.GetAllLocalizedStrings()` 由 `DefaultConfigTableHandler` 返回空结果并记录错误，[Localization](Localization.md) 服务会因此加载失败
- 修改 `m_ClientDataOutPutPath` / `m_ClientCodeOutPutPath` 后必须手动执行「更新配置路径」，否则 `config.ini` 仍指向旧目录
- 配置根目录位于 Assets 内时会自动加 `~` 后缀（如 `Assets/Config~`），Unity 不会导入该目录，转表脚本仍可正常访问

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [Localization](Localization.md)
