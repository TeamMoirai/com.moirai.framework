#if R3_INSTALLED
using System;

namespace Moirai.Atropos.R3
{
    /// <summary>
    /// 取消注册用于管理 <see cref="IDisposable"/> 的 scope 接口
    /// </summary>
    public interface IDisposableUnregister
    {
        /// <summary>
        /// Register new disposable to this unregister scope
        /// </summary>
        /// <param name="disposable"></param>
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