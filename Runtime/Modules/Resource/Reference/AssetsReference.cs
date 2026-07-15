using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    [Serializable]
    public struct AssetsRefInfo
    {
#if UNITY_6000_1_OR_NEWER
        public readonly EntityId instanceID;
#else
        public readonly int instanceID;
#endif

        public Object refAsset;

        public AssetsRefInfo(Object refAsset)
        {
            this.refAsset = refAsset;

#if UNITY_6000_1_OR_NEWER
            instanceID = refAsset.GetEntityId();
#else
            instanceID = refAsset.GetInstanceID();
#endif
        }
    }

    [DisallowMultipleComponent]
    public sealed class AssetsReference : MonoBehaviour
    {
        [SerializeField] private GameObject m_SourceGameObject;

        [SerializeField] private List<AssetsRefInfo> m_RefAssetInfoList;

        private static IResourceModule _resourceModule;

        private static Dictionary<GameObject, AssetsReference> _originalRefs = new Dictionary<GameObject, AssetsReference>();


        private void CheckInit()
        {
            if (_resourceModule != null)
            {
                return;
            }
            else
            {
                _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            }

            if (_resourceModule == null)
            {
                throw new GameException($"ResourceModule is null.");
            }
        }

        private void CheckRelease()
        {
            if (m_SourceGameObject != null)
            {
                _resourceModule.UnloadAsset(m_SourceGameObject);
            }
            else
            {
                Log.Warning($"SourceGameObject is not invalid.");
            }
        }


        private void Awake()
        {
            // 如果是克隆操作，需在克隆前清除引用记录
            if (!IsOriginalInstance())
            {
                ClearCloneReferences();
            }
        }

        private bool IsOriginalInstance()
        {
            return _originalRefs.TryGetValue(gameObject, out var originalComponent) &&
                   originalComponent == this;
        }

        private void ClearCloneReferences()
        {
            m_SourceGameObject = null;
            m_RefAssetInfoList?.Clear();
        }

        private void OnDestroy()
        {
            if (_originalRefs.TryGetValue(gameObject, out var reference) && reference == this)
            {
                _originalRefs.Remove(gameObject);
            }
            CheckInit();
            if (m_SourceGameObject != null)
            {
                CheckRelease();
            }

            ReleaseRefAssetInfoList();
        }

        private void ReleaseRefAssetInfoList()
        {
            if (m_RefAssetInfoList != null)
            {
                foreach (var refInfo in m_RefAssetInfoList)
                {
                    _resourceModule.UnloadAsset(refInfo.refAsset);
                }

                m_RefAssetInfoList.Clear();
            }
        }

        public AssetsReference Ref(GameObject source, IResourceModule resourceModule = null)
        {
            if (source == null)
            {
                throw new GameException($"Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameException($"Source gameObject is in scene.");
            }

            _resourceModule = resourceModule;
            m_SourceGameObject = source;

            _originalRefs[gameObject] = this;

            return this;
        }

        public AssetsReference Ref<T>(T source, IResourceModule resourceModule = null) where T : Object
        {
            if (source == null)
            {
                throw new GameException($"Source gameObject is null.");
            }

            _resourceModule = resourceModule;
            if (m_RefAssetInfoList == null)
            {
                m_RefAssetInfoList = new List<AssetsRefInfo>();
            }

            m_RefAssetInfoList.Add(new AssetsRefInfo(source));
            return this;
        }

        internal static AssetsReference Instantiate(GameObject source, Transform parent = null, IResourceModule resourceModule = null)
        {
            if (source == null)
            {
                throw new GameException($"Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameException($"Source gameObject is in scene.");
            }

            GameObject instance = Object.Instantiate(source, parent);
            return instance.AddComponent<AssetsReference>().Ref(source, resourceModule);
        }

        public static AssetsReference Ref(GameObject source, GameObject instance, IResourceModule resourceModule = null)
        {
            if (source == null)
            {
                throw new GameException($"Source gameObject is null.");
            }

            if (source.scene.name != null)
            {
                throw new GameException($"Source gameObject is in scene.");
            }

            var comp = instance.GetComponent<AssetsReference>();
            return comp ? comp.Ref(source, resourceModule) : instance.AddComponent<AssetsReference>().Ref(source, resourceModule);
        }

        public static AssetsReference Ref<T>(T source, GameObject instance, IResourceModule resourceModule = null) where T : Object
        {
            if (source == null)
            {
                throw new GameException($"Source gameObject is null.");
            }

            var comp = instance.GetComponent<AssetsReference>();
            return comp ? comp.Ref(source, resourceModule) : instance.AddComponent<AssetsReference>().Ref(source, resourceModule);
        }
    }
}