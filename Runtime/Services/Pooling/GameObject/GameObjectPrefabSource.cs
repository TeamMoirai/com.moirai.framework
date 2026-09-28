using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池预制体来源：Location（经加载器）或 External（外部引用，池不拥有）。
    /// <para>封装加载状态机与生命周期；主线程访问。</para>
    /// </summary>
    internal sealed class GameObjectPrefabSource
    {
        #region 常量 [CONSTANTS]

        /// <summary>
        /// 来源类型。
        /// </summary>
        public enum EKind : byte
        {
            Location = 0,
            External = 1
        }

        #endregion

        #region 字段 [FIELDS]

        private IPrefabLoader _loader;
        private GameObject _prefab;
        private EKind _kind;
        private UniTaskCompletionSource<GameObject> _completionSource;
        private bool _loading;
        private bool _isShuttingDown;
        private int _loadVersion;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 预制体是否就绪。
        /// </summary>
        public bool IsReady => _prefab != null;

        /// <summary>
        /// 当前预制体（可能为 null）。
        /// </summary>
        public GameObject Prefab => _prefab;

        /// <summary>
        /// 是否为 External 源。
        /// </summary>
        public bool IsExternal => _kind == EKind.External;

        /// <summary>
        /// 是否正在异步加载。
        /// </summary>
        public bool IsLoading => _loading;

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化为资源地址源。
        /// </summary>
        /// <param name="loader">预制体加载器。</param>
        public void InitializeLocation(IPrefabLoader loader)
        {
            ResetCore();
            _kind = EKind.Location;
            _loader = loader;
        }

        /// <summary>
        /// 初始化为外部预制体源（池不负责加载/卸载）。
        /// </summary>
        /// <param name="prefab">外部预制体。</param>
        public void InitializeExternal(GameObject prefab)
        {
            ResetCore();
            _kind = EKind.External;
            _prefab = prefab;
            _loader = null;
        }

        /// <summary>
        /// 清理状态（归还池对象前调用）。
        /// </summary>
        public void Clear()
        {
            _completionSource?.TrySetCanceled();
            ResetCore();
        }

        #endregion

        #region 加载 [LOADING]

        /// <summary>
        /// 同步确保预制体就绪。External 需引用有效；Location 经加载器同步加载。
        /// </summary>
        /// <param name="location">资源地址（External 忽略）。</param>
        /// <returns>是否就绪。</returns>
        public bool EnsureLoaded(string location)
        {
            if (_kind == EKind.External)
            {
                return _prefab != null;
            }

            if (_prefab != null)
            {
                return true;
            }

            if (_loading)
            {
                return false;
            }

            _prefab = _loader.LoadPrefab(location);
            return _prefab != null;
        }

        /// <summary>
        /// 异步确保预制体就绪。
        /// </summary>
        /// <param name="location">资源地址（External 忽略）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>是否就绪。</returns>
        public async UniTask<bool> EnsureLoadedAsync(string location, CancellationToken cancellationToken)
        {
            if (_kind == EKind.External)
            {
                return _prefab != null;
            }

            if (_prefab != null)
            {
                return true;
            }

            if (_loading)
            {
                await _completionSource.Task.AttachExternalCancellation(cancellationToken);
                return _prefab != null;
            }

            _loading = true;
            // 先捕获局部引用再启动加载——同步完成的加载器会立刻消费并置空字段，直接 await 字段将 NRE。
            UniTaskCompletionSource<GameObject> completionSource = new UniTaskCompletionSource<GameObject>();
            _completionSource = completionSource;
            RunLoadAsync(location, _loadVersion).Forget();
            await completionSource.Task.AttachExternalCancellation(cancellationToken);
            return _prefab != null;
        }

        /// <summary>
        /// 关闭：取消在途加载并卸载 Location 预制体。
        /// </summary>
        public void Shutdown()
        {
            _isShuttingDown = true;
            _loadVersion++;
            _loading = false;
            _completionSource?.TrySetCanceled();
            _completionSource = null;
            UnloadIfOwned();
        }

        /// <summary>
        /// Location 源卸载预制体；External 源仅断开引用。
        /// </summary>
        public void UnloadIfOwned()
        {
            if (_prefab != null && _kind == EKind.Location)
            {
                _loader?.UnloadPrefab(_prefab);
            }

            _prefab = null;
            _loading = false;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void ResetCore()
        {
            _loader = null;
            _prefab = null;
            _kind = EKind.Location;
            _completionSource = null;
            _loading = false;
            _isShuttingDown = false;
            _loadVersion++;
        }

        private async UniTaskVoid RunLoadAsync(string location, int loadVersion)
        {
            GameObject loaded = null;
            try
            {
                loaded = await _loader.LoadPrefabAsync(location);
            }
            catch (OperationCanceledException)
            {
                loaded = null;
            }
            catch (Exception e)
            {
                LogUtility.Error("[GameObjectPool] Prefab load failed. Location:{0}, Error:{1}", location, e.Message);
                loaded = null;
            }

            if (_isShuttingDown || loadVersion != _loadVersion)
            {
                if (loaded != null && _loader != null)
                {
                    _loader.UnloadPrefab(loaded);
                }

                _loading = false;
                _completionSource?.TrySetCanceled();
                _completionSource = null;
                return;
            }

            _prefab = loaded;
            _loading = false;
            UniTaskCompletionSource<GameObject> completionSource = _completionSource;
            _completionSource = null;
            completionSource?.TrySetResult(_prefab);
        }

        #endregion
    }
}
