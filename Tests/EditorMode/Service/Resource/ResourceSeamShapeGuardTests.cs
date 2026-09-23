using System;
using System.Reflection;
using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Service.Resource
{
    /// <summary>
    /// 后端接缝形状与处理器引用的记账用例。
    /// <para>本组不验行为，只把"当前有多少成员、其中多少个是 internal abstract、多少个还挂着
    /// [Obsolete]"钉成基线。后续重构会一项项往下削，削每一笔都必须在这里以 diff 的形式显形——
    /// 否则"顺手少个成员"和"引用悄悄对不上"在同一次改动里没人分得清。</para>
    /// </summary>
    public sealed class ResourceSeamShapeGuardTests
    {
        // 2026-09-23 基线：20 个抽象属性 + 54 个抽象方法；其中 11 个 internal abstract；6 个 [Obsolete]。
        private const int BaselineAbstractMembers = 74;
        private const int BaselineInternalAbstractMembers = 11;
        private const int BaselineObsoleteMembers = 6;

        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>
        /// 抽象成员总数停在基线上。
        /// </summary>
        [Test]
        public void Seam_AbstractMemberCount_MatchesRecordedBaseline()
        {
            Assert.AreEqual(BaselineAbstractMembers, CountAbstractMembers(),
                "后端接缝的抽象成员数变了。若这是有意收敛，同步改基线常数并在 CHANGELOG 写明收掉了哪些。");
        }

        /// <summary>
        /// internal abstract 成员数停在基线上。
        /// <para>这一项是"可插拔后端"名不副实的根因：抽象基类带 internal abstract 成员时，
        /// 程序集外的派生类既看不见也落不下，接缝只能在框架内实现。收口方向是把这些成员
        /// 移到一条 internal 的租约接缝上，届时本基线应降到 0。</para>
        /// </summary>
        [Test]
        public void Seam_InternalAbstractCount_MatchesRecordedBaseline()
        {
            Assert.AreEqual(BaselineInternalAbstractMembers, CountInternalAbstractMembers(),
                "internal abstract 接缝成员数变了。新增即等于把后端实现权再往程序集内收一格，要有理由。");
        }

        /// <summary>
        /// 仍挂着 [Obsolete] 的成员数停在基线上。
        /// </summary>
        [Test]
        public void Seam_ObsoleteMemberCount_MatchesRecordedBaseline()
        {
            Assert.AreEqual(BaselineObsoleteMembers, CountObsoleteMembers(),
                "遗留加载 API 的成员数变了。删除时一并更新基线，别让它无声增减。");
        }

        /// <summary>
        /// 设置资产里序列化引用的后端必须真的解析出来。
        /// <para>[SerializeReference] 存的是托管引用的类型名三元组（class/ns/asm），重命名或挪动
        /// 处理器类型不会有任何编译错误，只会让该字段还原成 null；外观层随即落到
        /// "设置里没有就用代码默认"的兜底上，游戏照常启动、照常读默认包名，且不打一行日志。
        /// 这条用例就是那个静默失败唯一的自动闸。</para>
        /// </summary>
        [Test]
        public void Settings_SerializedHandler_ResolvesToConcreteBackend()
        {
            ResourceServiceHandler handler = ResourceServiceSettings.ResourceServiceHandler;

            Assert.IsNotNull(handler,
                "设置资产里的 m_ResourceServiceHandler 解析为 null——序列化引用的类型名对不上，" +
                "外观层会静默改用代码默认后端，构建里看不出任何异常。");
            Assert.IsInstanceOf<YooAssetHandler>(handler,
                "序列化后端与包内默认后端不一致，确认是有意换的还是引用断裂后兜底的。");
        }

        private static int CountAbstractMembers()
        {
            Type type = typeof(ResourceServiceHandler);
            int count = 0;

            foreach (MethodInfo method in type.GetMethods(Declared))
            {
                if (!method.IsAbstract || IsPropertyAccessor(method.Name))
                {
                    continue;
                }

                count++;
            }

            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                MethodInfo getter = property.GetGetMethod(true);
                MethodInfo setter = property.GetSetMethod(true);
                bool isAbstract = (getter != null && getter.IsAbstract) || (setter != null && setter.IsAbstract);
                if (isAbstract)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountInternalAbstractMembers()
        {
            Type type = typeof(ResourceServiceHandler);
            int count = 0;

            foreach (MethodInfo method in type.GetMethods(Declared))
            {
                if (method.IsAbstract && method.IsAssembly && !IsPropertyAccessor(method.Name))
                {
                    count++;
                }
            }

            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                MethodInfo getter = property.GetGetMethod(true);
                MethodInfo setter = property.GetSetMethod(true);
                bool isInternalAbstract = (getter != null && getter.IsAbstract && getter.IsAssembly) ||
                    (setter != null && setter.IsAbstract && setter.IsAssembly);
                if (isInternalAbstract)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountObsoleteMembers()
        {
            Type type = typeof(ResourceServiceHandler);
            int count = 0;

            foreach (MethodInfo method in type.GetMethods(Declared))
            {
                if (!IsPropertyAccessor(method.Name) && method.IsDefined(typeof(ObsoleteAttribute), false))
                {
                    count++;
                }
            }

            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                if (property.IsDefined(typeof(ObsoleteAttribute), false))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsPropertyAccessor(string methodName)
        {
            return methodName.StartsWith("get_", StringComparison.Ordinal) ||
                methodName.StartsWith("set_", StringComparison.Ordinal);
        }
    }
}
