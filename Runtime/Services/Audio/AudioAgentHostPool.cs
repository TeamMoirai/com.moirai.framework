using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// AudioSource 宿主对象池——始终使用内部栈池复用运行时创建的空 GameObject + AudioSource。
    /// <para>扩展通道时不反复 <c>new GameObject</c>/<c>Destroy</c>；归还即失活入栈。</para>
    /// <para>闲置宿主统一挂在 <c>[Warmup]</c> 节点下，与各音轨 Category 实例区分。</para>
    /// </summary>
    internal static class AudioAgentHostPool
    {
        private static Transform s_PoolRoot;
        private const string POOL_ROOT_NAME = "[Warmup]";

        private static readonly Stack<AudioSource> s_Stack = new Stack<AudioSource>(16);
        /// <summary>
        /// 当前栈池缓存数量（诊断用）。
        /// </summary>
        public static int StackCount => s_Stack.Count;
        
        /// <summary>
        /// 预热内部栈池（闲置宿主置于 <c>[Warmup]</c> 下）。
        /// </summary>
        public static void Warmup(Transform parent, int count)
        {
            EnsurePoolRoot(parent);
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                var source = CreateHost(s_PoolRoot);
                source.gameObject.SetActive(false);
                s_Stack.Push(source);
            }
        }

        /// <summary>
        /// 获取一个 AudioSource 宿主（挂到 parent 下，命名为 name）。
        /// </summary>
        public static AudioSource Acquire(Transform parent, string name)
        {
            AudioSource pooled = null;
            while (s_Stack.Count > 0)
            {
                pooled = s_Stack.Pop();
                if (pooled != null) break;
            }

            if (pooled == null)
            {
                pooled = CreateHost(parent);
            }
            else
            {
                pooled.transform.SetParent(parent, false);
                pooled.transform.localPosition = Vector3.zero;
                pooled.gameObject.SetActive(true);
            }

            pooled.name = name;
            pooled.playOnAwake = false;
            pooled.Stop();
            pooled.clip = null;
            pooled.volume = 1f;
            pooled.loop = false;
            pooled.mute = false;

            // 复位遮挡低通：AudioOcclusionHrtf 挂载的滤镜不会随播放结束移除，
            // 不复位会让复用宿主继承上一次的截止频率（起播瞬间的闷声毛刺）
            var lowPass = pooled.GetComponent<AudioLowPassFilter>();
            if (lowPass != null)
            {
                lowPass.enabled = false;
                lowPass.cutoffFrequency = 22000f;
            }

            return pooled;
        }

        /// <summary>
        /// 归还宿主（失活入栈，挂回 <c>[Warmup]</c>）。
        /// </summary>
        public static void Release(AudioSource source)
        {
            if (source == null) return;

            source.Stop();
            source.clip = null;

            // 未预热时按需建根：Category.InstanceRoot 的父级即 Handler.InstanceRoot
            if (s_PoolRoot == null)
            {
                var current = source.transform.parent;
                EnsurePoolRoot(current != null ? current.parent : null);
            }

            source.transform.SetParent(s_PoolRoot, false);
            source.transform.localPosition = Vector3.zero;
            source.gameObject.SetActive(false);
            s_Stack.Push(source);
        }

        /// <summary>
        /// 清空栈池并销毁全部缓存宿主（含 <c>[Warmup]</c> 根节点）。
        /// </summary>
        public static void Clear()
        {
            while (s_Stack.Count > 0)
            {
                var source = s_Stack.Pop();
                if (source != null)
                {
                    Object.Destroy(source.gameObject);
                }
            }

            if (s_PoolRoot != null)
            {
                Object.Destroy(s_PoolRoot.gameObject);
                s_PoolRoot = null;
            }
        }

        private static void EnsurePoolRoot(Transform parent)
        {
            if (s_PoolRoot != null) return;

            var go = new GameObject(POOL_ROOT_NAME);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            s_PoolRoot = go.transform;
        }

        private static AudioSource CreateHost(Transform parent)
        {
            var go = new GameObject("AudioSourceHost");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
            }

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }
    }
}
