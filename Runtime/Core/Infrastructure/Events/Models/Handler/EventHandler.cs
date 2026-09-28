namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 能够使用回调来处理事件类的接口。
    /// </summary>
    public abstract class CallbackEventHandler : IEventHandler
    {
        public virtual bool IsCompositeRoot => false;
        
        public abstract IEventCoordinator Coordinator { get; }
        
        /// <summary>
        /// 获取和设置父回调处理程序
        /// </summary>
        /// <value></value>
        public CallbackEventHandler Parent { get; set; }
        
        private EventCallbackRegistry _callbackRegistry;

        /// <summary>
        /// 向实例添加事件处理程序。如果已为同一阶段（TrickleDown 或 BubbleUp）注册了事件处理程序，则此方法无效。
        /// </summary>
        /// <param name="callback">要添加的事件处理程序。</param>
        /// <param name="useTrickleDown">将此参数设置为 <c>true</c> 以从 TrickleDown 阶段删除回调。将此参数设置为 <c>false</c> 以从 BubbleUp 阶段删除回调。</param>
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            _callbackRegistry ??= new EventCallbackRegistry();

            _callbackRegistry.RegisterCallback(callback, useTrickleDown, default);
#if UNITY_EDITOR
            GlobalCallbackRegistry.RegisterListeners<TEventType>(this, callback, useTrickleDown);
#endif
            // AddEventCategories<TEventType>();
        }

        // TODO: Encode event categories
        // private void AddEventCategories<TEventType>() where TEventType : EventBase<TEventType>, new()
        // {

        // }

        /// <summary>
        /// 向实例添加事件处理程序。如果已为同一阶段注册了事件处理程序，则此方法无效。
        /// </summary>
        /// <param name="callback">要添加的事件处理程序。</param>
        /// <param name="userArgs">要传递给回调的数据。</param>
        public void RegisterCallback<TEventType, TUserArgsType>(EventCallback<TEventType, TUserArgsType> callback, TUserArgsType userArgs, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            _callbackRegistry ??= new EventCallbackRegistry();

            _callbackRegistry.RegisterCallback(callback, userArgs, useTrickleDown, default);
#if UNITY_EDITOR
            GlobalCallbackRegistry.RegisterListeners<TEventType>(this, callback, useTrickleDown);
#endif
            // AddEventCategories<TEventType>();
        }
        internal void RegisterCallback<TEventType>(EventCallback<TEventType> callback, InvokePolicy invokePolicy, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            _callbackRegistry ??= new EventCallbackRegistry();

            _callbackRegistry.RegisterCallback(callback, useTrickleDown, invokePolicy);

#if UNITY_EDITOR
            GlobalCallbackRegistry.RegisterListeners<TEventType>(this, callback, useTrickleDown);
#endif

            // AddEventCategories<TEventType>();
        }

        /// <summary>
        ///从实例中删除回调。
        /// </summary>
        /// <param name="callback">要删除的回调。如果此回调从未注册，则不会有影响。</param>
        /// <param name="useTrickleDown">将此参数设置为 <c>true</c> 以从 TrickleDown 阶段删除回调。将此参数设置为 <c>false</c> 以从 BubbleUp 阶段删除回调。</param>
        public void UnregisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            _callbackRegistry?.UnregisterCallback(callback, useTrickleDown);

#if UNITY_EDITOR
            GlobalCallbackRegistry.UnregisterListeners<TEventType>(this, callback);
