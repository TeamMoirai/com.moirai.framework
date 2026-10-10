namespace Moirai.Atropos
{
    /// <summary>
    /// MonoBehaviour 持久单例：最早创建的实例作为单例，且跨场景存活。
    /// </summary>
    /// <typeparam name="T">继承本基类的具体单例类型。</typeparam>
    /// <remarks>
    /// 强制 <see cref="SingletonMono{T}.m_Persistent"/> 为 true（等效 DontDestroyOnLoad），
    /// 多实例策略仍为先到先得。适用于全局脚本，eg：配置管理器、游戏管理器等。
    /// </remarks>
    // ReSharper disable once InconsistentNaming
    public abstract class SingletonMono_Persistent<T> : SingletonMono<T> where T : SingletonMono_Persistent<T>
    {
        /// <summary>
        /// 强制持久化后按基类策略消解多实例。
        /// </summary>
        protected override void CheckMultipleInstance()
        {
            m_Persistent = true;

            base.CheckMultipleInstance();
        }
    }
}
