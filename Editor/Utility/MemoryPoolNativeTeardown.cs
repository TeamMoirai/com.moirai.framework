using Moirai.Atropos;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 内存池非托管元数据的重载期收口。
    /// <para>页头与槽位元数据走 <c>Marshal.AllocHGlobal</c>，属进程堆；静态字段只活在当前域里，而 Unity
    /// 编辑器脚本重载并不触发 <c>AppDomain.DomainUnload</c>——静态字段一复位，那些指针就永久失联，
    /// 每热重载一次就漏一份，编辑器长时间开着会一直见顶不退。这里在重载与退出前主动收口。</para>
    /// </summary>
    internal static class MemoryPoolNativeTeardown
    {
        [InitializeOnLoadMethod]
        private static void Install()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnQuitting;
            EditorApplication.quitting += OnQuitting;
        }

        private static void OnBeforeAssemblyReload()
        {
            // 有对象在外时不回收：把元数据释放掉之后，那次归还会往已释放内存里写。
            // 这份泄漏就留给本次会话——热重载时业务代码本来就会丢引用，比制造野指针划算。
            if (MemoryPoolRegistry.TryReleaseAllNativeMetadataForTeardown())
            {
                return;
            }

            Debug.LogWarning("[MemoryPool] 脚本重载前仍有内存对象未归还，非托管页元数据本次不回收（会随本次编辑器会话驻留）。" +
                             "确认取还配对，或先 MemoryPool.ClearAll() 再重载。");
        }

        private static void OnQuitting()
        {
            MemoryPoolRegistry.TryReleaseAllNativeMetadataForTeardown();
        }
    }
}
