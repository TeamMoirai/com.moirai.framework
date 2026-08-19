# FSM 模块

> 有限状态机模块：以泛型持有者为中心，集中创建、轮询与销毁任意数量的状态机。

FSM 模块提供完整的有限状态机实现，通过 `GameModule.FSM` 静态访问器使用。每台状态机绑定一个持有者（owner），由若干 `FSMState<T>` 状态组成，模块统一驱动所有状态机的 `Update` 轮询。状态机实例本身来自 `MemoryPool`，创建与销毁零 GC 分配。流程管理模块 Procedure 即基于本模块构建，参见 [Procedure](Procedure.md)。

## 核心特性

- 泛型状态机：`FSMState<T>` 以持有者类型 `T` 为参数，一个状态类可服务任意持有者类型
- 具名状态机：同一持有者类型可创建多台独立状态机，以 `(ownerType, name)` 区分
- 完整生命周期：`OnInit` / `OnEnter` / `OnUpdate` / `OnExit` / `OnDestroy` 五个可重写钩子
- FSM 内置数据字典：`SetData` / `GetData` / `RemoveData` 在状态间共享临时数据
- 双时间轴轮询：`OnUpdate` 同时接收逻辑时间与真实时间（暂停游戏时逻辑时间停止）
- 池化实现：`FSM<T>` 实例经 `MemoryPool` 获取与归还，减少 GC 压力

## 核心类型

命名空间：`Moirai.Atropos.FSM`

| 类/接口 | 说明 |
|---------|------|
| `IFSMModule` | 状态机管理器接口：创建/销毁/查询状态机，通过 `GameModule.FSM` 访问 |
| `FSMModule` | 默认实现（`internal sealed`），模块优先级 `Priority = 1`，实现 `IUpdateModule` 统一轮询 |
| `IFSM<T>` | 状态机接口：`Start` / `ChangeState` / `HasState` / `GetState` / `GetAllStates` / 数据字典等 |
| `FSMBase` | 状态机抽象基类：`Name` / `FullName` / `OwnerType` / `FsmStateCount` / `IsRunning` / `IsDestroyed` / `CurrentStateName` / `CurrentStateTime` |
| `FSMState<T>` | 状态抽象基类（`public abstract`），定义全部生命周期虚方法与 `protected ChangeState` |
| `FSM<T>` | 具体实现（`internal sealed`），实现 `IMemory`，由 `MemoryPool` 管理 |

## 快速上手

```csharp
using Moirai.Atropos;
using Moirai.Atropos.FSM;

// 1. 定义持有者与状态（owner 必须是 class）
public class Enemy { }

public class EnemyIdleState : FSMState<Enemy>
{
    protected internal override void OnInit(IFSM<Enemy> fsm) { }

    protected internal override void OnEnter(IFSM<Enemy> fsm) { }

    protected internal override void OnUpdate(IFSM<Enemy> fsm, float elapseSeconds, float realElapseSeconds)
    {
        // 逻辑帧轮询；elapseSeconds 受时间缩放影响，realElapseSeconds 为真实时间
    }

    protected internal override void OnExit(IFSM<Enemy> fsm, bool isShutdown) { }

    protected internal override void OnDestroy(IFSM<Enemy> fsm) { }
}

// 2. 创建状态机（CreateFSM 有 4 个重载：params 数组 / List，可省略名称）
IFSM<Enemy> fsm = GameModule.FSM.CreateFSM("enemy-1", new Enemy(),
    new EnemyIdleState(), new EnemyPatrolState());

// 3. 启动（只能启动一次，重复启动抛出 GameException）
fsm.Start<EnemyIdleState>();

// 4. 切换状态（也可以在状态内部切换，见下）
fsm.ChangeState<EnemyPatrolState>();

// 5. 销毁
GameModule.FSM.DestroyFSM(fsm);
```

## 进阶用法

在状态内部切换状态，使用 `FSMState<T>` 提供的 `protected` 方法：

```csharp
public class EnemyIdleState : FSMState<Enemy>
{
    protected internal override void OnUpdate(IFSM<Enemy> fsm, float elapseSeconds, float realElapseSeconds)
    {
        ChangeState<EnemyPatrolState>(fsm);          // 泛型版本
        // ChangeState(fsm, typeof(EnemyPatrolState)); // Type 版本
    }
}
```

具名状态机与数据共享：

```csharp
// 同一持有者类型可创建多台互不影响的状态机
IFSM<Enemy> fsmA = GameModule.FSM.CreateFSM("enemy-a", enemyA, new EnemyIdleState());
IFSM<Enemy> fsmB = GameModule.FSM.CreateFSM("enemy-b", enemyB, new EnemyIdleState());

// 之后再按名称获取
IFSM<Enemy> found = GameModule.FSM.GetFSM<Enemy>("enemy-a");
bool exists = GameModule.FSM.HasFSM<Enemy>("enemy-b");

// 状态间共享数据（name -> object）
fsm.SetData("PatrolIndex", 0);
int index = fsm.GetData<int>("PatrolIndex");
fsm.RemoveData("PatrolIndex");

// 当前状态与持续时间
FSMState<Enemy> current = fsm.CurrentState;
float staying = fsm.CurrentStateTime; // 当前状态已持续的逻辑秒数
```

## 注意事项

- 生命周期方法均为 `protected internal virtual`，签名以源码为准：`OnInit(IFSM<T>)`、`OnEnter(IFSM<T>)`、`OnUpdate(IFSM<T>, float elapseSeconds, float realElapseSeconds)`、`OnExit(IFSM<T>, bool isShutdown)`、`OnDestroy(IFSM<T>)`
- 离开状态的回调是 `OnExit`（非 `OnLeave`）；`isShutdown` 为 `true` 表示因状态机关闭而离开
- `OnInit` 在 `CreateFSM` 时对每个状态立即调用；`Start<TState>()` 之前状态机不会轮询
- 同一 `(ownerType, name)` 重复创建、或 `Start` 已运行的状态机，均抛出 `GameException`
- 切换到不存在的状态会抛出 `GameException`；`ChangeState` 要求状态机已在运行
- 集合请传入至少一个状态，否则创建时抛出 `GameException`
- 框架关闭时 `FSMModule.Shutdown` 会依次关闭全部状态机，触发各状态 `OnExit(fsm, true)` 与 `OnDestroy(fsm)`

---
[« 返回主 README](../../README.md)
