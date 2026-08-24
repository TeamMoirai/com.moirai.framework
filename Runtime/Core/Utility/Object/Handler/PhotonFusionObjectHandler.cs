#if FUSION2
using Fusion;
using UnityEngine.SceneManagement;
#endif
using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 用来在联网项目中代替常规的 Instantiate 和 Destroy 方法。
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    [Serializable]
    public sealed class PhotonFusionObjectHandler : ObjectHandler
    {

        #region 变量 [VARIABLES]

#if FUSION2
        private Dictionary<Object, NetworkObject> _spawns;
#endif

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        public override T InstantiateObject<T>(T original, bool playerOwned = false, bool allowNetworked = true)
        {
            return (T)Instantiate(original, Vector3.zero, Quaternion.identity, null, allowNetworked);
        }

        public override T InstantiateObject<T>(T original, Transform parent, bool playerOwned = false, bool allowNetworked = true)
        {
            return (T)Instantiate(original, Vector3.zero, Quaternion.identity, parent, allowNetworked);
        }

        public override T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, bool playerOwned = false, bool allowNetworked = true)
        {
            return (T)Instantiate(original, position, rotation, null, allowNetworked);
        }

        public override T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, Transform parent, bool playerOwned = false, bool allowNetworked = true)
        {
            return (T)Instantiate(original, position, rotation, parent, allowNetworked);
        }

        public override void DestroyObject(UnityEngine.Object target, bool allowNetworked = true)
        {
#if FUSION2
           if (allowNetworked)
            {
                if(_spawns.ContainsKey(target))
                {
                    NetworkRunner runner = NetworkRunner.GetRunnerForScene(SceneManager.GetActiveScene());
                    runner.Despawn(_spawns[target]);
                    _spawns.Remove(target);
                    return;
                }
            }
#endif

            UnityEngine.Object.Destroy(target);
        }

#if FUSION2
        public NetworkObject GetSpawnNetworkObject(Object target)
        {
            if(_spawns.ContainsKey(target))
            {
                return _spawns[target];
            }

            return null;
        }
#endif

        #endregion

        #region 私有方法 [PRIVATE METHODS]

#if FUSION2

        private Object AddEntry(NetworkObject networkObject)
        {
            if (_spawns == null) _spawns = new Dictionary<Object, NetworkObject>();

            _spawns.Add(networkObject.gameObject, networkObject);

            return networkObject.gameObject;
        }

#endif

        private UnityEngine.Object Instantiate(UnityEngine.Object original, Vector3 position, Quaternion rotation, Transform parent, bool playerOwned = false, bool allowNetworked = true)
        {
            
#if FUSION2
            if (allowNetworked)
            {
                if (original is GameObject gameObject)
                {
                    NetworkRunner runner = NetworkRunner.GetRunnerForScene(SceneManager.GetActiveScene());
                    PlayerRef? owner = playerOwned ? runner.LocalPlayer : null;
                    GameObject go = (GameObject)AddEntry(runner.Spawn(gameObject, position: position, rotation: rotation, inputAuthority: owner));
                    if (parent != null)
                    {
                        go.transform.SetParent(parent);
                        go.transform.localPosition = position;
                    }
                    return go;
                }
            }
#endif

            return UnityEngine.Object.Instantiate(original, position, rotation, parent);
        }

        #endregion

    }
}
