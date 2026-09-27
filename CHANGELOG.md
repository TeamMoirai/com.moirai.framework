# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

### Changed

### Fixed

#### 存档

- 非 Windows 平台写档不再「先删旧档再改名」：`File.Replace` 抛 `PlatformNotSupportedException` / `NotImplementedException` 后，旧写法直接删掉主档再改名到位，这两步之间崩溃或断电就是存档消失——而 Android / iOS / WebGL 上这条回退正是常态路径。现改为旧档先改名到 `xxx.sav.journal`、再把临时文件改名到位，到位失败当场抬回；进程真崩在两步之间时由 `RecoverInterruptedWrites` 在下次初始化抬回（排在孤儿临时文件清扫之前）。回滚只在 journal 仍在时动主档——主档位置上可能是并发恢复刚抬回来的旧档；抬回也失败时 journal 与原样主档一并留着，交给下次初始化按「主档非空才算已提交」裁决，原异常照常上抛。
- 上述中转日志位与项目侧 `CreateBackup` / `RestoreBackup` 的单槽 `.bak` 分开：借 `.bak` 中转会让玩家「恢复上一版」捞到一份写入中途的快照。`DeleteFile` 先清同路径 `.journal` 再删主档——反过来的话 journal 被云同步/杀软锁住就留下「主档已没、journal 尚存」，删掉的档下次开机又复活。启动期的恢复与清扫持根级串行门，与所有存档 IO 互斥（否则恢复会在两步改名中间把 journal 抬成主档）。`CloudSaveStorageBackend` 的本地镜像同步转发 `RecoverInterruptedWrites`。`SupportsAtomicRename` 的语义改准为「替换时不出现半写窗口，且中断后旧档必可恢复」，不再是「底层用过一次原子 rename」；代价是中转期间主档路径短暂缺席，此时 `Exists()` / `EnumerateFiles()` 会把该槽报成不存在。
