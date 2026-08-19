# FSM Module

> Finite State Machine module: Centered around a generic owner, centrally creates, updates, and destroys any number of state machines.

The FSM module provides a complete finite state machine implementation, accessible via the `GameModule.FSM` static accessor. Each state machine is bound to an owner and consists of several `FSMState<T>` states. The module uniformly drives the `Update` polling of all state machines. State machine instances themselves come from `MemoryPool`, ensuring zero GC allocation on creation and destruction. The Procedure management module is built on top of this module; see [Procedure](Procedure.md).

## Core Features

- Generic state machine: `FSMState<T>` is parameterized with the owner type `T`; one state class can serve any owner type
- Named state machines: Multiple independent state machines can be created for the same owner type, distinguished by `(ownerType, name)`
- Full lifecycle: Five overridable hooks: `OnInit` / `OnEnter` / `OnUpdate` / `OnExit` / `OnDestroy`
- Built-in data dictionary within FSM: `SetData` / `GetData` / `RemoveData` for sharing temporary data between states
- Dual timeline polling: `OnUpdate` receives both logical time and real time (logical time stops when the game is paused)
- Pooled implementation: `FSM<T>` instances are acquired from and returned to `MemoryPool`, reducing GC pressure

## Core Types

Namespace: `Moirai.Atropos.FSM`

| Class/Interface | Description |
|---------|------|
| `IFSMModule` | State machine manager interface: create/destroy/query state machines, accessed via `GameModule.FSM` |
| `FSMModule` | Default implementation (`internal sealed`), module priority `Priority = 1`, implements `IUpdateModule` for unified polling |
| `IFSM<T>` | State machine interface: `Start` / `ChangeState` / `HasState` / `GetState` / `GetAllStates` / data dictionary, etc. |
| `FSMBase` | Abstract base class for state machines: `Name` / `FullName` / `OwnerType` / `FsmStateCount` / `IsRunning` / `IsDestroyed` / `CurrentStateName` / `CurrentStateTime` |
| `FSMState<T>` | Abstract base class for states (`public abstract`), defines all lifecycle virtual methods and `protected ChangeState` |
| `FSM<T>` | Concrete implementation (`internal sealed`), implements `IMemory`, managed by `MemoryPool` |

## Quick Start

```csharp
using Moirai.Atropos;
using Moirai.Atropos.FSM;

// 1. Define the owner and states (owner must be a class)
public class Enemy { }

public class EnemyIdleState : FSMState<Enemy>
{
    protected internal override void OnInit(IFSM<Enemy> fsm) { }

    protected internal override void OnEnter(IFSM<Enemy> fsm) { }

    protected internal override void OnUpdate(IFSM<Enemy> fsm, float elapseSeconds, float realElapseSeconds)
    {
        // Logic frame update; elapseSeconds is affected by time scale, realElapseSeconds is real time
    }

    protected internal override void OnExit(IFSM<Enemy> fsm, bool isShutdown) { }

    protected internal override void OnDestroy(IFSM<Enemy> fsm) { }
}

// 2. Create a state machine (CreateFSM has 4 overloads: params array / List, name can be omitted)
IFSM<Enemy> fsm = GameModule.FSM.CreateFSM("enemy-1", new Enemy(),
    new EnemyIdleState(), new EnemyPatrolState());

// 3. Start (can only be started once; repeated starts throw GameException)
fsm.Start<EnemyIdleState>();

// 4. Change state (can also be changed from within a state, see below)
fsm.ChangeState<EnemyPatrolState>();

// 5. Destroy
GameModule.FSM.DestroyFSM(fsm);
```

## Advanced Usage

To change state from within a state, use the `protected` methods provided by `FSMState<T>`:

```csharp
public class EnemyIdleState : FSMState<Enemy>
{
    protected internal override void OnUpdate(IFSM<Enemy> fsm, float elapseSeconds, float realElapseSeconds)
    {
        ChangeState<EnemyPatrolState>(fsm);          // Generic version
        // ChangeState(fsm, typeof(EnemyPatrolState)); // Type version
    }
}
```

Named state machines and data sharing:

```csharp
// Multiple independent state machines can be created for the same owner type
IFSM<Enemy> fsmA = GameModule.FSM.CreateFSM("enemy-a", enemyA, new EnemyIdleState());
IFSM<Enemy> fsmB = GameModule.FSM.CreateFSM("enemy-b", enemyB, new EnemyIdleState());

// Retrieve by name later
IFSM<Enemy> found = GameModule.FSM.GetFSM<Enemy>("enemy-a");
bool exists = GameModule.FSM.HasFSM<Enemy>("enemy-b");

// Share data between states (name -> object)
fsm.SetData("PatrolIndex", 0);
int index = fsm.GetData<int>("PatrolIndex");
fsm.RemoveData("PatrolIndex");

// Current state and duration
FSMState<Enemy> current = fsm.CurrentState;
float staying = fsm.CurrentStateTime; // Logical seconds the current state has been active
```

## Notes

- Lifecycle methods are all `protected internal virtual`; refer to source code for exact signatures: `OnInit(IFSM<T>)`, `OnEnter(IFSM<T>)`, `OnUpdate(IFSM<T>, float elapseSeconds, float realElapseSeconds)`, `OnExit(IFSM<T>, bool isShutdown)`, `OnDestroy(IFSM<T>)`
- The callback when leaving a state is `OnExit` (not `OnLeave`); `isShutdown` being `true` indicates the exit is due to the state machine being shut down
- `OnInit` is called immediately for each state during `CreateFSM`; the state machine will not poll before `Start<TState>()` is called
- Creating the same `(ownerType, name)` twice, or calling `Start` on an already running state machine, both throw `GameException`
- Switching to a non-existent state throws `GameException`; `ChangeState` requires the state machine to be running
- The state collection must contain at least one state, otherwise `GameException` is thrown on creation
- When the framework shuts down, `FSMModule.Shutdown` will close all state machines in sequence, triggering `OnExit(fsm, true)` and `OnDestroy(fsm)` on each state

---
[« Back to Main README](../../README_EN.md)