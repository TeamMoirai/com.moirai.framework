namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 通用对象池服务外观（Facade）。
    /// <para>统一的静态通用池访问入口，通过替换 <see cref="Handler"/> 即可在不同池后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="ObjectPoolServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// <para>通用池面向任意 <see cref="ObjectBase"/> 派生对象（非 GameObject）；GameObject 池化请使用 <see cref="GameObjectPoolService"/>。</para>
    /// </summary>
    [HandlerHost(typeof(ObjectPoolServiceHandler))]
    [UnityEngine.Scripting.Preserve]
    public partial class ObjectPoolService : ServiceBase, IServiceTickable
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="ObjectPoolServiceSettings"/> 创建默认通用对象池处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认通用对象池处理器实例。</returns>
        private static ObjectPoolServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<ObjectPoolService>();
            return ObjectPoolServiceSettings.ObjectPoolServiceHandler;
        }

        /// <summary>
        /// 获取服务优先级。
        /// </summary>
        public override int Priority => 5;

        /// <summary>
        /// 初始化通用对象池服务。由容器在构建期调用。
        /// <para>确保 <c>ObjectPoolService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭通用对象池服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器处理到期的维护操作。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            Handler.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用。
        /// </summary>
        public static bool IsValid => s_Handler != null;

        /// <summary>
        /// 获取池数量。
        /// </summary>
        public static int Count => Handler.Count;

        #endregion

        #region 池管理 [POOL MANAGEMENT]

        /// <summary>
        /// 是否存在指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>是否存在。</returns>
        public static bool HasObjectPool<T>(string name = "") where T : ObjectBase =>
            Handler.HasObjectPool<T>(name);

        /// <summary>
        /// 获取指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>池实例；不存在返回 null。</returns>
        public static IObjectPool<T> GetObjectPool<T>(string name = "") where T : ObjectBase =>
            Handler.GetObjectPool<T>(name);

        /// <summary>
        /// 获取或创建指定类型的池。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="options">创建选项（已存在时忽略）。</param>
        /// <returns>池实例；服务未注册返回 null。</returns>
        public static IObjectPool<T> GetOrCreatePool<T>(ObjectPoolCreateOptions options = default) where T : ObjectBase =>
            Handler.GetOrCreatePool<T>(options);

        /// <summary>
        /// 销毁指定类型的池（释放其全部对象）。
        /// </summary>
        /// <typeparam name="T">池化对象类型。</typeparam>
        /// <param name="name">池名称。</param>
        /// <returns>是否销毁成功。</returns>
        public static bool DestroyObjectPool<T>(string name = "") where T : ObjectBase =>
            Handler.DestroyObjectPool<T>(name);

        /// <summary>
        /// 获取全部池（按优先级可选排序）填充到结果数组。
        /// </summary>
        /// <param name="sort">是否按优先级降序排序。</param>
        /// <param name="results">结果数组。</param>
        /// <returns>池总数（可能超出数组容量）。</returns>
        public static int GetAllObjectPools(bool sort, ObjectPoolBase[] results) =>
            Handler.GetAllObjectPools(sort, results);

        #endregion

        #region 释放 [RELEASE]

        /// <summary>
        /// 释放所有池的全部可释放对象。
        /// </summary>
        public static void Release() =>
            Handler.Release();

        /// <summary>
        /// 释放所有池的全部未使用且可释放的对象（低内存响应同此）。
        /// </summary>
        public static void ReleaseAllUnused() =>
            Handler.ReleaseAllUnused();

        #endregion
    }
}