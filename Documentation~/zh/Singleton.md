# Singleton 单例系统

> 线程安全的单例基类家族：纯 C# 单例（volatile 双检锁）、MonoBehaviour 单例（场景查找 + 主线程物化）与注册式单例。

命名空间：`Moirai.Atropos`（`Runtime/Core/Singleton/`）

## 类型总览

| 类型 | 目标 | 线程安全 | 生命周期回调 | 适用场景 |
|------|------|----------|--------------|----------|
| `Singleton<T>` | 纯 C# 类 | ✅ 全程 | `OnInit()` / `OnShutdown()` | 无 Unity 依赖的全局对象 |
| `SingletonMono<T>` | MonoBehaviour | ✅（物化后） | `OnInit()` / `OnShutdown()` | 场景组件型管理器 |
| `SingletonMono_Persistent<T>` | MonoBehaviour | ✅（物化后） | 同上 + 强制 `DontDestroyOnLoad` | 跨场景全局脚本 |
| `SingletonRegister<T>` | 任意 `new()` 类型 | ✅ 全程 | 无 | 无法改继承关系的既有类型 |
| `SingletonRegisterMono<T>` | 任意 MonoBehaviour | ✅（物化后） | 无 | 免继承的 Mono 单例 |
| `ReferencedScriptableObject<T>` | ScriptableObject | —（主线程） | `OnReferenced()` / `OnDisposed()` | 弱引用登记所有存活实例 |

## Singleton\<T\> — 纯 C# 单例

```csharp
public class UIConfigManager : Singleton<UIConfigManager>
{
    public int CurrentThemeIndex { get; set; }

    protected override void OnInit() { /* 首次 Instance 访问后回调一次 */ }
    protected override void OnShutdown() { /* Dispose 前回调一次 */ }
}

// 任意线程：
UIConfigManager.Instance.CurrentThemeIndex = 2;
if (UIConfigManager.IsValid) { /* ... */ }
UIConfigManager.Instance.Dispose(); // 释放，下次访问重新创建并初始化
```

### 线程模型

- **快速路径**：实例已物化时仅一次 volatile 读，无锁、无分配，后台线程可安全访问。
- **惰性创建**：volatile 读 + 双检锁（Double-Checked Locking），并发首次访问只会创建一个实例并初始化一次。
- **`new()` 约束**：约束要求公共构造函数；编辑器环境下直接 `new` 派生类会立即触发 `LogUtility.Error` 告警（守卫仅编辑器生效，运行时零开销）。

### 初始化契约（先发布后初始化）

实例构造后**先写入静态字段、再执行 `OnInit()`**：

- `OnInit()` 内同线程递归访问 `Instance` 取回**正在初始化中的同一实例**（不会死锁、不会重复创建）；
- 跨线程首次访问阻塞至 `OnInit()` 完成后返回。

`OnInit()` / `OnShutdown()` 均在锁内执行，应保持轻量。

### 释放契约

`Dispose()`（实现 `IDisposable`）幂等且带陈旧实例守卫：

- 仅当前活动实例（`s_Instance == this`）能触发 `OnShutdown()` 并清空静态引用；
- 对已释放/已被替换的**陈旧实例**调用为 no-op，**不会误杀当前活动实例**；
- 释放后再次访问 `Instance` 会创建新实例并重新初始化；
- `OnShutdown()` 执行期间访问 `Instance` 仍取回正在关闭中的实例（不创建替身）。

## SingletonMono\<T\> — MonoBehaviour 单例

```csharp
public class AudioManager : SingletonMono<AudioManager>
{
    protected override void OnInit() { /* 胜出实例的 Awake 中回调 */ }
    protected override void OnShutdown() { /* 实例销毁前回调 */ }
}

// 主线程：
AudioManager.Instance.PlayBgm("main");
if (AudioManager.IsValid) { /* ... */ }
AudioManager.TryGetInstance()?.PlayBgm("main"); // 无实例时安全返回 null
```

### 物化与线程模型

| 阶段 | 行为 |
|------|------|
| 播放模式 · 主线程 · 无实例 | 查找场景已有实例 → 未找到则创建 `[TypeName]_AutoCreated` GameObject 并挂载 |
| 播放模式 · 后台线程 · 已物化 | volatile 读原子快速路径（无 Unity API 调用） |
| 播放模式 · 后台线程 · 未物化 | 抛出 `GameException`（fail-fast）——后台线程须在主线程预热后方可访问 |
| 编辑模式 · 主线程 | **只查找不创建**（避免向场景写入瞬时对象），未找到返回 null |
| 退出窗口（应用退出/停止播放） | `Instance` 返回 null 且拒绝重新创建，`IsValid` / `TryGetInstance()` 同步反映 |

需要后台线程访问的派生类应在启动阶段于主线程预热一次（参照 `MainThreadDispatcher.BootstrapOnPlay` 的 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 模式）。

### 多实例策略

| Inspector 选项 | 行为 |
|----------------|------|
| `m_Persistent` | 胜出后 `DontDestroyOnLoad`，跨场景存活 |
| `m_Replaceable` | 最新创建的实例胜出、销毁旧实例（eg：背景音乐）；默认先到先得，销毁后到者 |

派生类扩展初始化/关闭逻辑请覆写 `OnInit()` / `OnShutdown()`；`Awake()` / `OnDestroy()` 为非虚生命周期骨架，不可覆写。

### 退出标记与域重载

`OnDestroy` 在应用退出期间保持退出标记为 true，阻止退出期复活；播放中的常规销毁（场景切换）则复位标记，允许下次访问重新物化。关闭 Domain Reload 的编辑器工作流中，静态状态跨会话存活——需要跨会话重置的派生类应参照 `MainThreadDispatcher.ResetStatics()` 提供自己的 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 钩子。

## SingletonRegister / SingletonRegisterMono — 注册式单例

```csharp
// 无需继承：任意类型直接注册
SingletonRegister<LegacyConfig>.Instance.Load();

// 免继承的 Mono 单例（无场景查找/多实例消解/生命周期回调）
SingletonRegisterMono<FxPlayer>.Instance.Play("explosion");
```

`SingletonRegisterMono<T>` 的物化同样仅限主线程（越线程抛 `GameException`）。需要查找、多实例消解或生命周期回调时请改用 `SingletonMono<T>`。

---
[« 返回主 README](../../README.md) · [Core](Core.md) · [UpdateDriver](UpdateDriver.md)