#endif
        }

        /// <summary>
        ///从实例中删除回调。
        /// </summary>
        /// <param name="callback">要删除的回调。如果此回调从未注册，则不会有影响。</param>
        /// <param name="useTrickleDown">将此参数设置为 <c>true</c> 以从 TrickleDown 阶段删除回调。将此参数设置为 <c>false</c> 以从 BubbleUp 阶段删除回调。</param>
        public void UnregisterCallback<TEventType, TUserArgsType>(EventCallback<TEventType, TUserArgsType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            _callbackRegistry?.UnregisterCallback(callback, useTrickleDown);

#if UNITY_EDITOR
            GlobalCallbackRegistry.UnregisterListeners<TEventType>(this, callback);
#endif
        }

        internal bool TryGetUserArgs<TEventType, TCallbackArgs>(EventCallback<TEventType, TCallbackArgs> callback, TrickleDown useTrickleDown, out TCallbackArgs userData) where TEventType : EventBase<TEventType>, new()
        {
            userData = default;

            if (_callbackRegistry != null)
            {
                return _callbackRegistry.TryGetUserArgs(callback, useTrickleDown, out userData);
            }

            return false;
        }

        /// <summary>
        /// 将事件发送到事件处理程序。
        /// </summary>
        /// <param name="e">要发送的事件。</param>
        /// <param name="dispatchMode">事件调度模式。</param>
        public abstract void SendEvent(EventBase e, DispatchMode dispatchMode = DispatchMode.Default);

        public void HandleEventAtTargetPhase(EventBase evt)
        {
            evt.CurrentTarget = evt.Target;
            evt.PropagationPhase = PropagationPhase.AtTarget;
            HandleEventAtCurrentTargetAndPhase(evt);
            evt.PropagationPhase = PropagationPhase.DefaultActionAtTarget;
            HandleEventAtCurrentTargetAndPhase(evt);
        }

        public void HandleEventAtTargetAndDefaultPhase(EventBase evt)
        {
            HandleEventAtTargetPhase(evt);
            evt.PropagationPhase = PropagationPhase.DefaultAction;
            HandleEventAtCurrentTargetAndPhase(evt);
        }

        public void HandleEventAtCurrentTargetAndPhase(EventBase evt)
        {
            if (evt == null)
                return;

            switch (evt.PropagationPhase)
            {
                case PropagationPhase.TrickleDown:
                case PropagationPhase.BubbleUp:
                    {
                        if (!evt.IsPropagationStopped)
                        {
                            _callbackRegistry?.InvokeCallbacks(evt, evt.PropagationPhase);
                        }
                        break;
                    }
                case PropagationPhase.AtTarget:
                    {
                        // 当直接到达目标时，确保在 BubbleUp 回调之前调用 TrickleDownPhase 的回调
                        if (!evt.IsPropagationStopped)
                        {
                            _callbackRegistry?.InvokeCallbacks(evt, PropagationPhase.TrickleDown);
                        }
                        if (!evt.IsPropagationStopped)
                        {
                            _callbackRegistry?.InvokeCallbacks(evt, PropagationPhase.BubbleUp);
                        }
                    }
                    break;
                case PropagationPhase.DefaultActionAtTarget:
                    {
                        if (!evt.IsDefaultPrevented)
                        {
                            using (new EventDebuggerLogExecuteDefaultAction(evt))
                            {
                                if (evt.SkipDisabledElements && this is IBehaviourScope bs && !bs.Behaviour.isActiveAndEnabled)
                                    ExecuteDefaultActionDisabledAtTarget(evt);
                                else
                                    ExecuteDefaultActionAtTarget(evt);
                            }
                        }
                        break;
                    }

                case PropagationPhase.DefaultAction:
                    {
                        if (!evt.IsDefaultPrevented)
                        {
                            using (new EventDebuggerLogExecuteDefaultAction(evt))
                            {
                                if (evt.SkipDisabledElements && this is IBehaviourScope bs && !bs.Behaviour.isActiveAndEnabled)
                                    ExecuteDefaultActionDisabled(evt);
                                else
                                    ExecuteDefaultAction(evt);
                            }
                        }
                        break;
                    }
            }
        }


        void IEventHandler.HandleEvent(EventBase evt)
        {
            HandleEventAtCurrentTargetAndPhase(evt);
        }

        /// <summary>
        /// 如果事件传播 TrickleDown 阶段的事件处理程序附加到此对象，则返回 <c>true</c>。
        /// </summary>
        /// <returns>如果 object 有 TrickleDown 阶段的事件处理程序，则为 True。</returns>
        public bool HasTrickleDownHandlers()
        {
            return _callbackRegistry != null && _callbackRegistry.HasTrickleDownHandlers();
        }

        /// <summary>
        /// 如果事件传播 BubbleUp 阶段的事件处理程序已附加到此对象上，则返回 <c>true</c>。
        /// </summary>
        /// <returns>如果 object 有 BubbleUp 阶段的事件处理程序，则为 True。</returns>
        public bool HasBubbleUpHandlers()
        {
            return _callbackRegistry != null && _callbackRegistry.HasBubbleHandlers();
        }
        /// <summary>
        /// 在事件目标上注册的回调执行后执行逻辑，
        /// 除非该事件被标记为阻止其默认行为。
        /// <see cref="EventBase.PreventDefault"/>.
        /// </summary>
        /// <param name="evt">The event instance.</param>
        protected virtual void ExecuteDefaultActionAtTarget(EventBase evt) { }

        /// <summary>
        /// 在事件目标上注册的回调执行后执行逻辑，
        /// 除非已标记事件以防止其默认行为。
        /// <see cref="EventBase.PreventDefault"/>.
        /// </summary>
        /// <remarks>
        /// 此方法旨在被子类覆盖。使用它来实现事件处理，而无需注册回调，从而保证子类用户注册回调的优先级。
        /// 与 <see cref="ExecuteDefaultActionAtTarget"/> 不同，此方法在元素上注册回调后调用。
        /// </remarks>
        /// <param name="evt">The event instance.</param>
        protected virtual void ExecuteDefaultAction(EventBase evt) { }

        protected virtual void ExecuteDefaultActionDisabledAtTarget(EventBase evt) { }

        protected virtual void ExecuteDefaultActionDisabled(EventBase evt) { }
    }
}
