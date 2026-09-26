# 创建新服务

在 Moirai Framework 中新建一个服务：静态外观（Facade）+ 处理器契约（Handler）+ 配置资产（Settings）。

## 参数
- $SERVICE_NAME: 服务名（如 `Quest`，生成 `QuestService`）

## 落地位置

```
Runtime/Services/<Module>/
├── <Module>Service.cs              # 静态外观，派生 ServiceBase
├── <Module>ServiceHandler.cs       # 抽象契约基类（可插拔后端的边界）
├── <Module>ServiceSettings.cs      # 配置资产，派生 FrameworkSettings<T>
└── Handler/
    └── Default<Module>Handler.cs   # 内置默认实现
```

命名与生命周期约定以 `CLAUDE.md` 的「编码规范 / 命名规范」为准，本文件不复述第二套。

## 骨架

```csharp
namespace Moirai.Atropos.<Module>
{
    [AutoRegisterService]
    [ServiceDependency(typeof(ResourceService))]          // 只在真正依赖时声明
    [HandlerHost(typeof(<Module>ServiceHandler))]
    public partial class <Module>Service : ServiceBase
    {
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>settings 未配置时的代码兜底。</summary>
        internal static <Module>ServiceHandler CreateDefaultHandler() => new Default<Module>Handler();

        public override void OnInit() => _ = Handler;

        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>读成员容忍未就绪，写成员走 RequireHandler() 做 fail-fast。</summary>
        public static Result DoSomething(Input arg) => s_Handler?.DoSomethingInternal(arg);
    }
}
```

Settings 侧的 handler 槽位用 `[SerializeReference]` 持有契约基类，配 `[FrameworkSetting("...")]` 让它在框架设置面板里可选；默认值直接取 `CreateDefaultHandler()`。

## 要点

- `Handler` 属性与 `s_Handler` 字段由 `SourceGenerators/` 里的 `HandlerHostGenerator` 生成，**不要手写**。换处理器只走生成出来的 `Internal_PeekHandler()` / `Internal_UseHandler(next)`，测试也不反射私有字段。
- 抽象接缝上只放真正随后端变化的成员：`internal abstract` 会把后端实现权往程序集内收一格，新增要有理由（`ResourceSeamShapeGuardTests` 那类基线用例就是为此存在）。
- 每帧驱动实现 `IServiceTickable`：`Tick(float elapseSeconds, float realElapseSeconds)`。
- 服务必须派生 `ServiceBase` 或 `ServiceMono<TScope>`，裸实现 `IService` 会被注册守卫拦下。
- 发起即忘的异步不要 `Forget()` 掉异常——要么把失败原因落到日志，要么让调用方拿到可await的结果。
- 动到公开面时同步 `Documentation~/zh|en/<Module>.md` 与 `CHANGELOG.md`，口径见 `CLAUDE.md` 的「提交时的文档与 CHANGELOG」。

请告诉我要创建什么服务，我按上面的形状生成三件套与对应的默认处理器。
