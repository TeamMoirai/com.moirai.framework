---
name: change:optimize
description: 热路径与分配优化：先建测量环路，同轮 A/B，只采纳显著收益
when_to_use: 有可测的每帧分配、GC、CPU 热点时
argument-hint: <目标链路> [关注点 cpu|gc|allocation]
arguments: [target, focus]
allowed-tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Bash(git log:*), Edit, Write
---

## 判据

- 先量再改：没有基线读数不动手；改动后同轮 A/B，跨轮时间差不算数。
- 只改性能敏感面：日志写入、序列化读写、资源/音频桥接。
- 行为不变：时序、可见效果、Inspector 语义、序列化键不变。
- 不铺性能标注：只在有同轮显著收益或真实扫描违例时加。
- 收尾过编译门；插件测试按装配存在性：只有 6 个插件有测试装配，其余无。

## 流程

1. 定义指标：分配字节/帧、GC 次数、单帧耗时；选一个稳定复现的环路。
2. 取基线：记录场景、帧数、采样窗口、Unity 版本、代码 revision。
3. 读实现体找分配/热点：LINQ、闭包、每帧 `new`、拼串、`GetComponent`、装箱、`params`、未池化事件。
4. 最小改动；一次只改一类，便于归因。
5. 同轮 A/B；记录增益与噪声。
6. 回归：编译门 + 测试/人工点验；确认行为没变。
7. 无效就撤：收益不显著时回退。

## 输出

- 指标与基线（含采样窗口）。
- 改动点（文件:行号）与预期作用。
- 同轮 A/B 读数与增益；噪声说明。
- 回退项与未采纳候选。
- 未取证：设备侧 IL2CPP/真机读数。
