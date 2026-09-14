using System.Collections.Generic;
using Moirai.Atropos.Resource;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景登记簿——场景服务的纯状态容器与决策单元。
    /// <para>承载主/子场景登记、location 级在途防重入、子场景短名反向索引与全部登记迁移决策；
    /// 不依赖日志与资源外观，决策以枚举结果返回，由 <see cref="DefaultSceneHandler"/> 在边界翻译为日志与异常。</para>
    /// <para>全部成员仅限主线程调用（场景加载管线本身即主线程契约），不做线程守卫。</para>
    /// </summary>
    internal sealed class SceneRegistry
    {
        /// <summary>
        /// 子场景登记状态。
        /// </summary>
        internal enum ESubSceneState : byte
        {
            /// <summary>已发起加载（可能挂起待激活），尚未完成。</summary>
            Loading = 0,

            /// <summary>加载完成，可激活与卸载。</summary>
            Loaded = 1,
        }

        /// <summary>
        /// 加载门禁判定结果。
        /// </summary>
        internal enum ELoadGate : byte
        {
            /// <summary>允许发起加载。</summary>
            Allow = 0,

            /// <summary>同地址存在在途加载/卸载操作。</summary>
            InFlight = 1,

            /// <summary>Additive：同地址已登记为子场景。</summary>
            SubAlreadyRegistered = 2,

            /// <summary>Single：存在在途主场景加载（互斥）。</summary>
            MainLoadInFlight = 3,

            /// <summary>Single：同地址已登记为子场景（跨模式重复）。</summary>
            RegisteredAsSub = 4,

            /// <summary>Additive：同地址已是当前主场景或在途主场景（跨模式重复）。</summary>
            RegisteredAsMain = 5,
        }

        /// <summary>
        /// 卸载门禁判定结果。
        /// </summary>
        internal enum EUnloadGate : byte
        {
            /// <summary>允许发起卸载（已占用在途标记）。</summary>
            Allow = 0,

            /// <summary>地址未登记为子场景。</summary>
            NotRegistered = 1,

            /// <summary>同地址存在在途加载/卸载操作。</summary>
            InFlight = 2,

            /// <summary>子场景仍在加载中（含挂起），不可卸载。</summary>
            StillLoading = 3,
        }

        /// <summary>
        /// 子场景登记项——句柄、归一化场景短名与登记状态。
        /// </summary>
        internal readonly struct SubSceneEntry
        {
            /// <summary>资源系统场景句柄。</summary>
            internal readonly ResourceSceneHandle Handle;

            /// <summary>归一化场景短名（加载完成前为空串）。</summary>
            internal readonly string SceneName;

            /// <summary>登记状态。</summary>
            internal readonly ESubSceneState State;

            /// <summary>
            /// 创建子场景登记项。
            /// </summary>
            internal SubSceneEntry(ResourceSceneHandle handle, string sceneName, ESubSceneState state)
            {
                Handle = handle;
                SceneName = sceneName;
                State = state;
            }
        }

        /// <summary>
        /// 待执行卸载的解析结果——规范化登记地址、场景短名与句柄。
        /// <para>仅当门禁判定为 <see cref="EUnloadGate.Allow"/> 时完整有效；非 Allow 时仅 <see cref="Location"/> 可用于日志（NotRegistered 时为默认值）。</para>
        /// </summary>
        internal readonly struct PendingUnload
        {
            /// <summary>子场景登记地址。</summary>
            internal readonly string Location;

            /// <summary>归一化场景短名。</summary>
            internal readonly string SceneName;

            /// <summary>资源系统场景句柄。</summary>
            internal readonly ResourceSceneHandle Handle;

            /// <summary>
            /// 创建待执行卸载解析结果。
            /// </summary>
            internal PendingUnload(string location, string sceneName, ResourceSceneHandle handle)
            {
                Location = location;
                SceneName = sceneName;
                Handle = handle;
            }
        }

        private string _currentMainSceneName = string.Empty;
        private string _currentMainSceneLocation = string.Empty;
        private ResourceSceneHandle _mainSceneHandle;
        private ResourceSceneHandle _mainSceneLoadingHandle;
        private string _mainSceneLoadingLocation = string.Empty;

        private readonly Dictionary<string, SubSceneEntry> _subScenes = new Dictionary<string, SubSceneEntry>();
        private readonly Dictionary<string, string> _subSceneNameIndex = new Dictionary<string, string>();
        private readonly HashSet<string> _handlingScene = new HashSet<string>();

        /// <summary>当前主场景短名（<see cref="UnityEngine.SceneManagement.Scene.name"/> 归一化）。</summary>
        internal string CurrentMainSceneName => _currentMainSceneName;

        /// <summary>当前主场景资源地址（启动场景未经本服务加载时为空串）。</summary>
        internal string CurrentMainSceneLocation => _currentMainSceneLocation;

        /// <summary>当前主场景句柄（Single 模式加载完成，被替换时释放引用计数）。</summary>
        internal ResourceSceneHandle MainSceneHandle => _mainSceneHandle;

        /// <summary>主场景在途加载句柄（Single 发起后、收尾前；含挂起待激活）。非空即表示主场景加载互斥中。</summary>
        internal ResourceSceneHandle MainLoadingHandle => _mainSceneLoadingHandle;

        /// <summary>主场景在途加载的资源地址。</summary>
        internal string MainLoadingLocation => _mainSceneLoadingLocation;

        /// <summary>
        /// 取当前激活场景作为初始主场景（编辑器下启动场景可能不在 Build Settings，
        /// <c>GetSceneByBuildIndex(0)</c> 会得到无效场景）。
        /// </summary>
        internal void CaptureActiveMainScene(UnityEngine.SceneManagement.Scene scene)
        {
            _currentMainSceneName = scene.IsValid() ? scene.name : string.Empty;
            _currentMainSceneLocation = string.Empty;
        }

        #region 加载门禁与登记 [LOAD GATING & REGISTRATION]

        /// <summary>
        /// 加载门禁纯判定——不修改任何状态，通过后方可占用在途标记（<see cref="TryMarkOperation"/>）。
        /// </summary>
        internal ELoadGate CheckBeginLoad(string location, LoadSceneMode sceneMode)
        {
            if (_handlingScene.Contains(location))
            {
                return ELoadGate.InFlight;
            }

            if (sceneMode == LoadSceneMode.Additive)
            {
                if (_subScenes.ContainsKey(location))
                {
                    return ELoadGate.SubAlreadyRegistered;
                }

                // 守卫字段未设置时为空串，location 非空（入口已校验），直接比较安全
                if (location == _currentMainSceneLocation || location == _mainSceneLoadingLocation)
                {
                    return ELoadGate.RegisteredAsMain;
                }
            }
            else
            {
                if (_mainSceneLoadingHandle != null)
                {
                    return ELoadGate.MainLoadInFlight;
                }

                if (_subScenes.ContainsKey(location))
                {
                    return ELoadGate.RegisteredAsSub;
                }
            }

            return ELoadGate.Allow;
        }

        /// <summary>
        /// 占用 location 级在途标记。
        /// </summary>
        /// <returns>占用成功；已在途返回 <c>false</c>。</returns>
        internal bool TryMarkOperation(string location)
        {
            return _handlingScene.Add(location);
        }

        /// <summary>
        /// 释放 location 级在途标记。
        /// </summary>
        internal void UnmarkOperation(string location)
        {
            _handlingScene.Remove(location);
        }

        /// <summary>
        /// 查询 location 是否存在在途标记（后台收尾守卫：关闭流程清空登记后用于放弃过期收尾）。
        /// </summary>
        internal bool IsOperationMarked(string location)
        {
            return _handlingScene.Contains(location);
        }

        /// <summary>
        /// 登记主场景在途加载（Single 发起后调用）。
        /// </summary>
        internal void RegisterMainInFlight(string location, ResourceSceneHandle handle)
        {
            _mainSceneLoadingHandle = handle;
            _mainSceneLoadingLocation = location;
        }

        /// <summary>
        /// 前置登记子场景在途加载（Additive 发起后调用；挂起加载的场景在 UnSuspend 之后才会完成加载）。
        /// </summary>
        internal void RegisterSubInFlight(string location, ResourceSceneHandle handle)
        {
            _subScenes[location] = new SubSceneEntry(handle, string.Empty, ESubSceneState.Loading);
        }

        /// <summary>
        /// 完成子场景加载——Loading 迁移为 Loaded 并登记短名反向索引。
        /// </summary>
        /// <returns>短名索引被覆盖的前一个登记地址（同名碰撞时供调用方告警）；无碰撞返回 <c>null</c>。</returns>
        internal string CompleteSubLoad(string location, string sceneName)
        {
            // 在途标记由调用方持有至本调用结束，登记项必定存在（卸载门禁拒绝 Loading 项），缺失即逻辑错误，fail fast
            var handle = _subScenes[location].Handle;

            string overwritten = null;
            if (!string.IsNullOrEmpty(sceneName))
            {
                if (_subSceneNameIndex.TryGetValue(sceneName, out var existingLocation) && existingLocation != location)
                {
                    overwritten = existingLocation;
                }

                _subSceneNameIndex[sceneName] = location;
            }

            _subScenes[location] = new SubSceneEntry(handle, sceneName, ESubSceneState.Loaded);
            return overwritten;
        }

        /// <summary>
        /// 完成主场景加载——在途迁入已完成，替换当前主场景句柄。
        /// </summary>
        /// <returns>被替换的前一个主场景句柄（调用方负责释放引用计数）；无则返回 <c>null</c>。</returns>
        internal ResourceSceneHandle CompleteMainLoad(string location, string sceneName, ResourceSceneHandle handle)
        {
            AbandonLoad(location, LoadSceneMode.Single);

            var previousHandle = _mainSceneHandle;
            _mainSceneHandle = handle;
            _currentMainSceneLocation = location;
            _currentMainSceneName = sceneName;
            return previousHandle;
        }

        /// <summary>
        /// 放弃加载登记——主场景仅当在途字段仍指向本次加载时清空（避免误清后续主场景加载），子场景移除 Loading 项。
        /// </summary>
        internal void AbandonLoad(string location, LoadSceneMode sceneMode)
        {
            if (sceneMode == LoadSceneMode.Single)
            {
                if (_mainSceneLoadingHandle != null && location == _mainSceneLoadingLocation)
                {
                    _mainSceneLoadingHandle = null;
                    _mainSceneLoadingLocation = string.Empty;
                }
            }
            else
            {
                _subScenes.Remove(location);
            }
        }

        #endregion

        #region 卸载门禁与登记 [UNLOAD GATING & REGISTRATION]

        /// <summary>
        /// 卸载门禁——解析入参并校验登记状态，通过时占用在途标记（调用方须保证最终 <see cref="UnmarkOperation"/>）。
        /// </summary>
        internal EUnloadGate TryBeginUnload(string requestedLocation, out PendingUnload pending)
        {
            pending = default;

            var location = ResolveSubSceneLocation(requestedLocation);
            if (location == null || !_subScenes.TryGetValue(location, out var entry))
            {
                return EUnloadGate.NotRegistered;
            }

            // 预填 Location 供调用方在拒绝路径输出规范地址日志
            pending = new PendingUnload(location, entry.SceneName, entry.Handle);

            if (!_handlingScene.Add(location))
            {
                return EUnloadGate.InFlight;
            }

            if (entry.State != ESubSceneState.Loaded)
            {
                _handlingScene.Remove(location);
                return EUnloadGate.StillLoading;
            }

            return EUnloadGate.Allow;
        }

        /// <summary>
        /// 完成卸载——移除子场景登记；短名索引仅当仍指向本地址时移除（同名碰撞时后注册者可能已覆盖索引）。
        /// </summary>
        /// <returns>是否移除了登记项。</returns>
        internal bool CompleteUnload(string location, string sceneName)
        {
            if (!_subScenes.Remove(location))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(sceneName) &&
                _subSceneNameIndex.TryGetValue(sceneName, out var mappedLocation) && mappedLocation == location)
            {
                _subSceneNameIndex.Remove(sceneName);
            }

            return true;
        }

        #endregion

        #region 查询 [QUERY]

        /// <summary>
        /// 按登记地址直查子场景（不经短名索引解析）。
        /// </summary>
        internal bool TryGetSubScene(string location, out SubSceneEntry entry)
        {
            return _subScenes.TryGetValue(location, out entry);
        }

        /// <summary>
        /// 解析入参（地址或短名）并查询已加载（Loaded）子场景。
        /// </summary>
        internal bool TryGetLoadedSubScene(string locationOrName, out SubSceneEntry entry)
        {
            entry = default;
            var location = ResolveSubSceneLocation(locationOrName);
            if (location == null || !_subScenes.TryGetValue(location, out entry))
            {
                return false;
            }

            return entry.State == ESubSceneState.Loaded;
        }

        /// <summary>
        /// 将入参解析为子场景登记地址——直接命中登记键，否则经场景短名反向索引解析。
        /// </summary>
        /// <returns>登记地址；未命中返回 <c>null</c>。</returns>
        internal string ResolveSubSceneLocation(string locationOrName)
        {
            if (string.IsNullOrEmpty(locationOrName))
            {
                return null;
            }

            if (_subScenes.ContainsKey(locationOrName))
            {
                return locationOrName;
            }

            return _subSceneNameIndex.TryGetValue(locationOrName, out var location) ? location : null;
        }

        /// <summary>
        /// 经短名反向索引查询子场景登记地址（主场景短名碰撞告警用）。
        /// </summary>
        internal bool TryGetSubLocationByName(string sceneName, out string location)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                location = null;
                return false;
            }

            return _subSceneNameIndex.TryGetValue(sceneName, out location);
        }

        /// <summary>
        /// 查询场景是否已登记（主场景含启动场景，子场景含加载中未完成的挂起加载）。
        /// </summary>
        internal bool IsContainScene(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return false;
            }

            if (location == _currentMainSceneName || location == _currentMainSceneLocation)
            {
                return true;
            }

            // 主场景在途加载也视为已登记
            if (_mainSceneLoadingHandle != null && location == _mainSceneLoadingLocation)
            {
                return true;
            }

            return _subScenes.ContainsKey(location) || _subSceneNameIndex.ContainsKey(location);
        }

        /// <summary>
        /// 判断指定场景是否为当前主场景（身份判断，不含激活状态）。
        /// </summary>
        internal bool IsMainScene(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return false;
            }

            return location == _currentMainSceneName || location == _currentMainSceneLocation;
        }

        /// <summary>
        /// 已完成加载的子场景资源地址快照（不含加载中的子场景）。
        /// </summary>
        internal List<string> SnapshotLoadedSubScenes()
        {
            var result = new List<string>(_subScenes.Count);
            foreach (var pair in _subScenes)
            {
                if (pair.Value.State == ESubSceneState.Loaded)
                {
                    result.Add(pair.Key);
                }
            }

            return result;
        }

        #endregion

        /// <summary>
        /// 关闭排空——取出全部待处置句柄并重置登记簿。每个句柄只从唯一登记处取出一次。
        /// </summary>
        /// <param name="mainLoadingHandle">主场景在途句柄（调用方负责释放）。</param>
        /// <param name="mainHandle">主场景已完成句柄（调用方负责释放）。</param>
        /// <returns>全部子场景登记项快照（调用方按状态决定卸载或释放）。</returns>
        internal List<SubSceneEntry> Shutdown(out ResourceSceneHandle mainLoadingHandle, out ResourceSceneHandle mainHandle)
        {
            mainLoadingHandle = _mainSceneLoadingHandle;
            mainHandle = _mainSceneHandle;

            var subScenes = new List<SubSceneEntry>(_subScenes.Values);

            _mainSceneLoadingHandle = null;
            _mainSceneLoadingLocation = string.Empty;
            _mainSceneHandle = null;
            _currentMainSceneName = string.Empty;
            _currentMainSceneLocation = string.Empty;
            _subScenes.Clear();
            _subSceneNameIndex.Clear();
            _handlingScene.Clear();

            return subScenes;
        }
    }
}
