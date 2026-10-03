using Microsoft.CodeAnalysis;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// SaveServiceCodegen 诊断描述符（MIRAI3xx 系列，与 ServiceDependencyAnalyzer 的 MIRAI1xx/2xx 系列错开）。
    /// </summary>
    internal static class Diagnostics
    {
        /// <summary>诊断类别。</summary>
        private const string Category = "Save";

        /// <summary>MIRAI300：字段类型不受生成器支持。</summary>
        public static readonly DiagnosticDescriptor UnsupportedFieldType = new(
            "MIRAI300",
            "SaveField 字段类型不受支持",
            "[SaveField] 字段 '{0}.{1}' 的类型 '{2}' 不受 SaveServiceCodegen 生成器支持（支持：基元/枚举/string/DateTime/TimeSpan/Unity 数学类型、数组/List/Queue/Stack/HashSet/Dictionary、嵌套 [SaveData] 类、UnityEngine.Object 引用）",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI301：存档键重复。</summary>
        public static readonly DiagnosticDescriptor DuplicateKey = new(
            "MIRAI301",
            "SaveField 存档键重复",
            "类型 '{0}' 内存在重复的存档键 '{1}'",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI302：ISaveMigrator 实现无法生成自注册代码。</summary>
        public static readonly DiagnosticDescriptor InvalidMigrator = new(
            "MIRAI302",
            "ISaveMigrator 实现无法自注册",
            "ISaveMigrator 实现 '{0}' 无法生成自注册代码：须为具体（非抽象）类、不能嵌套在私有类型内、且提供可访问的无参构造函数",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI303：包含类型必须为 partial class。</summary>
        public static readonly DiagnosticDescriptor TypeMustBePartial = new(
            "MIRAI303",
            "包含类型必须为 partial class",
            "[SaveField] 所在类型 '{0}' 必须声明为 partial class（嵌套组件的外层类型链每一级同样须为 partial class——生成捕获器逐层包裹以访问字段）",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI304：字段必须为实例字段。</summary>
        public static readonly DiagnosticDescriptor MustBeInstanceField = new(
            "MIRAI304",
            "SaveField 必须标注实例字段",
            "[SaveField] 不能标注类型 '{0}' 的静态/常量字段 '{1}'",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI305：场景引用字段指引（目标须挂 SaveObjectIdentity）。</summary>
        public static readonly DiagnosticDescriptor SceneReferenceGuidance = new(
            "MIRAI305",
            "场景引用字段需 SaveObjectIdentity",
            "[SaveField] 场景引用字段 '{0}'：捕获存目标 SaveObjectIdentity 的稳定 ID——被引用物体须挂 SaveObjectIdentity 组件，否则捕获写 Null",
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        /// <summary>MIRAI306：引用字段类型不明（UnityEngine.Object 基类无法区分场景/资产）。</summary>
        public static readonly DiagnosticDescriptor AmbiguousReferenceType = new(
            "MIRAI306",
            "引用字段类型不明",
            "[SaveField] 引用成员 '{0}' 声明为 UnityEngine.Object 基类，生成器无法区分场景引用与资产引用——请使用具体类型（Component 派生/GameObject = 场景引用；Texture/ScriptableObject 等 = 资产引用），该字段不参与捕获",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        /// <summary>MIRAI307：嵌套数据类型无效。</summary>
        public static readonly DiagnosticDescriptor InvalidNestedType = new(
            "MIRAI307",
            "嵌套数据类型无效",
            "[SaveField] 嵌套数据成员 '{0}' 的类型 '{1}' 无效：{2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI308：集合元素/映射键值成员类型不受支持。</summary>
        public static readonly DiagnosticDescriptor UnsupportedNestedMember = new(
            "MIRAI308",
            "集合/嵌套成员类型不受支持",
            "[SaveField] 成员 '{0}' 的类型 '{1}' 不受支持：{2}",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI309：[RegisterSerializer] 目标无法生成自注册代码。</summary>
        public static readonly DiagnosticDescriptor InvalidSaveSerializer = new(
            "MIRAI309",
            "SaveSerializer 实现无法自注册",
            "[RegisterSerializer] 标注的类型 '{0}' 无法生成自注册代码：须实现 Moirai.Atropos.Save.ISaveSerializer、为具体（非抽象、非泛型）类、嵌套链在程序集内可访问、且提供可访问的无参构造函数",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI310：BackendId 不是编译期常量表达式。</summary>
        public static readonly DiagnosticDescriptor BackendIdNotConstant = new(
            "MIRAI310",
            "SaveSerializer 的 BackendId 须为编译期常量",
            "[RegisterSerializer] 类型 '{0}' 的 BackendId 不是编译期常量表达式：判重与占号检查依赖静态取值，请写成 '=> 1000' 或 '=> SaveBackendIds.XXX'（const）形态",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI311：后端标识落在框架保留区或与内置标识相撞。</summary>
        public static readonly DiagnosticDescriptor ReservedSaveBackendId = new(
            "MIRAI311",
            "SaveSerializer 后端标识与框架保留区相撞",
            "[RegisterSerializer] 类型 '{0}' 声明的后端标识 {1} 落在框架保留区 0-{2}（内置 {3}，{4} 为组件捕获格式专用）：自定义后端从 1000 起分配，否则模块初始化期会与内置后端重复登记",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI312：同一编译单元内两个实现共用后端标识。</summary>
        public static readonly DiagnosticDescriptor DuplicateSaveBackendId = new(
            "MIRAI312",
            "SaveSerializer 后端标识重复",
            "[RegisterSerializer] 类型 '{0}' 的后端标识 {1} 已被 '{2}' 占用：同标识两实现并存会使旧档还原结果不可预期（跨程序集撞号运行期记 Fatal 并保留先到那份）",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
