using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Attributes;
using Moirai.Atropos.Events;
using Sirenix.OdinInspector;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 预加载游戏对象
    /// </summary>
    /// <remarks>比如一些持久化对象</remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class GameObjectPreLoader : MonoBehaviour
    {
        [InfoBox("等内置流程跑完后，再加载必要的游戏对象")]
        [ResourcePath(EStr.AssetDatabase, typeof(GameObject))]
        [SerializeField] private string[] m_PreLoadPrefabs;

        private void Awake()
        {
            EventManager.RegisterCallback<HotfixEntryEvent>(OnHotfixEntryEvent);

            // 防止未加载完就切换场景
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            EventManager.UnregisterCallback<HotfixEntryEvent>(OnHotfixEntryEvent);
        }
        
        /// <summary>
        /// 进入游戏主流程 => 加载持久化对象
        /// </summary>
        /// <param name="evt"></param>
        /// <remarks>依赖模块，所以必须进入 <see cref="HotfixEntry"/> 后再实例化</remarks>
        private void OnHotfixEntryEvent(HotfixEntryEvent evt)
        {
            if (m_PreLoadPrefabs.Length == 0) return;
            if (GameModule.Resource == null) return;

            Load().Forget();
        }

        /// <summary>
        /// 分帧加载预制体
        /// </summary>
        private async UniTaskVoid Load()
        {
            for (int i = 0; i < m_PreLoadPrefabs.Length; i++)
            {
                if (m_PreLoadPrefabs[i] == null)
                {
                    Log.Warning($"[{nameof(GameObjectPreLoader)}] {i} is null");
                    continue;
                }

                var go = await GameModule.Resource.LoadAssetAsync<GameObject>(m_PreLoadPrefabs[i]);
                var obj = Instantiate(go);
                obj.name = go.name;
            }
        }
    }
}