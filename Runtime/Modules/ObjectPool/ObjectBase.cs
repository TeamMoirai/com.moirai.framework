namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用对象池对象基类。
    /// <para>池化对象必须继承此类并实现 <see cref="Release(bool)"/>；对象经 <see cref="MemoryPool"/> 复用，重置逻辑写在 <see cref="Clear"/>。</para>
    /// <para>生命周期：外部构造并 <c>Initialize</c> → <c>Register</c> 入池 → <see cref="OnSpawn"/>/<see cref="OnDespawn"/> 往复 → <see cref="Release(bool)"/> 永久移除。</para>
    /// </summary>
    public abstract class ObjectBase : MemoryObject
    {
        #region 字段 [FIELDS]

        private string _name;
        private object _target;
        private bool _locked;
        private float _lastUseTime;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取对象名称（用于按名取用）。
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// 获取对象引用目标（判等与查找键）。
        /// </summary>
        public object Target => _target;

        /// <summary>
        /// 获取或设置是否锁定（锁定对象不会被自动释放）。
        /// </summary>
        public bool Locked
        {
            get => _locked;
            set => _locked = value;
        }

        /// <summary>
        /// 获取或设置最近使用时间（实时时钟，由池维护）。
        /// </summary>
        public float LastUseTime
        {
            get => _lastUseTime;
            internal set => _lastUseTime = value;
        }

        /// <summary>
        /// 获取自定义可释放标记（默认恒 true；子类可覆写以阻止自动释放）。
        /// </summary>
        public virtual bool CustomCanReleaseFlag => true;

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化对象（无名、未锁定）。
        /// </summary>
        /// <param name="target">引用目标。</param>
        protected void Initialize(object target)
        {
            Initialize(string.Empty, target, false);
        }

        /// <summary>
        /// 初始化对象。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="target">引用目标。</param>
        /// <param name="locked">是否锁定。</param>
        protected void Initialize(string name, object target, bool locked = false)
        {
            _name = name ?? string.Empty;
            _target = target;
            _locked = locked;
            _lastUseTime = 0f;
        }

        #endregion

        #region 池回调 [POOL CALLBACKS]

        /// <summary>
        /// 对象从池中取用时回调。
        /// </summary>
        protected internal virtual void OnSpawn()
        {
        }

        /// <summary>
        /// 对象归还池中时回调。
        /// </summary>
        protected internal virtual void OnDespawn()
        {
        }

        /// <summary>
        /// 对象被永久移出池时回调（容量裁剪/过期/关闭池）。
        /// </summary>
        /// <param name="isShutdown">是否因池关闭而移除。</param>
        protected internal abstract void Release(bool isShutdown);

        #endregion

        #region MemoryObject 重写 [MEMORY OBJECT OVERRIDE]

        /// <summary>
        /// 清理对象状态（归还 MemoryPool 前调用）。
        /// </summary>
        public override void Clear()
        {
            _name = null;
            _target = null;
            _locked = false;
            _lastUseTime = 0f;
        }

        #endregion
    }

    /// <summary>
    /// 强类型引用目标的通用对象池对象基类。
    /// </summary>
    /// <typeparam name="TTarget">引用目标类型。</typeparam>
    public abstract class ObjectBase<TTarget> : ObjectBase where TTarget : class
    {
        /// <summary>
        /// 获取强类型引用目标。
        /// </summary>
        public new TTarget Target => (TTarget)base.Target;

        /// <summary>
        /// 初始化对象（无名、未锁定）。
        /// </summary>
        /// <param name="target">引用目标。</param>
        protected void Initialize(TTarget target)
        {
            Initialize(string.Empty, target, false);
        }

        /// <summary>
        /// 初始化对象。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="target">引用目标。</param>
        /// <param name="locked">是否锁定。</param>
        protected void Initialize(string name, TTarget target, bool locked = false)
        {
            base.Initialize(name, target, locked);
        }
    }
}
