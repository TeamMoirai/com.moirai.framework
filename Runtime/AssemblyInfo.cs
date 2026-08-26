using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

[assembly: AssemblyProduct("Moirai.Atropos")]
[assembly: AssemblyDescription("A Unity development framework designed for efficient, flexible, and professional workflows.")]
[assembly: AssemblyCopyright("Copyright © 2026")]

[assembly: InternalsVisibleTo("Moirai.Atropos.Editor")]
[assembly: InternalsVisibleTo("Moirai.Atropos.Tests")]
[assembly: InternalsVisibleTo("Moirai.Clotho")]
[assembly: InternalsVisibleTo("Moirai.Lachesis")]

// IL2CPP 代码生成优化：仅 Player 构建生效，Editor Mono 保持全量隐式检查；
// 框架 Fail-Fast 依赖的显式 GameException 校验不受影响
[assembly: Il2CppSetOption(Option.NullChecks, false)]
[assembly: Il2CppSetOption(Option.ArrayBoundsChecks, false)]