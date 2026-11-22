# 创建新服务

在 Moirai Framework 中新建一个服务：静态外观（Facade）+ 处理器契约（Handler）+ 配置资产（Settings）。

## 参数
- $SERVICE_NAME: 服务名（如 `Quest`，生成 `QuestService`）

## 落地位置

```
Runtime/Services/<Module>/
├── <Module>Service.cs              # 静态门面（[ProviderHandlerHost] 宿主），模块根平铺
├── I<Module>Service.cs             # 后端契约接口（可插拔后端的边界，也是服务互调的注入契约）
├── <Module>ServiceSettings.cs      # 配置资产，派生 FrameworkSettings<T>
├── <Module>ServiceDebuggerWindow.cs # 可选：调试面板，确有面板才建
├── Handler/
│   └── Default<Module>Handler.cs   # 内置默认后端（ServiceBase 派生；单支默认后端不建 <Backend>/ 子目录）
├── Models/                         # 数据面与类型面：Entry/Lease/Request/Options/枚举/事件/Attribute
├── Objects/                        # 服务管理的对象、项目侧要继承的基类
├── Internal/                       # 模块私有机制：注册表/缓存/调度/编解码/状态机
└── Support/                        # 挂 GameObject 或场景的运行时配套件
```

- 模块顶层只允许上面的封闭角色档，新文件按 `AGENTS.md`「目录与文件」的 R1–R8 归位；多后端才开 `Handler/<Backend>/`。
- 家族基座 `<Module>ServiceHandler.cs` **只在确有共享实现或被插件继承时才建**，可插拔边界由 `I<Module>Service.cs` 承担。

命名与生命周期约定以 `AGENTS.md` 的「编码规范 / 命名规范」为准，本文件不复述第二套。

## 骨架

四件各一份，形状对齐 `Runtime/Services/Timer/`（契约接口 + 静态门面 + 容器后端 + 设置资产）。

```csharp
// I<Module>Service.cs —— 后端契约：只放真正随后端变化的能力
namespace Moirai.Atropos.<Module>
{
    /// <summary>
    /// <Module> 服务契约：后端实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    public interface I<Module>Service
    {
        /// <summary>
        /// 契约成员示例。
        /// </summary>
        Result DoSomething(Input arg);
    }
}
```

```csharp
// <Module>Service.cs —— 静态门面：[ProviderHandlerHost] 的宿主，缓存槽 + 高层封装
namespace Moirai.Atropos.<Module>
{
    /// <summary>
    /// <Module> 服务外观（Facade）：全框架统一的静态访问入口。
    /// </summary>
    /// <remarks>
    /// 生成的 <c>s_Handler</c> 槽 ≡ 容器登记实例（绑定即注册）；读成员容忍未就绪，写成员走 <c>RequireHandler()</c> fail-fast。
    /// </remarks>
    [ProviderHandlerHost(typeof(I<Module>Service), typeof(Default<Module>Handler))]
    [UnityEngine.Scripting.Preserve]
    public static partial class <Module>Service
    {
        /// <summary>
        /// 从本模块的 Settings 资产解析后端。
        /// </summary>
        /// <remarks>由生成的懒装配链调用（预热在入图期、懒加载在世界就绪后）；返回 null 时回退声明默认工厂。</remarks>
        /// <returns>settings 中配置的后端；未配置时为 <c>null</c>。</returns>
        private static I<Module>Service GetHandlerFromSettings() => <Module>ServiceSettings.<Module>ServiceHandler;

        /// <summary>静态 API：转发到就绪后端，未就绪返回安全默认值。</summary>
        public static Result DoSomething(Input arg) => s_Handler?.DoSomething(arg) ?? default;
    }
}
```

