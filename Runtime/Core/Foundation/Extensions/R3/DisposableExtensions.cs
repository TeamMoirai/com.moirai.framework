#if R3_INSTALLED
using System;

namespace Moirai.Atropos.R3
{
    /// <summary>
    /// 取消注册用于管理 <see cref="IDisposable"/> 的 scope 接口。
    /// </summary>
    public interface IDisposableUnregister
    {
        /// <summary>
        /// 将新的可释放对象注册到该取消注册 scope。
        /// </summary>
        void Register(IDisposable disposable);
    }

    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, IDisposableUnregister unRegister) where T : IDisposable
        {
            unRegister.Register(disposable);
            return disposable;
        }
    }
}
#endif