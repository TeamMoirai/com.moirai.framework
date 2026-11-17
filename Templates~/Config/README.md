# Config 转表说明

使用 Luban 作为配置解决方案（https://www.datable.cn/docs/intro）。

请先安装 *git*，再执行 `build-luban` 编译（会把 Luban 主程序与多语言插件一并产出并登记进
`Luban.deps.json`），或自行导入编译后的工程文件到当前目录的 *Luban* 文件夹下（需自建 Luban 文件夹）。

## 常用命令（Windows 用 gen.bat，macOS 直接跑 gen.sh，逻辑同源）

    ./gen.sh                  客户端导出（bin 路线 + 懒加载，缺省值取 config.ini）
    ./gen.sh server           服务端导出（不含多语言）
    ./gen.sh all              客户端 + 服务端
    ./gen.sh client --format=json   当次改用 json 路线
    ./gen.sh client --load=eager    当次退回内置模板（构造期加载全部表）
    ./gen.sh client --check   校验模式：真实产物零写入，插件把暂存产物与已提交产物
                              逐字节比对；CI 里用它防止「改了表忘了重新导出」

## 导出管线

客户端导出由 `Extensions/Moirai.Luban.Localization` 插件基于 Luban 公开 API 实现
（`moirai-l10n` 管线），单进程完成：

1. 校验趟：合并 schema（luban_validate.conf）只加载 + 校验，含词条 key 的 ref 校验
   与资源路径校验，任何失败以非 0 退出。
2. 常规表趟：语言无关的代码与数据。
3. 多语言代码趟：bean 只剩一个变体字段，代码与语言无关。
4. 各语言数据趟：每语言一份，落 `DATA_OUTPUT_PATH_CLIENT` 下的语言子目录。
5. 语言常量 `L10nLanguages.cs` 与生成码头部空行剥离（均在暂存区内完成）。

所有产物先进 `Temp/moirai_stage` 暂存区，最后统一字节级提交（只写变更、删过期文件及
.meta、清理过期语言目录与空目录）——不再有「某趟 saver 顺手清掉另一趟产物」的趟序陷阱。
Luban 源码零修改；插件随 build-luban 构建注入（LubanHost.targets），编译产物登记进
Luban.deps.json。

服务端导出没有多语言，仍是单趟常规导出。

## 普通表引用词条 key

普通表字段需要多语言文本时，把类型声明为词条 key 的引用，导出期自动校验：

    ##type  string#ref=L10n.TbLocalizedStrings

引用了词库中不存在的 key 会在第 1 趟直接失败，指出所在记录与文件。
多张词库表可用逗号并列：`string#ref=L10n.TbLocalizedStrings,L10n.TbLocalizedAudio`。
