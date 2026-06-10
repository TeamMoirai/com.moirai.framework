namespace Moirai.Atropos
{
    /// <summary>
    /// MonoBehaviour 持久单例。最早创建的实例作为单例。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <remarks>适用于全局脚本，eg：配置管理器、游戏管理器等。</remarks>
    // ReSharper disable once InconsistentNaming
    public abstract class SingletonMono_Persistent<T> : SingletonMono<T> where T : SingletonMono_Persistent<T>
    {
        protected override void CheckMultipleInstance()
        {
            m_Persistent = true;

            base.CheckMultipleInstance();
        }
    }
}