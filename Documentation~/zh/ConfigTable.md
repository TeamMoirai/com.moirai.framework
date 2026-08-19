# ConfigTable 服务

> Luban 配置表桥接层：框架侧定义接口与单例入口，具体配置代码由 Luban 从 Excel 生成到项目程序集。

`ConfigTable` 服务在 Runtime 侧只包含 `IConfigTableService` 接口与 `ConfigMgr` 单例：前者约定框架关心的三个能力（多语言文本、图标加载、UI 窗口路径），后者作为全局访问点持有实现。真正的 `ConfigTableService` 实现、`Tables` 表集合与各表结构均由 Luban 转表工具从模板生成到项目的 `GameProto` 程序集，并通过 `RuntimeInitializeOnLoadMethod` 自动注册到 `ConfigMgr.Instance.ConfigTableService`。配套的编辑器工具（`Tools/Settings/ConfigTableSettings` 与 `Tools/Config` 菜单）负责配置表工程目录的生成、重定向与转表。

## 核心特性

- 框架与配表解耦：框架仅依赖 `IConfigTableService` 接口，Luban 生成代码落在业务程序集，移除配表不影响框架其他服务编译
- 懒加载 `Tables`：首次访问 `ConfigTableService.Tables` 时才加载，按生成代码的 Loader 返回类型自动选择二进制（`ByteBuf`）或 JSON（`JSONNode`）格式
- 编辑器友好：非运行模式下配置 `TextAsset` 直接经 `AssetDatabase` 加载，无需启动资源系统
- 多语言桥接：反射读取生成代码中的 `LocalizedBean` 字段注册可用语言，并将 `TbLocalizedStrings` 展开为 `Dictionary<string, List<string>>` 供 [Localization](Localization.md) 服务使用
- 图标与 UI 配置读取：`TbSprite` / `TbSpriteAtlas` / `TbUIWindow` 表驱动 Sprite 加载与窗口资源定位
- 编辑器工作流：一键复制内置 Config 模板（含 Luban 可执行文件、示例表、生成模板）、转表脚本调用、导出路径同步

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `Moirai.Atropos.ConfigTable.IConfigTableService` | 配置表辅助器接口：`GetAllLocalizedStrings`、`LoadSpriteByID`、`GetUIWindowLocation` |
| `Moirai.Atropos.ConfigTable.ConfigMgr` | `Singleton<ConfigMgr>` 全局入口，持有 `ConfigTableService` 实例并转发调用 |
| `GameProto.Config.ConfigTableService` | Luban 生成（模板 `ConfigTableService.cs` + `ConfigTableService_Init.cs`），实现 `IConfigTableService`，含懒加载 `Tables` 与 `Instance` 单例 |
| `GameProto.Config.Tables` | Luban 生成的表集合（如 `TbLocalizedStrings`、`TbUIWindow`、`TbSprite`、`TbSpriteAtlas` 及业务表） |
| `Moirai.Atropos.ConfigTable.Editor.ConfigTableSettings` | 编辑器设置（`FrameworkSettings`）：配置表根目录、数据/代码导出路径 |
| `Moirai.Atropos.ConfigTable.Editor.LubanTools` | 转表菜单：`Tools/Config/Luban 转表 &X`、`Tools/Config/打开表格目录` |

## 快速上手

```csharp
// 业务代码：直接访问生成的 Tables（懒加载，首次访问自动读表）
Tables tables = ConfigTableService.Instance.Tables;

// 读取 UI 窗口配置表（表示例来自内置模板）
if (tables.TbUIWindow.DataMap.TryGetValue("MainWindow", out var uiConfig))
{
    string prefabPath = uiConfig.DefaultRes;
}

// 框架层 API（经 ConfigMgr 单例转发，无需引用 GameProto 程序集）
// 1. 获取所有多语言文本（Localization 服务启动时调用）
Dictionary<string, List<string>> localized = ConfigMgr.Instance.GetAllLocalizedStrings();

// 2. 按 ID 异步加载图集 Sprite（TbSprite + TbSpriteAtlas 联查）
Sprite icon = await ConfigMgr.Instance.LoadSpriteByID("icon_hero", cancellationToken: this.GetCancellationTokenOnDestroy());

// 3. 获取 UI 窗口资源路径
string location = ConfigMgr.Instance.GetUIWindowLocation("MainWindow");
```

## 配置与工作流

### 初始生成

1. 打开菜单 `Tools/Settings/ConfigTableSettings`（配置表设置）
2. 点击「生成 Config 到指定目录」：将包内 `Templates~/Config`（含 `Excels` 示例表、`Luban` 可执行文件、`CustomTemplate` 生成模板、`Defines`）复制到所选目录；目录名不含 "Config" 时自动创建 Config 子目录，位于 Assets 内时自动追加 `~` 后缀避免 Unity 导入
3. 初次使用需先执行 build-luban 编译最新版 Luban，或将编译好的 Luban 导入配置目录的 `[Luban]` 文件夹

### 日常转表

- 菜单 `Tools/Config/Luban 转表`（菜单项标记快捷键 `Shift+X`）执行配置目录下的 `gen_code_bin_to_project.bat`（OSX/Linux 为 `.sh`），生成数据到 `ClientDataOutPutPath`（默认 `Assets/AssetRaw/Default/Config/Table`）、代码到 `ClientCodeOutPutPath`（默认 `Assets/Scripts/GameProto`）
- 菜单 `Tools/Config/打开表格目录` 直接打开配置工程
- 移动配置表目录后，在设置界面使用「重定向 Config 目录」重新指定；修改导出路径后点击「更新配置路径」，自动同步 `path_export.conf` 各键与 `CustomTemplate/ConfigTableService_Init.cs` 中的 `CONFIG_PATH` 常量

### 生成产物

| 产物 | 说明 |
|------|------|
| `Gen/` 下的表代码 | 各表 Bean 与 `Tables` 集合 |
| `ConfigTableService.cs` | 接口实现：注册、多语言解析、Sprite/UI 查询 |
| `ConfigTableService_Init.cs` | 加载器：懒加载 `Tables`，桥接 YooAsset 资源系统 |
| `ExternalTypeUtil.cs` | Luban 扩展类型工具 |

## 注意事项

- `ConfigTableService` 与 `ConfigTableService_Init` 均为转表生成代码，手动修改会在下次转表时被覆盖；定制逻辑应写在业务侧或修改 `CustomTemplate` 模板
- 配置数据按 PRELOAD 预加载标签打包，运行时 `LoadTextAsset` 走同步 `GameApp.Resource.LoadAsset<TextAsset>`，需确保资源系统已就绪
- 未生成 Config 时 `ConfigMgr.Instance.GetAllLocalizedStrings()` 返回 `null` 并报错 "Generate Config first!"，[Localization](Localization.md) 服务会因此加载失败
- 修改 `m_ClientDataOutPutPath` / `m_ClientCodeOutPutPath` 后必须手动执行「更新配置路径」，否则 `path_export.conf` 仍指向旧目录
- 配置根目录位于 Assets 内时会自动加 `~` 后缀（如 `Assets/Config~`），Unity 不会导入该目录，转表脚本仍可正常访问

---
[« 返回主 README](../../README.md) · [Localization](Localization.md)
