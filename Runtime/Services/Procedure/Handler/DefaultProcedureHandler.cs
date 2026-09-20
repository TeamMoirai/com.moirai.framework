using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 默认流程处理器（纯 C# 状态机实现）。
    /// <para><see cref="ProcedureServiceHandler"/> 的内置实现，承载全部流程状态管理逻辑。</para>
    /// <para>切换在 <c>OnLeave</c>/<c>OnEnter</c> 执行期间重入受深度上限保护——超出即抛出
    /// <see cref="GameException"/>（互为 OnEnter 互切的流程环会在此 fail-fast，而非栈溢出）。</para>
    /// <para>嵌套切换语义：OnEnter/OnLeave 内的合法重定向（如闪屏直切）会递归完成再逐层记录；
    /// 中间流程可能未走 OnLeave，历史记录以最外层完成态为准（每条记录的 To 即广播时刻的当前流程）。</para>
    /// </summary>
    [Serializable]
    internal sealed class DefaultProcedureHandler : ProcedureServiceHandler
    {
        /// <summary>单次调用栈内允许的最大切换深度（合法嵌套远低于此值，超出即判定为流程环）。</summary>
        private const int MaxTransitionDepth = 16;

        private static readonly ProcedureBase[] EmptyProcedures = new ProcedureBase[0];

        [NonSerialized] private Dictionary<Type, ProcedureBase> _states;
        [NonSerialized] private ProcedureBase _currentState;
        [NonSerialized] private float _currentStateTime;
        [NonSerialized] private bool _isStateReady;
        [NonSerialized] private int _transitionDepth;

        /// <summary>
        /// 状态机是否已就绪（已 <see cref="Initialize"/> 且未关停）。
        /// </summary>
        public override bool IsStateReady => _isStateReady;

        /// <summary>
        /// 当前流程。
        /// </summary>
        public override ProcedureBase CurrentProcedure
        {
            get
            {
                if (!_isStateReady)
                {
                    throw new GameException("You must initialize procedure first.");
                }

                return _currentState;
            }
        }

        /// <summary>
        /// 当前流程持续时间。
        /// </summary>
        public override float CurrentProcedureTime
        {
            get
            {
                if (!_isStateReady)
                {
                    throw new GameException("You must initialize procedure first.");
                }

                return _currentStateTime;
            }
        }

        /// <summary>
        /// 已注册的全部流程（未初始化时为空集）。
        /// </summary>
        public override IReadOnlyCollection<ProcedureBase> Procedures =>
            _states != null ? (IReadOnlyCollection<ProcedureBase>)_states.Values : EmptyProcedures;

        /// <summary>
        /// 处理器初始化。
        /// </summary>
        protected override void OnInit()
        {
            _states ??= new Dictionary<Type, ProcedureBase>();
            _currentState = null;
            _currentStateTime = 0f;
            _isStateReady = false;
            ClearTransitionHistory();
        }

        /// <summary>
        /// 处理器关闭，销毁全部流程状态。
        /// <para>关停是不可跳过的收尾路径，逐流程隔离异常：当前流程 <c>OnLeave(true)</c> 或任一
        /// <c>OnDestroy</c> 抛出时记错误日志后继续——单个坏流程不得阻断其余流程的销毁回调，
        /// 关停切换记录在 finally 中保证写入。</para>
        /// </summary>
        protected override void OnShutdown()
        {
            if (_isStateReady)
            {
                if (_currentState != null)
                {
                    // From 取 OnLeave(true) 调用前的引用，防御 OnLeave 内重定向导致的记录错位
                    ProcedureBase from = _currentState;
                    float currentStateTime = _currentStateTime;
                    try
                    {
                        from.OnLeave(true);
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Error("Procedure '{0}' threw in OnLeave during shutdown: {1}",
                            from.GetType().FullName, ex);
                    }
                    finally
                    {
                        RecordTransition(new ProcedureTransitionRecord(
                            ProcedureTransitionKind.Shutdown, from, null, currentStateTime));
                    }
                }

                foreach (KeyValuePair<Type, ProcedureBase> state in _states)
                {
                    try
                    {
                        state.Value.OnDestroy();
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Error("Procedure '{0}' threw in OnDestroy during shutdown: {1}",
                            state.Key.FullName, ex);
                    }
                }

                _isStateReady = false;
            }

            _currentState = null;
            _currentStateTime = 0f;
            // _states 由 OnInit 契约保证非空；容错处理防御异常关停次序
            _states?.Clear();
            // 切换历史保留（含关停记录）供事后诊断，重新 Initialize 时才清空
        }

        /// <summary>
        /// 轮询当前流程。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (!_isStateReady || _currentState == null)
            {
                return;
            }

            _currentStateTime += elapseSeconds;
            _currentState.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 初始化流程管理器。
        /// <para>任一流程 <c>OnInit</c> 抛出即整体 fail-fast（异常上抛，<see cref="IsStateReady"/> 保持 false）；
        /// 已完成 <c>OnInit</c> 的流程不做回收（保留现场供诊断），调用方应丢弃整批流程实例后重建传入。</para>
        /// </summary>
        /// <param name="procedures">流程管理器包含的流程。</param>
        public override void Initialize(params ProcedureBase[] procedures)
        {
            if (procedures == null || procedures.Length < 1)
            {
                throw new GameException("Procedures is invalid.");
            }

            if (_currentState != null)
            {
                throw new GameException("Procedure is running, can not initialize again. Use RestartProcedure instead.");
            }

            _states.Clear();
            _currentState = null;
            _currentStateTime = 0f;
            _isStateReady = false;
            ClearTransitionHistory();

            foreach (ProcedureBase procedure in procedures)
            {
                if (procedure == null)
                {
                    throw new GameException("Procedure is invalid.");
                }

                Type procedureType = procedure.GetType();
                if (_states.ContainsKey(procedureType))
                {
                    throw new GameException(StringUtility.Format("Procedure '{0}' is already exist.", procedureType.FullName));
                }

                procedure.SetOwner(this);
                _states.Add(procedureType, procedure);
                procedure.OnInit();
            }

            _isStateReady = true;
        }

        /// <summary>
        /// 开始流程。
        /// <para><c>OnEnter</c> 抛出时异常上抛并回滚到未启动态（当前流程置空，修复后可重新 StartProcedure）；
        /// 若 <c>OnEnter</c> 内已完成嵌套重定向（当前流程不再是本流程），保留嵌套终态不回滚。</para>
        /// </summary>
        /// <param name="procedureType">要开始的流程类型。</param>
        public override void StartProcedure(Type procedureType)
        {
            ThrowIfBroadcastingTransition();

            if (!_isStateReady)
            {
                throw new GameException("You must initialize procedure first.");
            }

            if (_currentState != null)
            {
                throw new GameException("Procedure is running, can not start again.");
            }

            ProcedureBase procedure = GetRegisteredProcedure(procedureType);

            _currentStateTime = 0f;
            _currentState = procedure;
            try
            {
                procedure.OnEnter();
            }
            catch
            {
                if (ReferenceEquals(_currentState, procedure))
                {
                    _currentState = null;
                    _currentStateTime = 0f;
                }

                throw;
            }

            // To 取 OnEnter 完成后的稳定态——OnEnter 内嵌套重定向时与 CurrentProcedure 保持一致
            RecordTransition(new ProcedureTransitionRecord(
                ProcedureTransitionKind.Start, null, _currentState, 0f));
        }

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <param name="procedureType">要检查的流程类型。</param>
        /// <returns>是否存在流程。</returns>
        public override bool HasProcedure(Type procedureType)
        {
            if (!_isStateReady)
            {
                throw new GameException("You must initialize procedure first.");
            }

            return _states.ContainsKey(ValidateProcedureType(procedureType));
        }

        /// <summary>
        /// 切换流程。
        /// <para>目标流程 <c>OnEnter</c> 抛出时异常上抛并回滚到切出流程（它此前是完整进入态，恢复其驻留时长，
        /// 轮询安全），避免半进入流程继续被 <c>OnUpdate</c>；若 <c>OnEnter</c> 内已完成嵌套重定向
        /// （当前流程已不再是目标流程），保留嵌套终态不回滚。<c>OnLeave</c> 抛出不影响当前流程（仍指向切出流程）。</para>
        /// </summary>
        /// <param name="procedureType">要切换的状态类型。</param>
        public override void ChangeState(Type procedureType)
        {
            ThrowIfBroadcastingTransition();

            if (!_isStateReady)
            {
                throw new GameException("You must initialize procedure first.");
            }

            if (_currentState == null)
            {
                throw new GameException("Current procedure is invalid.");
            }

            ProcedureBase procedure = GetRegisteredProcedure(procedureType);
            if (_transitionDepth >= MaxTransitionDepth)
            {
                throw new GameException(StringUtility.Format(
                    "ChangeState depth exceeds {0} — procedure loop detected between '{1}' and '{2}'. " +
                    "Do not switch procedures mutually inside OnEnter/OnLeave.",
                    MaxTransitionDepth, _currentState.GetType().FullName, procedureType?.FullName));
            }

            ProcedureBase from = _currentState;
            float fromElapsed = _currentStateTime;
            _transitionDepth++;
            try
            {
                from.OnLeave(false);
                _currentStateTime = 0f;
                _currentState = procedure;
                procedure.OnEnter();
            }
            catch
            {
                if (ReferenceEquals(_currentState, procedure))
                {
                    _currentState = from;
                    _currentStateTime = fromElapsed;
                }

                throw;
            }
            finally
            {
                _transitionDepth--;
            }

            // To 取 OnEnter 完成后的稳定态——OnLeave/OnEnter 内嵌套重定向时与 CurrentProcedure 保持一致
            RecordTransition(new ProcedureTransitionRecord(
                ProcedureTransitionKind.Change, from, _currentState, fromElapsed));
        }

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <param name="procedureType">要获取的流程类型。</param>
        /// <returns>要获取的流程（不存在时为 null）。</returns>
        public override ProcedureBase GetProcedure(Type procedureType)
        {
            if (!_isStateReady)
            {
                throw new GameException("You must initialize procedure first.");
            }

            ValidateProcedureType(procedureType);
            return _states.TryGetValue(procedureType, out ProcedureBase procedure) ? procedure : null;
        }

        /// <summary>
        /// 重启流程。默认使用第一个流程作为启动流程。
        /// </summary>
        /// <param name="procedures">新的流程。</param>
        /// <returns>是否重启成功。</returns>
        public override bool RestartProcedure(params ProcedureBase[] procedures)
        {
            ThrowIfBroadcastingTransition();

            if (procedures == null || procedures.Length <= 0)
            {
                throw new GameException("RestartProcedure Failed procedures is invalid.");
            }

            OnShutdown();
            Initialize(procedures);
            StartProcedure(procedures[0].GetType());
            return true;
        }

        #region 校验与解析 [VALIDATION]

        /// <summary>
        /// 广播期重入防护——<see cref="ProcedureService.onProcedureChanged"/> 回调内禁止同步启动/切换。
        /// <para>切换深度上限只防 OnEnter/OnLeave 互切环；事件回调发生在深度归零之后，须单独置位拒绝。</para>
        /// </summary>
        private void ThrowIfBroadcastingTransition()
        {
            if (IsBroadcastingTransition)
            {
                throw new GameException(
                    "StartProcedure/ChangeState is not allowed inside ProcedureChanged callback — defer to next frame or Tick.");
            }
        }

        /// <summary>
        /// 校验流程类型合法性（非空且为 <see cref="ProcedureBase"/> 子类），非法即抛出。
        /// </summary>
        private Type ValidateProcedureType(Type procedureType)
        {
            if (procedureType == null)
            {
                throw new GameException("Procedure type is invalid.");
            }

            if (!typeof(ProcedureBase).IsAssignableFrom(procedureType))
            {
                throw new GameException(StringUtility.Format("Procedure type '{0}' is invalid.", procedureType.FullName));
            }

            return procedureType;
        }

        /// <summary>
        /// 校验并解析已注册的流程，未注册即抛出。
        /// </summary>
        private ProcedureBase GetRegisteredProcedure(Type procedureType)
        {
            if (!_states.TryGetValue(ValidateProcedureType(procedureType), out ProcedureBase procedure))
            {
                throw new GameException(StringUtility.Format("Procedure '{0}' is not registered.", procedureType.FullName));
            }

            return procedure;
        }

        #endregion
    }
}
