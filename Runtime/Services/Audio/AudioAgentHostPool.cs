using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// AudioSource 宿主对象池——始终使用内部栈池复用运行时创建的空 GameObject + AudioSource。
    /// <para>扩展通道时不反复 <c>new GameObject</c>/<c>Destroy</c>；归还即失活入栈。</para>
    /// </summary>
    internal static class AudioAgentHostPool
    {
        private static readonly Stack<AudioSource> s_Stack = new Stack<AudioSource>(16);

        /// <summary>
        /// 预热内部栈池。
        /// </summary>
        public static void Warmup(Transform parent, int count)
        {
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                var source = CreateHost(parent);
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
            return pooled;
        }

        /// <summary>
        /// 归还宿主（失活入栈）。
        /// </summary>
        public static void Release(AudioSource source)
        {
            if (source == null) return;

            source.Stop();
            source.clip = null;
            source.gameObject.SetActive(false);
            s_Stack.Push(source);
        }

        /// <summary>
        /// 清空栈池并销毁全部缓存宿主。
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
        }

        /// <summary>
        /// 当前栈池缓存数量（诊断用）。
        /// </summary>
        public static int StackCount => s_Stack.Count;

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
