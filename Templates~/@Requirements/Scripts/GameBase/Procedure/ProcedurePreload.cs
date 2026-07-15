using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.FSM;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Resource;
using UnityEngine;
using YooAsset;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 预加载
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedurePreload : ProcedurePremainBase
    {
        public override bool UseNativeDialog => true;

        private IFSM<IProcedureModule> _procedureOwner;

        // 预加载开关
        private readonly bool _preloadSwitch = true;

        private readonly Dictionary<string, bool> _loadedFlag = new Dictionary<string, bool>();

        // 无预加载时的假加载进度
        private float _fakeProgress = 0f;

        /// <summary>
        /// 预加载回调。
        /// </summary>
        private LoadAssetCallbacks _preLoadAssetCallbacks;
        
        protected override void OnInit(IFSM<IProcedureModule> procedureOwner)
        {
            base.OnInit(procedureOwner);
            _procedureOwner = procedureOwner;
            _preLoadAssetCallbacks = new LoadAssetCallbacks(OnPreLoadAssetSuccess, OnPreLoadAssetFailure);
        }
        
        protected override void OnEnter(IFSM<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            _loadedFlag.Clear();

            LauncherMgr.ShowUI<LoadUpdateUI>(StringUtility.Format(LoadText.Instance.Label_Load_Load_Progress, 0));
            LauncherMgr.RefreshVersion(VersionUtility.GameVersion, VersionUtility.ResourceVersion);
            
            PreloadResources();
        }

        protected override void OnUpdate(IFSM<IProcedureModule> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            LauncherMgr.RefreshProgress(_fakeProgress);

            float progress = _fakeProgress;
            if (_loadedFlag.Count != 0)
            {
                var totalCount = _loadedFlag.Count <= 0 ? 1 : _loadedFlag.Count;

                var loadCount = _loadedFlag.Count <= 0 ? 1 : 0;

                foreach (KeyValuePair<string, bool> loadedFlag in _loadedFlag)
                {
                    if (!loadedFlag.Value) break;
                    loadCount++;
                }

                // 假进度 90% + 加载进度 10%
                progress = (progress - 0.1f) + (float)loadCount/totalCount/10f;
            }

            bool isComplete = _loadedFlag.Count > 0 && Math.Abs(progress - 1f) < 0.001f;
            if (!isComplete)
            {
                string progressStr = $"{progress * 100:f1}";
                LauncherMgr.ShowUI<LoadUpdateUI>(StringUtility.Format(LoadText.Instance.Label_Load_Load_Progress, progressStr));
                return;
            }

            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Load_Complete);
            ChangeState<ProcedureLoadAssembly>(procedureOwner);
        }

        private void PreloadResources()
        {
            if (!_preloadSwitch) return;

            AssetInfo[] assetInfos = _resourceModule.GetAssetInfos("PRELOAD");
            foreach (var assetInfo in assetInfos)
            {
                PreLoad(assetInfo.AssetPath);
            }
#if UNITY_WEBGL
            AssetInfo[] webAssetInfos = _resourceModule.GetAssetInfos("WEBGL_PRELOAD");
            foreach (var assetInfo in webAssetInfos)
            {
                PreLoad(assetInfo.AssetPath);
            }
#endif

            // 假进度
            SmoothValue(1, 0.85f).Forget();

        }

        private void PreLoad(string location)
        {
            _loadedFlag.Add(location, false);
            _resourceModule.LoadAssetAsync(location, 100, _preLoadAssetCallbacks, null);
        }

        private void OnPreLoadAssetFailure(string assetName, LoadResourceStatus status, string errorMessage, object userdata)
        {
            Log.Warning("Can not preload asset from '{0}' with error message '{1}'.", assetName, errorMessage);
            _loadedFlag[assetName] = true;
        }

        private void OnPreLoadAssetSuccess(string assetName, object asset, float duration, object userdata)
        {
            Log.Debug("Success preload asset from '{0}' duration '{1}'.", assetName, duration);
            _loadedFlag[assetName] = true;
        }

        private async UniTaskVoid SmoothValue(float value, float duration, Action callback = null)
        {
            float time = 0f;
            while (time < duration)
            {
                time += Time.deltaTime;
                _fakeProgress = Mathf.Lerp(0, value, time / duration);
                await UniTask.Yield();
            }

            _fakeProgress = value;
            callback?.Invoke();
        }

        private void ChangeProcedureToLoadAssembly()
        {
            ChangeState<ProcedureLoadAssembly>(_procedureOwner);
        }
    }
}