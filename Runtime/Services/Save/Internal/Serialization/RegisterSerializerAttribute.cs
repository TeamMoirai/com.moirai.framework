using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 序列化后端登记特性：标注于 <see cref="ISaveSerializer"/> 实现类，由 SaveServiceCodegen 源生成器写进模块初始化器完成自注册。
    /// </summary>
    /// <remarks>
    /// 特性不带编号：后端标识的唯一真源是实现自述的 <see cref="ISaveSerializer.BackendId"/>，且须写成编译期常量表达式 <br />
    /// （项目自定义后端 <c>=&gt; 1000</c>；内置那三个二进制后端外置到 plugin 分支后按契约面别名引用，如 <c>=&gt; ISaveSerializer.MEMORY_PACK</c>）——生成器据此静态判重与判占号冲突。 <br />
    /// 契约面别名见 <see cref="ISaveSerializer.MESSAGE_PACK"/> 一族：数值真源仍是框架内部表 <see cref="SaveBackendIds"/>， <br />
    /// 内部表本身不外露（<c>internal</c>），也不给插件放行 <c>InternalsVisibleTo</c>。 <br />
    /// 框架内置实现同样走本特性：注册表不再硬编码任何后端，内置与项目在模块初始化期同形登记（只挂 ID→类型，首次查询到该后端才实例化）。 <br />
    /// 形状非法、编号非常量、与框架占号相撞、同编译单元内重复一律报 MOIRAI309~312 错误；跨程序集撞号生成器看不见，运行期由 <see cref="SaveSerializerRegistry"/> 记 Fatal 并保留先到那份—— <br />
    /// 声明式登记跑在模块初始化期，在那里抛出会连累整个编辑器（<c>TypeInitializationException</c> 实测让 Unity 在源生成扫描中原生崩溃），故该路径刻意不抛。
    /// </remarks>
    /// <example>
    /// <code lang="csharp">
    /// [RegisterSerializer]
    /// public sealed class MyCompressedSerializer : ISaveSerializer
    /// {
    ///     public ushort BackendId =&gt; 1000;
    ///     public byte[] Serialize&lt;T&gt;(T data) =&gt; /* ... */;
    ///     public T Deserialize&lt;T&gt;(byte[] bytes) =&gt; /* ... */;
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RegisterSerializerAttribute : Attribute
    {
    }
}
