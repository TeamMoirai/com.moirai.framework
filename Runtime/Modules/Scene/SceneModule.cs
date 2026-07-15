using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine.SceneManagement;
using YooAsset;
using SceneHandle = YooAsset.SceneHandle;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景管理模块。
    /// </summary>
    public sealed class SceneModule : Module, ISceneModule
    {
        private string _currentMainSceneName = string.Empty;

        private SceneHandle _currentMainScene;

        private readonly Dictionary<string, SceneHandle> _subScenes = new Dictionary<string, SceneHandle>();
        
        private readonly HashSet<string> _handlingScene = new HashSet<string>();

        public string CurrentMainSceneName => _currentMainSceneName;
        
        public override void OnInit()
        {
            _currentMainScene = null;
            _currentMainSceneName = SceneManager.GetSceneByBuildIndex(0).name;
        }

        public override void Shutdown()
        {
            var iter = _subScenes.Values.GetEnumerator();
            while (iter.MoveNext())
            {
                SceneHandle subScene = iter.Current;
                if (subScene != null)
                {
                    subScene.UnloadAsync();
                }
            }

            iter.Dispose();
            _subScenes.Clear();
            _handlingScene.Clear();
            _currentMainSceneName = string.Empty;
        }

        public async UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            bool gcCollect = true, Action<float> progressCallBack = null)
        {
            if (!_handlingScene.Add(location))
            {
                Log.Error("Could not load scene while loading. Scene: {0}", location);
                return default;
            }

            if (sceneMode == LoadSceneMode.Additive)
            {
                if (_subScenes.TryGetValue(location, out SceneHandle subScene))
                {
                    throw new GameException($"Could not load subScene while already loaded. Scene: {location}");
                }

                subScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

                // Fix 这里前置，subScene.IsDone 在 UnSuspend 之后才会是true
                _subScenes.Add(location, subScene);

                if (progressCallBack != null)
                {
                    while (!subScene.IsDone && subScene.IsValid)
                    {
                        progressCallBack.Invoke(subScene.Progress);
                        await UniTask.Yield();
                    }
                }
                else
                {
                    await subScene.ToUniTask();
                }

                _handlingScene.Remove(location);

                return subScene.SceneObject;
            }
            else
            {
                if (_currentMainSceneName == location && _currentMainScene is { IsDone: false })
                {
                    throw new GameException($"Could not load MainScene while loading. CurrentMainScene: {_currentMainSceneName}.");
                }

                _currentMainSceneName = location;

                _currentMainScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

                if (progressCallBack != null)
                {
                    while (!_currentMainScene.IsDone && _currentMainScene.IsValid)
                    {
                        progressCallBack.Invoke(_currentMainScene.Progress);
                        await UniTask.Yield();
                    }
                }
                else
                {
                    await _currentMainScene.ToUniTask();
                }

#if UNITY_EDITOR && EditorFixedMaterialShader
                MaterialUtility.WaitGetRootGameObjects(_currentMainScene).Forget();
#endif

                ModuleSystem.GetModule<IResourceModule>().ForceUnloadUnusedAssets(gcCollect);

                _handlingScene.Remove(location);

                return _currentMainScene.SceneObject;
            }
        }

        public void LoadScene(string location, string packageName = "", LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false, uint priority = 100, bool gcCollect = true, Action<UnityEngine.SceneManagement.Scene> callBack = null, Action<float> progressCallBack = null)
        {
            if (!_handlingScene.Add(location))
            {
                Log.Error("Could not load scene while loading. Scene: {0}", location);
                return;
            }
            
            if (sceneMode == LoadSceneMode.Additive)
            {
                if (_subScenes.TryGetValue(location, out SceneHandle subScene))
                {
                    throw new GameException($"Could not load subScene while already loaded. Scene: {location}");
                }

                if (string.IsNullOrEmpty(packageName))
                {
                    subScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
                }
                else
                {
                    var package = YooAssets.GetPackage(packageName);
                    subScene = package.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
                }

                subScene.Completed += handle =>
                {
                    _handlingScene.Remove(location);
                    callBack?.Invoke(handle.SceneObject);
                };

                if (progressCallBack != null)
                {
                    InvokeProgress(subScene, progressCallBack).Forget();
                }

                _subScenes.Add(location, subScene);
            }
            else
            {
                if (_currentMainSceneName == location && _currentMainScene is { IsDone: false })
                {
                    throw new GameException($"Could not load MainScene while loading. CurrentMainScene: {_currentMainSceneName}.");
                }

                _currentMainSceneName = location;

                if (string.IsNullOrEmpty(packageName))
                {
                    _currentMainScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
                }
                else
                {
                    var package = YooAssets.GetPackage(packageName);
                    _currentMainScene = package.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
                }

                _currentMainScene.Completed += handle =>
                {
                    _handlingScene.Remove(location);
                    callBack?.Invoke(handle.SceneObject);
                };

                if (progressCallBack != null)
                {
                    InvokeProgress(_currentMainScene, progressCallBack).Forget();
                }

#if UNITY_EDITOR && EditorFixedMaterialShader
                MaterialUtility.WaitGetRootGameObjects(_currentMainScene).Forget();
#endif

                ModuleSystem.GetModule<IResourceModule>().ForceUnloadUnusedAssets(gcCollect);
            }
        }

        private async UniTaskVoid InvokeProgress(SceneHandle sceneHandle, Action<float> progress)
        {
            if (sceneHandle == null)
            {
                return;
            }

            while (!sceneHandle.IsDone && sceneHandle.IsValid)
            {
                await UniTask.Yield();

                progress?.Invoke(sceneHandle.Progress);
            }
        }
        
        public bool ActivateScene(string location)
        {
            if (_currentMainSceneName.Equals(location))
            {
                if (_currentMainScene != null)
                {
                    return _currentMainScene.ActivateScene();
                }

                return false;
            }

            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                return subScene.ActivateScene();
            }

            Log.Warning("IsMainScene invalid location:{0}", location);
            return false;
        }

        public bool UnSuspend(string location)
        {
            if (_currentMainSceneName.Equals(location))
            {
                if (_currentMainScene != null)
                {
                    return _currentMainScene.UnSuspend();
                }

                return false;
            }

            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                return subScene.UnSuspend();
            }

            Log.Warning("IsMainScene invalid location:{0}", location);
            return false;
        }

        public bool IsMainScene(string location)
        {
            // 获取当前激活的场景  
            UnityEngine.SceneManagement.Scene currentScene = SceneManager.GetActiveScene();  
            
            if (_currentMainSceneName.Equals(location))
            {
                if (_currentMainScene == null)
                {
                    return false;
                }
                // 判断当前场景是否是主场景  
                if (currentScene.name == _currentMainScene.SceneName)
                {
                    return true;
                }
                    
                return _currentMainScene.SceneName == currentScene.name;
            }

            // 判断当前场景是否是主场景  
            if (currentScene.name == _currentMainScene?.SceneName)
            {
                return true;
            }

            Log.Warning("IsMainScene invalid location:{0}", location);
            return false;
        }
        
        public async UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null)
        {
            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                if (subScene.SceneObject == default)
                {
                    Log.Error("Could not unload Scene while not loaded. Scene: {0}", location);
                    return false;
                }

                if (!_handlingScene.Add(location))
                {
                    Log.Warning("Could not unload Scene while loading. Scene: {0}", location);
                    return false;
                }

                var unloadOperation = subScene.UnloadAsync();

                if (progressCallBack != null)
                {
                    while (!unloadOperation.IsDone && unloadOperation.Status != EOperationStatus.Failed)
                    {
                        progressCallBack.Invoke(unloadOperation.Progress);
                        await UniTask.Yield();
                    }
                }
                else
                {
                    await unloadOperation.ToUniTask();
                }
                
                _subScenes.Remove(location);
                
                _handlingScene.Remove(location);

                return true;
            }

            Log.Warning("UnloadAsync invalid location:{0}", location);
            return false;
        }
        
        public void Unload(string location, Action callBack = null, Action<float> progressCallBack = null)
        {
            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                if (subScene.SceneObject == default)
                {
                    Log.Error("Could not unload Scene while not loaded. Scene: {0}", location);
                    return;
                }
                
                if (!_handlingScene.Add(location))
                {
                    Log.Warning("Could not unload Scene while loading. Scene: {0}", location);
                    return;
                }

                subScene.UnloadAsync();
                subScene.UnloadAsync().Completed += @base =>
                {
                    _subScenes.Remove(location);
                    _handlingScene.Remove(location);
                    callBack?.Invoke();
                };

                if (progressCallBack != null)
                {
                    InvokeProgress(subScene, progressCallBack).Forget();
                }
                
                return;
            }

            Log.Warning("UnloadAsync invalid location:{0}", location);
        }
        
        public bool IsContainScene(string location)
        {
            if (_currentMainSceneName.Equals(location))
            {
                return true;
            }

            return _subScenes.TryGetValue(location, out var _);
        }
    }
}