```csharp
// Handler/Default<Module>Handler.cs —— 默认后端：注册实体，生命周期归容器
namespace Moirai.Atropos.<Module>
{
    /// <summary>
    /// <Module> 服务默认后端。
    /// </summary>
    [Serializable]
    [UnityEngine.Scripting.Preserve]
    [ServiceDependency(typeof(IAssetService))]          // 只在真正依赖时声明，形参是契约接口
    [ProviderDisplay(title: "默认 <Module> 后端")]
    internal sealed class Default<Module>Handler : ServiceBase, I<Module>Service
    {
        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <inheritdoc />
        protected override void OnInit()
        {
            // 走到此处时依赖已按声明图初始化：Context.GetRequiredService<IAssetService>()
        }

        /// <inheritdoc />
        protected override void OnShutdown()
        {
            // 槽位失效只在真实容器拆除时发生（State 门）：内部复用 OnShutdown 做引导重置时不清槽
            if (State >= EServiceState.ShuttingDown)
            {
                <Module>Service.InvalidateHandler(this);
            }
        }

        /// <inheritdoc />
        public Result DoSomething(Input arg) => default;
    }
}
```

```csharp
// <Module>ServiceSettings.cs —— 后端选型资产（order 取模块专属档，见同类设置资产）
namespace Moirai.Atropos.<Module>
{
    [FrameworkSetting("[服务]<Module>设置", "<Module> 后端配置", 0)]
    public sealed class <Module>ServiceSettings : FrameworkSettings<<Module>ServiceSettings>
    {
        [ProviderDropdown]
        [SerializeReference] private I<Module>Service m_<Module>ServiceHandler = <Module>Service.CreateDefaultHandler();
        /// <summary><Module> 后端。</summary>
        public static I<Module>Service <Module>ServiceHandler => Instance.m_<Module>ServiceHandler;
    }
}
```

设置侧槽位用 `[SerializeReference]` 持有**契约接口**、配 `[ProviderDropdown]` 出候选，默认值取生成的 `CreateDefaultHandler()`。

## 要点

- **静态门面不派生任何基座**：只是缓存宿主 + API 面，不是注册实体，`ServiceBase` 由后端继承。
  入图来路是组合根的 `BuiltinServiceRegistration.RegisterAll` 逐宿主发射 `WarmHandler`（settings 解析 → 注册 → 回填槽位）。
- 生成成员**不要手写**（写了就是重复定义）：`s_Handler` / `Handler` / `IsValid` / `RequireHandler` /
  `CreateDefaultHandler` / `WarmHandler` / `InvalidateHandler` /
  `Internal_PeekHandler()` / `Internal_UseHandler(next)` 都由 `ProviderHandlerHostGenerator` 发出。
- `[ServiceDependency]` 声明的是**契约接口**（`typeof(IAssetService)`），不是具体门面类：static 门面既不实现 `IService`
  也不是自报的契约接口，编译期 MA2001 判红（空参数表另有 MA2002）。
- 后端钩子只有 `protected override OnInit()`/`OnShutdown()`：装配与释放由容器经 `IServiceLifecycle` 转 `LifecycleMachine` 单点驱动。
  直驱档另有其人——不进容器的工具处理器才派生 `FrameworkHandler`，入口是 `Internal_Init()`/`Internal_Shutdown()`。
- 抽象接缝上只放真正随后端变化的成员：`internal abstract` 会把后端实现权往程序集内收一格，新增要有理由。
  形状不进测试登记（不建签名快照/成员计数基线用例），破坏性变更写 `CHANGELOG.md` 迁移口径。
- 每帧驱动实现 `IServiceTickable`：`Tick(float elapseSeconds, float realElapseSeconds)`——挂在**后端**上，门面不实现。
- 可注册进容器的服务必须派生 `ServiceBase` 或 `ServiceMono<TScope>`，裸实现 `IService` 会被注册守卫拦下。
- 发起即忘的异步不要 `Forget()` 掉异常——要么把失败原因落到日志，要么让调用方拿到可 await 的结果。
- 动到公开面时同步 `Documentation~/<Module>.md` 与 `CHANGELOG.md`，口径见 `AGENTS.md` 的「提交时的文档与 CHANGELOG」。

请告诉我要创建什么服务，我按上面的形状生成三件套与对应的默认处理器。
