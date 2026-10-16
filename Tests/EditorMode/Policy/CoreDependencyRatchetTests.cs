using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.PackageManager;
using static Moirai.Atropos.LogHandler;

namespace Policy
{
    /// <summary>
    /// Core→功能服务依赖棘轮门禁：<c>Runtime/Core/**</c> 不得使用 <c>Runtime/Services/**</c>（<c>Runtime/Services/Kernel/**</c> 容器除外）
    /// 在命名空间层级声明的类型，按「类型名整词使用」判定，与消费方的目录、与 <c>using</c> 写法都无关；允许清单是双向棘轮。
    /// 判的是「Core 里有没有在服务层类型」这条<b>使用</b>边，不是服务代码住在哪个目录（见盲点 ⑦）。
    /// </summary>
    /// <remarks>
    /// 为什么判据必须是「类型名」而不是「using」：本轮搬运会拆掉 <c>Runtime/Core/Foundation</c> 与 <c>Runtime/Core/Infrastructure</c>
    /// 两个层级壳，壳一没，「Core 不依赖服务层」在仓里就再没有目录名可以承载，本文件是它唯一的机械判据（<c>Runtime</c> 只有一枚 asmdef，
    /// <see cref="CoreDependencyRatchetTests"/> 之外没有任何编译期边界）。而 using 扫描看不见同命名空间/全限定写法：<c>GameApp.cs</c> 就在
    /// <c>namespace Moirai.Atropos</c> 里裸用 <c>GameServices</c> 与 <c>EServiceScopeKind</c>，一行 using 都没有。 <br />
    /// 容器豁免：<c>Runtime/Services/Kernel/**</c>（<c>GameServices</c>/<c>ServiceWorld</c>/<c>IService</c>/<c>ServiceBase</c>/<c>ServiceMono</c> 等 16 个 <c>.cs</c>，
    /// 枚数钉在 <see cref="ContainerKernel_FileSet_IsExactlyTheRoster"/> 的名册上）是依赖注入容器本身，Core 的启动路径必须能碰它，所以整目录不进判据词表；
    /// 被禁的是 Kernel 之外的「功能服务」（Timer/Resource/Save/Scene/UI…）。这条豁免按<b>目录形状</b>生效，所以它自带一个镜像盲点，见 ⑧。 <br />
    /// 词表由扫描自身推导：<c>Runtime/Services</c> 下（容器除外）在命名空间层级声明的类型名，减去 <c>Runtime/Core</c> 侧同层同名声明的名字——
    /// 后者是 partial 类跨目录拆开的情形（实测只有 <c>CommandLineUtility</c>：Core 的 <c>Runtime/Core/Utilities/CommandLine/CommandLineUtility.cs</c>
    /// 与 Debugger/Localization 两份 partial 同一个类型，类型名归属 Core 自己，不是跨层依赖），同名分不开就不算服务类型。
    /// 词表不写死，搬运改目录改文件名都影响不到它。但它钉的是<b>使用</b>边、不是<b>归属</b>边：把仍被 Core 引用、或已在清单里挂着的服务类型搬进
    /// <c>Runtime/Core</c> 时，那个名字要么从词表里收缩掉、要么因两侧同层同名而被剔除，清单条目随即对不上而变红（复算搬 <c>TimerService.cs</c> 进 Core 三条全红，
    /// 报的是 ③ 号「同层同名剔除」诊断而不是「站点消失，请删条目」）；
    /// 搬一份<b>当下没人引用</b>的服务文件（复算搬 <c>Runtime/Services/Pooling/Kernel/OpenHashMap.cs</c> 进 <c>Runtime/Core</c>）三条全绿——
    /// 目录归属不由本门禁判，见盲点 ⑦。 <br />
    /// 清单键 = 「消费方<b>文件名</b> + 被使用的服务<b>类型名</b>」，两列都与路径无关（Task 14 把 <c>DelayTask.cs</c> 从
    /// <c>Infrastructure/Tasks/Models/</c> 搬到 <c>Tasks/Models/</c> 不该弄红本门禁）。文件名可能撞名，所以有第三条断言把关：
    /// 一把键只准对应一个物理文件，否则「登记一条洗白两份」的漏洞就开了。 <br />
    /// 已知看不见的情形（①⑦ 是离线快照复算读数；② 与 ⑥ 与「全限定看得见」是编辑器仓内探针实测读数；⑧ 的洞形是读码得出、其守卫在 scratch 树上打红实测过；③④⑤ 是按实现推得、没有单独造探针）：
    /// ① 写在注释或字符串字面量里的类型名（扫描前一律抹成空格，所以词表成员在注释里的提及、字符串常量里的同名子串都不计站点）——
    /// 这条不举具体名字为证：举一个本就不在词表里的名字，抹不抹它都不会成站点，证不了这条盲区；② 服务类型嵌在别的类型内部时不进词表——跨文件用它必然写出容器名，
    /// 而容器名自己在词表里（实测被排除的 <c>Debug</c>/<c>Slot</c>/<c>ObjectPool</c> 都是这种嵌套类型，同名的 Core 成员不是依赖）。
    /// 定层靠花括号栈，栈只在声明正则认得出那行时才压类型帧，所以<b>正则认不出的声明形态会把它内部的类型误升成命名空间层级</b>：
    /// 上一轮就因此漏了 <c>public ref struct SaveKeyValueWriter</c>/<c>Reader</c>（整份不进词表）又多收了它的嵌套类型 <c>NestingFrame</c>，
    /// 本轮补了 <c>ref</c> 并把词表逐名与独立复算比对（探针实测 <c>SaveKeyValueWriter</c> 现在计站点、<c>NestingFrame</c> 不再计）；
    /// ③ 经 <c>using X = 非服务类型;</c> 别名遮蔽的名字不算站点（别名右侧若指向服务，其 <c>Moirai.Atropos.*</c> 链会被合格形态捕获，不会被洗白）；
    /// ④ 继承来的成员被裸调用（Core 类型继承服务基类后直接用继承成员，不出现类型名）看不见——继承声明本身要写基类名，那条边本身算站点；
    /// ⑤ <c>#if</c> 死分支里的声明与使用照样进词表/站点，只会多不会少；
    /// ⑥ <b>半合格写法看不见</b>：在 <c>namespace Moirai.Atropos</c> 里写 <c>UI.UIWindow</c>（合法 C#，一行 using 都不需要）不会计成站点——
    /// 回读不成 <c>Moirai.Atropos.*</c> 三段链就不算全限定，而它前一个字符是 <c>.</c>，被「点号前缀＝成员访问」规则当 <c>ELogLevel.Debug</c> 丢掉了；
    /// 文本上 <c>UI.UIWindow</c> 与真成员访问无从区分，故按看不见如实登记，不冒充检测力（探针实测：同一次运行里裸名 <c>SaveKeyValueWriter</c> 进了站点清单，
    /// <c>UI.UIWindow</c> 没进）。今天 Core 树里这种写法 0 处，是潜在洞、不是现役洞；
    /// ⑦ 把<b>没人引用</b>的服务文件整份搬进 <c>Runtime/Core</c> 不弄红本门禁（见上文，目录归属由搬运批次自己的出口验收判，不由这里判）；
    /// ⑧ <b>⑦ 的镜像形态：把功能服务文件搬进豁免目录 <c>Runtime/Services/Kernel/</c> 也不弄红词表判据</b>——<c>Scan()</c> 是在<b>造词表的途中</b>按
    /// <c>IsUnder(relative, CONTAINER_ROOT)</c> 整份跳过文件的，所以 <c>git mv Runtime/Services/Scene/SceneService.cs Runtime/Services/Kernel/SceneService.cs</c>
    /// 这种「只换目录、内容零改动、命名空间不动」的搬运会把 <c>SceneService</c> 从判据词表里整个摘掉（它在 Core 侧 0 处取用、也不在允许清单里，
    /// 所以三条判据断言照旧读绿，能红的只有下面那条名册断言），
    /// 此后 Core 爱怎么用它就怎么用——豁免面就这一枚 <c>git mv</c> 的宽度，而且仓里原本没有任何东西约束「什么可以声明在 <c>Kernel/</c> 里」。
    /// 已钉住的三支基线类型（<c>TimerService</c>/<c>ResourceService</c>/<c>TimerUnsafeBinding</c>）反倒是<b>碰巧</b>受保护：名字一旦整个离开词表，就落进断言二的第二条分支
    /// （「已不在 <c>SERVICES_ROOT</c>（<c>Kernel</c> 除外）的命名空间层级声明里」），那条文案对这种情形给的指引本来就是对的（先核对搬动方向，别顺手删条目）。
    /// 这条洞由 <see cref="ContainerKernel_FileSet_IsExactlyTheRoster"/> 收口：<c>Kernel/**</c> 的 <c>.cs</c> 文件集合与 <see cref="ContainerKernelFileRoster"/>
    /// 做集合相等判定，多一枚少一枚都红，因此「悄悄把豁免面扩大」这件事必须在这份名册上留名。它钉的是<b>豁免目录的边界不许被暗中改动</b>，
    /// 不判「这枚文件该不该住在 Kernel」——后者归人判；也救不了「把类型改名成词表外新名字」那条老路（见 ⑦ 与盲点清单开头）。
    /// 全限定 <c>Moirai.Atropos.&lt;服务&gt;.&lt;类型&gt;</c> 与
    /// <c>global::Moirai.Atropos.&lt;服务&gt;.&lt;类型&gt;</c> 都看得见（<c>global::</c> 那枚探针实测）。扫描是文本级（抹注释与字符串后的整词匹配 + 花括号栈定层），
    /// 不是语法树，也不读编译模型，因此不需要反射。 <br />
    /// 结构与 <see cref="ReflectionPolicyGuardTests"/> 同构（包根解析、双向断言、失败文案给下一步动作）。
    /// </remarks>
    [TestFixture]
    public sealed class CoreDependencyRatchetTests
    {
        /// <summary>清单键的「文件名 / 类型名」分隔符：合法文件名与 C# 标识符里都不可能出现竖线。</summary>
        private const char KEY_SEPARATOR = '|';

        /// <summary>被扫描的消费方根目录（相对包根）。</summary>
        private const string CORE_ROOT = "Runtime/Core";

        /// <summary>服务层根目录（相对包根）：判据词表由它推导。</summary>
        private const string SERVICES_ROOT = "Runtime/Services";

        /// <summary>容器目录（相对包根）：依赖注入容器本身，Core 启动路径按构造必须能碰，整目录豁免。</summary>
        private const string CONTAINER_ROOT = "Runtime/Services/Kernel";

        /// <summary>根命名空间：Core 的文件都住在它的子树里，所以它声明的类型无需 using 即可裸用。</summary>
        private const string ROOT_NAMESPACE = "Moirai.Atropos";

        /// <summary>
        /// 基线登记表：条目写成<b>数组</b>再交给 <see cref="BuildAllowlist"/> 折成字典——字典字面量的索引器会静默覆盖重复键，
        /// 后写的那条理由凭空消失、登记条数也悄悄变小，门禁读数就对不上账。重复键在这里是硬失败，不是「后写的算」。
        /// </summary>
        private static readonly KeyValuePair<string, string>[] BaselineEntries =
        {
            Entry("DelayTask.cs", "TimerService",
                "基线：DelayTask 的延迟语义就是 TimerService.DelayUnsafe（Runtime/Core/Tasks/Models/DelayTask.cs:16），任务与计时器的这层耦合本轮不拆，接受"),

            Entry("DelayTask.cs", "TimerUnsafeBinding",
                "基线：与上一条同一条 using 边带出的回调结构体，DelayTask.cs:16 new TimerUnsafeBinding(task, &StopDelayTask)，旧版按 using 命名空间计数时看不见这条"),

            Entry("DefaultVersionHandler.cs", "ResourceService",
                "基线：默认版本处理器经 ResourceService 读远端版本清单（Runtime/Core/Utilities/GameVersion/Handler/DefaultVersionHandler.cs:18 与 :20），本轮不改行为，接受"),
        };

        /// <summary>
        /// 允许的 Core→功能服务依赖站点：键 = 「消费方文件名 + 被使用的服务类型名」，值 = 登记理由。
        /// </summary>
        /// <remarks>
        /// 基线 3 条站点（本门禁自己的读数，逐条带 file:line 证据，见各条理由）。 <br />
        /// 本门禁变红有<b>两种</b>成因，不等于「该批新增了一条 Core→功能服务的类型引用」：键由「消费方文件名 + 服务类型名」两列组成，
        /// 纯改名（叶子文件换个名，如 <c>DelayTask.cs</c> 换成 <c>DelayTask.Delay.cs</c>）就同时打红两条——① 把改名后的文件报成新站点，
        /// ② 报旧条目不再命中，而这一批什么都没引用，要改的是那条键的<b>文件名</b>列。真新增一条使用边只打红 ①，且 ① 正文给的
        /// <c>文件:行</c> 指向一条此前不存在的使用边，两种成因据此区分。只换目录不换文件名的纯搬运（git mv、命名空间不动）弄不红它——<b>前提是该搬运不跨豁免目录、
        /// 也不进出参与导入的目录</b>：把服务文件搬进 <c>Runtime/Services/Kernel/</c> 会让它整个从词表消失，那是另一码事（见盲点 ⑧ 与 <see cref="ContainerKernel_FileSet_IsExactlyTheRoster"/>）。
        /// 但「把服务代码搬进 Core」本身不必然弄红它，见文件头盲点 ⑦。
        /// 确属正当的新站点必须在此登记「文件名 + 类型名」并写明为什么解不开，同时在提交信息里说明。
        /// </remarks>
        private static readonly Dictionary<string, string> Allowlist = BuildAllowlist(BaselineEntries);

        /// <summary>
        /// 抹掉注释与字符串后切出的声明：命名空间或类型（含其在文本里的位置）。修饰符表必须列全——
        /// 漏一个 <c>ref</c>，<c>public ref struct</c> 的服务类型就整个不进词表，它没被认出的那枚 <c>{</c> 还会被当成匿名帧，
        /// 把它的嵌套类型误升成命名空间层级（实测：<c>SaveKeyValueWriter</c>/<c>SaveKeyValueReader</c> 缺席，<c>NestingFrame</c> 被误收）。
        /// </summary>
        private static readonly Regex DeclarationPattern = new Regex(
            @"^[ \t]*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|unsafe|new|extern|file|fixed|ref)[ \t]+)*(?:(?<ns>namespace)|(?<type>class|struct|interface|enum|record))[ \t]+(?<name>[A-Za-z_][\w\.]*)",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>普通 <c>using</c> 与 <c>using static</c> 的目标路径（命名空间或类型）。</summary>
        private static readonly Regex PlainUsingPattern = new Regex(
            @"^[ \t]*using[ \t]+(?:static[ \t]+)?(?<target>[A-Za-z_][\w\.]*)[ \t]*;",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>别名 <c>using A = X;</c>：左侧 A 会遮蔽同名类型，右侧 X 仍参与链判定。</summary>
        private static readonly Regex AliasUsingPattern = new Regex(
            @"^[ \t]*using[ \t]+(?<alias>[A-Za-z_]\w*)[ \t]*=[ \t]*(?<target>[A-Za-z_:][\w\.:]*)[ \t]*;",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>抹掉注释与字符串之后的源文本里切出所有标识符（整词匹配靠它，不用逐个类型名跑一遍正则）。</summary>
        private static readonly Regex IdentifierPattern = new Regex(@"[A-Za-z_]\w*", RegexOptions.Compiled);

        /// <summary>
        /// 方向一（只准降不准升）：<c>Runtime/Core</c> 下扫到的每条 Core→功能服务站点都必须命中允许清单。
        /// </summary>
        [Test]
        public void Core_DependencySites_AreWithinAllowlist()
        {
            ScanResult scan = Scan(ResolvePackageRoot());

            List<Site> offenders = scan.Sites.Where(site => !Allowlist.ContainsKey(site.Key)).ToList();

            Assert.IsEmpty(offenders,
                $"新增 Core→功能服务依赖，棘轮只准降不准升（本轮扫到 {scan.CoreSourceCount} 个 {CORE_ROOT} 源文件、{scan.ServiceSourceCount} 个服务源文件，" +
                $"判据词表 {scan.ServiceTypeNames.Count} 个类型名，容器目录 {CONTAINER_ROOT} 整目录豁免，同层同名剔除：{Describe(scan.ExcludedNames)}；" +
                $"命中 {scan.Sites.Count} 处使用 / {scan.Pairs.Count} 条站点）：\n" +
                string.Join("\n", offenders.Select(offender =>
                    $"  新增 Core→功能服务依赖：`{offender.RelativeFile}:{offender.Line}` 以{offender.Form}形态使用了服务类型 `{offender.TypeName}`——要么解开这条依赖，" +
                    $"要么在 CoreDependencyRatchetTests.Allowlist 里登记 `{offender.Key}` 这条「文件名 + 类型名」并说明为什么解开不了。")) +
                "\n本门禁的键是「消费方文件名 + 服务类型名」，不含目录：只换目录、文件名与命名空间都不动的纯搬运（git mv）弄不红它——前提是它不跨容器豁免目录、" +
                "也不进出参与导入的目录（把服务文件搬进 " + CONTAINER_ROOT + " 会让它从词表消失，那是盲点 ⑧，由名册断言拦）。" +
                "上面每一条的字面含义是：这个 Core 文件正在以该形态使用这个服务类型名，而这一组合不在允许清单里。打红有两种成因，先按上面给的 文件:行 判别再动手——" +
                "① 那一行是一条此前不存在的使用边：这才算真新增，要么解开这条依赖，要么按上面的说明把这条「文件名 + 类型名」键登记进 Allowlist 并写明为什么解不开；" +
                "② 这一条只是既有基线站点换了文件名（叶子重命名把新文件名报成新站点，方向二会同时报旧条目不再命中）：要改的是那条键的文件名列，" +
                "这一批既没有新增依赖也不该被回退。");
        }

        /// <summary>
        /// 方向二（棘轮是双向的）：允许清单里每条都必须仍被同一次扫描命中，否则清单在腐烂，减方向的运动就看不见。
        /// </summary>
        [Test]
        public void Core_Allowlist_EntriesStillMatch()
        {
            ScanResult scan = Scan(ResolvePackageRoot());

            List<string> stale = new List<string>();

            foreach (KeyValuePair<string, string> entry in Allowlist)
            {
                if (scan.Pairs.Contains(entry.Key))
                {
                    continue;
                }

                SplitKey(entry.Key, out string consumerFile, out string typeName);

                if (!scan.CoreFileNames.Contains(consumerFile))
                {
                    stale.Add($"  清单登记的消费方 `{consumerFile}`（类型 `{typeName}`）在 {CORE_ROOT} 下已无同名文件——这条只判「同名文件在场性」，判不出「有没有文件在用它」：" +
                              $"{CORE_ROOT} 下的消费方改了名（如 `DelayTask.cs` 换成 `DelayTask.Delay.cs`）也走这里，那时这条边还活着，方向一会同时把新文件名报成新站点。" +
                              "先核对是改名还是真删：改了名就改这条键的文件名列（别回退那一批），确属真没有了才从清单删除");
                }
                else if (!scan.AllServiceTypeNames.Contains(typeName))
                {
                    stale.Add($"  类型 `{typeName}` 已不在 {SERVICES_ROOT}（{CONTAINER_ROOT} 除外）的命名空间层级声明里，而 `{consumerFile}` 还在——那是服务类型改名或被搬进 Core/搬出服务层，" +
                              "属于层级变化而不是站点消失：先核对搬动方向对不对，别顺手删条目把这条边永久弄瞎");
                }
                else if (scan.ExcludedNames.Contains(typeName))
                {
                    stale.Add($"  类型 `{typeName}` 本轮被同层同名剔除（{CORE_ROOT} 侧也声明了同名类型，文本判据分不开归属），`{consumerFile}` 的使用因此不被计数——" +
                              "这是词表盲区而不是依赖消失：先把同名 partial 的归属理清，别删清单条目");
                }
                else
                {
                    stale.Add($"  `{consumerFile}` 已经不再使用服务类型 `{typeName}`——站点已消失，请从清单删除，别留着占位");
                }
            }

            Assert.IsEmpty(stale,
                "Core→功能服务依赖允许清单有腐烂条目，棘轮的减方向就看不见（清单登记 " + Allowlist.Count + " 条，扫描实际命中 " + scan.Pairs.Count + " 条）：\n" +
                string.Join("\n", stale));
        }

        /// <summary>
        /// 第三条（键的唯一性与集合等式）：一把「文件名 + 类型名」的键只准对应一个物理文件，且扫到的站点集合与清单集合大小相等。
        /// </summary>
        /// <remarks>
        /// 前两条断言各自只查一个方向；这条把「键不能洗白」说死：Core 里若出现两个同名文件都用了同一个服务类型，
        /// 扫到的两条使用会折叠成一条键，方向一与方向二都还是绿的——登记一条就洗白两份。此处必须变红并点名每个键的物理来源。
        /// </remarks>
        [Test]
        public void Core_DependencySiteKeys_MapToExactlyOneFileEach()
        {
            ScanResult scan = Scan(ResolvePackageRoot());

            List<string> offenders = new List<string>();

            foreach (KeyValuePair<string, List<string>> pair in scan.PairSources)
            {
                if (pair.Value.Count > 1)
                {
                    offenders.Add($"  键 `{pair.Key}` 同时被 {pair.Value.Count} 个不同的物理文件命中：{Describe(pair.Value)}——" +
                                  "清单按文件名登记，这条键会把两份依赖洗成一份；请把两个消费方文件改成不同名（或各自登记并说明）");
                }
            }

            int unregistered = scan.Sites.Count(site => !Allowlist.ContainsKey(site.Key));
            int unmatched = Allowlist.Count(entry => !scan.Pairs.Contains(entry.Key));
            if (scan.Pairs.Count != Allowlist.Count)
            {
                offenders.Add($"  集合不等式：扫描命中 {scan.Pairs.Count} 条站点，清单登记 {Allowlist.Count} 条（未登记 {unregistered} 处使用、失效登记 {unmatched} 条）——" +
                              "双向必须相等，棘轮才是双向的");
            }

            Assert.IsEmpty(offenders,
                $"Core→功能服务依赖的键位不唯一或集合不等（本轮 {scan.CoreSourceCount} 个 {CORE_ROOT} 源文件）：\n" +
                string.Join("\n", offenders));
        }

        /// <summary>
        /// 容器豁免目录的名册（相对包根、正斜杠）：<c>Runtime/Services/Kernel/**</c> 下的 <c>.cs</c> 今天就是这 16 枚，一枚不多一枚不少。
        /// </summary>
        /// <remarks>
        /// 只列 <c>.cs</c> 是因为它恰好等于判据词表的输入集（<see cref="EnumerateSources"/> 只收 <c>*.cs</c>）；
        /// folder <c>.meta</c> 与 <c>.cs.meta</c> 的在场性不由这里判，另有搬家完整性工具与 CI 的目录 meta 门禁。
        /// 名册按<b>当前在场</b>登记：门面两枚在根，其余分布在 <c>Internal/</c>、<c>Models/</c>、<c>Objects/</c> 三个档。
        /// </remarks>
        private static readonly string[] ContainerKernelFileRoster =
        {
            "Runtime/Services/Kernel/GameServices.cs",
            "Runtime/Services/Kernel/GameServices.DiagnosticInfo.cs",
            "Runtime/Services/Kernel/Internal/ContractHandleComparer.cs",
            "Runtime/Services/Kernel/Internal/IServiceLifecycle.cs",
            "Runtime/Services/Kernel/Internal/ReferenceComparer.cs",
            "Runtime/Services/Kernel/Internal/ServicePriorityOrder.cs",
            "Runtime/Services/Kernel/Internal/ServiceScope.cs",
            "Runtime/Services/Kernel/Internal/ServiceWorld.cs",
            "Runtime/Services/Kernel/Internal/TopologySorter.cs",
            "Runtime/Services/Kernel/Models/AutoRegisterServiceAttribute.cs",
            "Runtime/Services/Kernel/Models/EDuplicateContractPolicy.cs",
            "Runtime/Services/Kernel/Models/IService.cs",
            "Runtime/Services/Kernel/Models/IServiceInterceptor.cs",
            "Runtime/Services/Kernel/Models/ServiceDependencyAttribute.cs",
            "Runtime/Services/Kernel/Objects/ServiceBase.cs",
            "Runtime/Services/Kernel/Objects/ServiceMono.cs",
        };

        /// <summary>
        /// 第四条（豁免面不许被暗中扩大）：<c>Runtime/Services/Kernel/**</c> 下的 <c>.cs</c> 文件集合必须与 <see cref="ContainerKernelFileRoster"/> <b>集合相等</b>。
        /// </summary>
        /// <remarks>
        /// 这条存在的理由是盲点 ⑧：<c>Scan()</c> 是在造词表的<b>途中</b>按目录前缀跳过 <c>CONTAINER_ROOT</c> 的，所以「往 <c>Kernel/</c> 里放一枚功能服务文件」
        /// 等价于「把它的类型从依赖判据里删掉」，而且一次 <c>git mv</c> 就够，不必改一个字节。名册把这条唯一的加宽通道变成会红的断言：
        /// 要多一枚就必须同时改这份基线，改判据这件事因此在 diff 里留下一行可见的账。它证明的是<b>豁免目录的边界没被暗中改动</b>，不证明「这枚文件该不该住在 <c>Kernel/</c>」（那归人判），
        /// 也不覆盖 <c>Kernel/</c> 内部原有的类型（它们今天就已经不在词表里）。少一枚同样红——搬空豁免目录会同时把老路径的判据输入摘掉，得留名。
        /// </remarks>
        [Test]
        public void ContainerKernel_FileSet_IsExactlyTheRoster()
        {
            string packageRoot = ResolvePackageRoot();
            string kernelRoot = Root(packageRoot, CONTAINER_ROOT);
            Assert.IsTrue(Directory.Exists(kernelRoot), $"{CONTAINER_ROOT} 根目录不存在：{kernelRoot}——豁免目录整个没了也是判据变更，不许静默通过");

            HashSet<string> actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(kernelRoot, "*.cs", SearchOption.AllDirectories))
            {
                actual.Add(Relative(packageRoot, file));
            }

            HashSet<string> expected = new HashSet<string>(ContainerKernelFileRoster, StringComparer.Ordinal);
            Assert.IsTrue(expected.Count == ContainerKernelFileRoster.Length, $"名册里登记了重复路径（{ContainerKernelFileRoster.Length} 条 vs {expected.Count} 个去重后的值）——基线自己都对不上账");

            List<string> extra = actual.Where(path => !expected.Contains(path)).OrderBy(path => path, StringComparer.Ordinal).ToList();
            List<string> missing = expected.Where(path => !actual.Contains(path)).OrderBy(path => path, StringComparer.Ordinal).ToList();

            Assert.IsEmpty(extra,
                $"{CONTAINER_ROOT} 下多出 {extra.Count} 个不在名册里的 .cs：{Describe(extra)}（名册 {expected.Count} 枚，实有 {actual.Count} 枚）——\n" +
                $"{CONTAINER_ROOT} 是<b>容器豁免目录</b>：判据词表在 Scan() 里对它整目录跳过，所以往这里放一枚服务文件就等于把它的类型从 Core→服务依赖门禁里删掉，" +
                "之后 Core 怎么用它都不计站点（盲点 ⑧）。要么把它搬回功能服务模块自己该去的档；" +
                $"若它确实是容器的新组成部分，就把它加进 {nameof(ContainerKernelFileRoster)}，并在提交信息里说明为什么豁免面该这么大——改这份名册就是改判据，不是走流程。");

            Assert.IsEmpty(missing,
                $"{CONTAINER_ROOT} 的名册少了 {missing.Count} 枚：{Describe(missing)}（名册 {expected.Count} 枚，实有 {actual.Count} 枚）——" +
                "豁免目录里的文件被搬走或删掉了：它的类型会<b>回到</b>判据词表里，方向一可能立刻多出新站点，而基线名册还在按老账说话。" +
                "先确认搬动方向对不对，再把名册改成终态，别让名册与盘上两处不一致地各活各的。");
        }

        #region 扫描 [SCAN]

        /// <summary>
        /// 一次扫描同时供三条断言使用：推导服务类型词表、扫 Core 的类型名使用、按「文件名 + 类型名」聚合成键位表。
        /// </summary>
        private static ScanResult Scan(string packageRoot)
        {
            string coreRoot = Root(packageRoot, CORE_ROOT);
            string servicesRoot = Root(packageRoot, SERVICES_ROOT);
            Assert.IsTrue(Directory.Exists(coreRoot), $"{CORE_ROOT} 根目录不存在：{coreRoot}——门禁无法测量，不是「没有依赖」");
            Assert.IsTrue(Directory.Exists(servicesRoot), $"{SERVICES_ROOT} 根目录不存在：{servicesRoot}——判据词表无从推导，门禁无法测量");

            ScanResult scan = new ScanResult();

            // ① 词表：服务层（容器目录除外）在命名空间层级声明的类型名 → 声明它的命名空间集合
            Dictionary<string, HashSet<string>> vocabulary = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string file in EnumerateSources(servicesRoot))
            {
                string relative = Relative(packageRoot, file);
                if (IsUnder(relative, CONTAINER_ROOT))
                {
                    continue;
                }

                scan.ServiceSourceCount++;
                FileModel model = ModelOf(file);

                foreach (TypeDecl decl in model.NamespaceLevelTypes)
                {
                    if (!vocabulary.TryGetValue(decl.Name, out HashSet<string> namespaces))
                    {
                        namespaces = new HashSet<string>(StringComparer.Ordinal);
                        vocabulary[decl.Name] = namespaces;
                    }

                    namespaces.Add(decl.NamespaceName);
                }
            }

            Assert.IsNotEmpty(vocabulary, $"{SERVICES_ROOT} 下（{CONTAINER_ROOT} 除外）没推导到任何命名空间层级的服务类型——词表塌了，判定不可信");

            // ② 同层同名剔除：Core 自己也声明了同名类型（partial 跨目录拆开），文本分不开归属，不算服务类型
            List<KeyValuePair<string, FileModel>> coreFiles = new List<KeyValuePair<string, FileModel>>();
            HashSet<string> coreTypeNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (string file in EnumerateSources(coreRoot))
            {
                FileModel model = ModelOf(file);
                coreFiles.Add(new KeyValuePair<string, FileModel>(file, model));
                scan.CoreFileNames.Add(Path.GetFileName(file));

                foreach (TypeDecl decl in model.NamespaceLevelTypes)
                {
                    coreTypeNames.Add(decl.Name);
                }
            }

            scan.CoreSourceCount = coreFiles.Count;
            Assert.IsNotEmpty(coreFiles, $"没有扫到 {CORE_ROOT} 下的任何 .cs 源文件——扫描范围塌了，判定不可信");

            // 记账顺序就是这条分支的可达性：先按「剔除同名之前」的全集留底，再做剔除。
            // 写反了 AllServiceTypeNames 与 ServiceTypeNames 永远相等，断言二的 ③ 号分支（词表盲区）成死代码，
            // 同名剔除会被报成「服务类型改名或被搬进 Core」，把操作者指向核对搬动方向这条错路。
            foreach (KeyValuePair<string, HashSet<string>> entry in vocabulary)
            {
                scan.AllServiceTypeNames.Add(entry.Key);
            }

            foreach (string name in vocabulary.Keys.Where(coreTypeNames.Contains).OrderBy(n => n, StringComparer.Ordinal).ToList())
            {
                scan.ExcludedNames.Add(name);
                vocabulary.Remove(name);
            }

            foreach (KeyValuePair<string, HashSet<string>> entry in vocabulary)
            {
                scan.ServiceTypeNames.Add(entry.Key);
                scan.ServiceTypeNamespaces[entry.Key] = entry.Value;
            }

            // ③ 扫 Core：整词命中的服务类型名，按「全限定链」或「裸名 + 可绑定」计入站点
            foreach (KeyValuePair<string, FileModel> entry in coreFiles)
            {
                string file = entry.Key;
                FileModel model = entry.Value;
                string relative = Relative(packageRoot, file);
                string leaf = Path.GetFileName(file);

                foreach (Match match in IdentifierPattern.Matches(model.Blanked))
                {
                    string name = match.Value;
                    if (!scan.ServiceTypeNamespaces.TryGetValue(name, out HashSet<string> declaringNamespaces))
                    {
                        continue;
                    }

                    string form = Classify(model, match.Index, name, declaringNamespaces);
                    if (form == null)
                    {
                        continue;
                    }

                    Site site = new Site(leaf, relative, name, LineOf(model.Blanked, match.Index), form);
                    scan.Sites.Add(site);
                    scan.Pairs.Add(site.Key);

                    if (!scan.PairSources.TryGetValue(site.Key, out List<string> sources))
                    {
                        sources = new List<string>();
                        scan.PairSources[site.Key] = sources;
                    }

                    if (!sources.Contains(relative))
                    {
                        sources.Add(relative);
                    }
                }
            }

            return scan;
        }

        /// <summary>
        /// 这次命中算不算一条站点：返回形态标签，不算则返回 null。
        /// </summary>
        /// <remarks>
        /// 全限定链（<c>Moirai.Atropos.&lt;服务&gt;.&lt;类型&gt;</c>，含 <c>global::</c> 前缀）无条件计入——写法摆在这里，跨层就是跨层。
        /// 名字前一个字符是 <c>.</c> 又不是合格链的，是成员访问（<c>ELogLevel.Debug</c>、<c>this.TimerService</c>），不算。
        /// 裸名要能绑得上才计：类型在根命名空间里（Core 的文件都住在 <c>Moirai.Atropos</c> 子树，无需 using）、
        /// 本文件 import 了它的命名空间、或本文件就声明在它的命名空间（或其子命名空间）里；
        /// 被本文件的 <c>using A = ...;</c> 别名遮蔽的名字不计（别名指向服务时，右侧的链会以合格形态自己撞上判据）。
        /// </remarks>
        private static string Classify(FileModel model, int index, string name, HashSet<string> declaringNamespaces)
        {
            string chained = ChainedNamespaceOf(model.Blanked, index, name);
            if (chained != null)
            {
                return declaringNamespaces.Contains(chained) ? "全限定" : null;
            }

            if (index > 0 && model.Blanked[index - 1] == '.')
            {
                return null;
            }

            if (model.AliasedNames.Contains(name))
            {
                return null;
            }

            foreach (string declared in declaringNamespaces)
            {
                if (declared == ROOT_NAMESPACE)
                {
                    return "裸名（根命名空间，无需 using）";
                }

                if (model.UsingTargets.Any(target => target == declared || IsWholeSegmentPrefix(target, declared)))
                {
                    return "裸名（using 引入了该命名空间）";
                }

                if (model.Namespaces.Any(@namespace => @namespace == declared || IsWholeSegmentPrefix(@namespace, declared)))
                {
                    return "裸名（同命名空间可见，无 using）";
                }
            }

            return null;
        }

        /// <summary>往回读 <c>a.b.C</c> 链：链根是 <c>Moirai.Atropos</c> 时返回该类型名之前的命名空间段，否则返回 null。</summary>
        private static string ChainedNamespaceOf(string text, int index, string name)
        {
            List<string> segments = new List<string> { name };
            int cursor = index;

            while (cursor > 0 && text[cursor - 1] == '.')
            {
                int end = cursor - 1;
                int start = end;

                while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
                {
                    start--;
                }

                if (start == end)
                {
                    break;
                }

                segments.Insert(0, text.Substring(start, end - start));
                cursor = start;
            }

            if (segments.Count < 3 || segments[0] != "Moirai" || segments[1] != "Atropos")
            {
                return null;
            }

            return string.Join(".", segments.Take(segments.Count - 1));
        }

        /// <summary><paramref name="candidate"/> 是不是以整段命名空间的形式落在 <paramref name="prefix"/> 之下。</summary>
        private static bool IsWholeSegmentPrefix(string candidate, string prefix)
        {
            return candidate.Length > prefix.Length
                && candidate.StartsWith(prefix, StringComparison.Ordinal)
                && candidate[prefix.Length] == '.';
        }

        /// <summary>行号：从抹除后的文本位置回推（抹除保留换行，行号与源文件一致）。</summary>
        private static int LineOf(string text, int index)
        {
            int line = 1;

            for (int i = 0; i < index && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        /// <summary>
        /// 解析一个源文件：抹掉注释与字符串字面量（保留换行），再按花括号栈标出命名空间层级的类型声明、命名空间、using 目标与别名。
        /// </summary>
        private static FileModel ModelOf(string file)
        {
            string blanked = BlankOut(File.ReadAllText(file, Encoding.UTF8));
            FileModel model = new FileModel(blanked);

            List<Decl> decls = new List<Decl>();

            foreach (Match match in DeclarationPattern.Matches(blanked))
            {
                bool isNamespace = match.Groups["ns"].Success;
                decls.Add(new Decl(match.Index, match.Index + match.Length, match.Groups["name"].Value, isNamespace));
            }

            Stack<char> frames = new Stack<char>();
            Stack<string> namespaces = new Stack<string>();
            Decl pending = null;
            int next = 0;
            int cursor = 0;

            while (cursor < blanked.Length)
            {
                if (next < decls.Count && decls[next].Position == cursor)
                {
                    Decl decl = decls[next++];

                    if (decl.IsNamespace)
                    {
                        namespaces.Push(decl.Name);
                        model.Namespaces.Add(decl.Name);
                    }
                    else if (!frames.Contains('T'))
                    {
                        // 命名空间层级（栈里只有命名空间帧）的声明才是判据词表的候选；嵌在类型内部的声明不是
                        model.NamespaceLevelTypes.Add(new TypeDecl(decl.Name, namespaces.Count > 0 ? namespaces.Peek() : string.Empty));
                    }

                    // 声明与它自己的 { 之间可能隔着基类列表与 where 约束，pending 一路活到那个 {；
                    // 文件作用域命名空间（namespace X;）没有配套的 { ，不能置 pending，否则下面第一个类型的 { 会被当成命名空间帧
                    int after = SkipSpace(blanked, decl.End);
                    pending = decl.IsNamespace && after < blanked.Length && blanked[after] == ';' ? null : decl;

                    continue;
                }

                char c = blanked[cursor];

                if (c == '{')
                {
                    frames.Push(pending == null ? 'O' : (pending.IsNamespace ? 'N' : 'T'));
                    pending = null;
                }
                else if (c == '}')
                {
                    if (frames.Count > 0)
                    {
                        char popped = frames.Pop();

                        if (popped == 'N' && namespaces.Count > 0)
                        {
                            namespaces.Pop();
                        }
                    }

                    pending = null;
                }

                cursor++;
            }

            foreach (Match match in PlainUsingPattern.Matches(blanked))
            {
                model.UsingTargets.Add(match.Groups["target"].Value);
            }

            foreach (Match match in AliasUsingPattern.Matches(blanked))
            {
                model.AliasedNames.Add(match.Groups["alias"].Value);
                model.UsingTargets.Add(match.Groups["target"].Value);
            }

            return model;
        }

        /// <summary>从 <paramref name="start"/> 起跳过空白。</summary>
        private static int SkipSpace(string text, int start)
        {
            int i = start;

            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            return i;
        }

        /// <summary>把注释与字符串字面量的内容抹成空格，换行原样保留，使行号与标识符位置仍对得上源文件。</summary>
        private static string BlankOut(string source)
        {
            char[] buffer = source.ToCharArray();
            int i = 0;

            while (i < buffer.Length)
            {
                char c = buffer[i];

                if (c == '/' && i + 1 < buffer.Length && (buffer[i + 1] == '/' || buffer[i + 1] == '*'))
                {
                    if (buffer[i + 1] == '/')
                    {
                        while (i < buffer.Length && buffer[i] != '\n')
                        {
                            if (buffer[i] != '\r')
                            {
                                buffer[i] = ' ';
                            }

                            i++;
                        }
                    }
                    else
                    {
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i += 2;

                        while (i < buffer.Length)
                        {
                            if (buffer[i] == '*' && i + 1 < buffer.Length && buffer[i + 1] == '/')
                            {
                                buffer[i] = ' ';
                                buffer[i + 1] = ' ';
                                i += 2;
                                break;
                            }

                            if (buffer[i] != '\n' && buffer[i] != '\r')
                            {
                                buffer[i] = ' ';
                            }

                            i++;
                        }
                    }

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    bool verbatim = i > 0 && (buffer[i - 1] == '@' || buffer[i - 1] == '$');
                    i++;

                    while (i < buffer.Length)
                    {
                        char d = buffer[i];

                        if (d == '\\' && !verbatim)
                        {
                            buffer[i] = ' ';
                            i++;

                            if (i < buffer.Length && buffer[i] != '\n' && buffer[i] != '\r')
                            {
                                buffer[i] = ' ';
                            }

                            i++;
                            continue;
                        }

                        if (d == '\n' || d == '\r')
                        {
                            if (!verbatim)
                            {
                                break;
                            }

                            i++;
                            continue;
                        }

                        if (d == quote)
                        {
                            i++;

                            if (verbatim && i < buffer.Length && buffer[i] == quote)
                            {
                                buffer[i - 1] = ' ';
                                buffer[i] = ' ';
                                i++;
                                continue;
                            }

                            break;
                        }

                        buffer[i] = ' ';
                        i++;
                    }

                    continue;
                }

                i++;
            }

            return new string(buffer);
        }

        /// <summary>
        /// 参与判定的源文件：跳过构建产物目录与 Unity 不导入的 <c>~</c> 结尾目录段（今天靠它把 <c>Templates~/</c> 挡在判定之外）。
        /// 过滤看的是整条绝对路径的分段，包根之上的祖先目录若也以 <c>~</c> 结尾会整棵树落空——那种情形由下面的
        /// <c>Assert.IsNotEmpty</c> 读数门当场报红，不会静默变绿。
        /// </summary>
        private static IEnumerable<string> EnumerateSources(string root)
        {
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => path.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) < 0)
                .Where(path => path.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) < 0)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment.EndsWith("~", StringComparison.Ordinal)));
        }

        private static string Root(string packageRoot, string relative)
        {
            return Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string Relative(string packageRoot, string file)
        {
            return Path.GetRelativePath(packageRoot, file).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static bool IsUnder(string relativePath, string relativePrefix)
        {
            return relativePath.StartsWith(relativePrefix + "/", StringComparison.Ordinal);
        }

        private static string Describe(IEnumerable<string> values)
        {
            List<string> list = values.ToList();

            return list.Count == 0 ? "无" : string.Join(", ", list);
        }

        /// <summary>清单键：文件名与类型名两列拼成一把序数比较的键。</summary>
        private static string KeyOf(string consumerFile, string typeName)
        {
            return consumerFile + KEY_SEPARATOR + typeName;
        }

        /// <summary>登记一条基线站点：键照 <see cref="KeyOf"/> 组装；重复不在这儿判，统一交给 <see cref="BuildAllowlist"/> 判清。</summary>
        private static KeyValuePair<string, string> Entry(string consumerFile, string typeName, string reason)
        {
            return new KeyValuePair<string, string>(KeyOf(consumerFile, typeName), reason);
        }

        /// <summary>
        /// 把 <see cref="BaselineEntries"/> 折成字典：同一把「文件名|类型名」键登记两次是写错，当场抛，不玩「后写的覆盖前写的」。
        /// </summary>
        /// <remarks>
        /// 字典字面量的索引器写法（<c>[KeyOf(...)] = "..."</c>）遇到重复键会静默覆盖：两条理由只剩最后一条、
        /// <c>Allowlist.Count</c> 也少一条，第三条断言就拿这份假账去比扫描读数。<see cref="Allowlist"/> 因此只能由这个方法产出。
        /// </remarks>
        private static Dictionary<string, string> BuildAllowlist(KeyValuePair<string, string>[] entries)
        {
            Dictionary<string, string> allowlist = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> entry in entries)
            {
                if (allowlist.ContainsKey(entry.Key))
                {
                    throw new InvalidOperationException(
                        $"基线登记表把 `{entry.Key}` 登记了两次——同一把「文件名{KEY_SEPARATOR}类型名」键只准对应一条理由：" +
                        "要么并成一条，要么两个消费方文件本就撞名（那是改文件名的事，不是再登记一条）。静默覆盖会让其中一条理由凭空消失，棘轮读数就说不清是谁给的豁免");
                }

                allowlist[entry.Key] = entry.Value;
            }

            return allowlist;
        }

        /// <summary>拆清单键；键写错（缺分隔符）本身就是要报出来的错。</summary>
        private static void SplitKey(string key, out string consumerFile, out string typeName)
        {
            int index = key.IndexOf(KEY_SEPARATOR);
            Assert.IsTrue(index > 0 && index < key.Length - 1, $"允许清单的键缺「文件名|类型名」分隔符，无法核对：`{key}`");

            consumerFile = key.Substring(0, index);
            typeName = key.Substring(index + 1);
        }

        /// <summary>被测包根：经 <see cref="PackageInfo"/> 定位，不依赖当前工作目录，也不依赖任何目录名假设。</summary>
        private static string ResolvePackageRoot()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(Moirai.Atropos.GameApp).Assembly);
            Assert.IsNotNull(package, "无法定位 Moirai.Atropos 所属包，Core→服务棘轮无法解析包根目录。");
            Assert.IsNotEmpty(package.resolvedPath, "Moirai.Atropos 所属包的 resolvedPath 为空，Core→服务棘轮无法解析包根目录。");

            return package.resolvedPath;
        }

        #endregion

        #region 扫描产物 [SCAN RESULT]

        private sealed class ScanResult
        {
            public List<Site> Sites = new List<Site>();

            public HashSet<string> Pairs = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>键 → 命中它的物理文件（相对包根）；一条键对上多个文件就是「登记一条洗白两份」。</summary>
            public Dictionary<string, List<string>> PairSources = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            public HashSet<string> ServiceTypeNames = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>
            /// 剔除同名<b>之前</b>的服务类型名全集（在 <see cref="ExcludedNames"/> 摘除动作前记账，故它是 <see cref="ServiceTypeNames"/> 的超集，
            /// 两者之差恰是本轮被剔除的名字）：断言二用它区分「站点真的没了」与「类型还在服务层、只是被同层同名剔掉而瞎了」。
            /// </summary>
            public HashSet<string> AllServiceTypeNames = new HashSet<string>(StringComparer.Ordinal);

            public Dictionary<string, HashSet<string>> ServiceTypeNamespaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            public List<string> ExcludedNames = new List<string>();

            public HashSet<string> CoreFileNames = new HashSet<string>(StringComparer.Ordinal);

            public int CoreSourceCount;

            public int ServiceSourceCount;
        }

        /// <summary>一条 Core→功能服务依赖站点：消费方文件名 + 被使用的服务类型名 + 证据位置与形态。</summary>
        private sealed class Site
        {
            public Site(string consumerFile, string relativeFile, string typeName, int line, string form)
            {
                ConsumerFile = consumerFile;
                RelativeFile = relativeFile;
                TypeName = typeName;
                Line = line;
                Form = form;
            }

            public string ConsumerFile { get; }

            public string RelativeFile { get; }

            public string TypeName { get; }

            public int Line { get; }

            public string Form { get; }

            /// <summary>与 <see cref="Allowlist"/> 比对用的键：只认「文件名 + 类型名」，与路径无关。</summary>
            public string Key => KeyOf(ConsumerFile, TypeName);
        }

        private sealed class FileModel
        {
            public FileModel(string blanked)
            {
                Blanked = blanked;
            }

            public string Blanked { get; }

            public List<TypeDecl> NamespaceLevelTypes { get; } = new List<TypeDecl>();

            public HashSet<string> Namespaces { get; } = new HashSet<string>(StringComparer.Ordinal);

            public HashSet<string> UsingTargets { get; } = new HashSet<string>(StringComparer.Ordinal);

            public HashSet<string> AliasedNames { get; } = new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class TypeDecl
        {
            public TypeDecl(string name, string namespaceName)
            {
                Name = name;
                NamespaceName = namespaceName;
            }

            public string Name { get; }

            public string NamespaceName { get; }
        }

        /// <summary>一次声明命中：在抹除文本里的起止位置、名字、是不是命名空间。</summary>
        private sealed class Decl
        {
            public Decl(int position, int end, string name, bool isNamespace)
            {
                Position = position;
                End = end;
                Name = name;
                IsNamespace = isNamespace;
            }

            public int Position { get; }

            public int End { get; }

            public string Name { get; }

            public bool IsNamespace { get; }
        }

        #endregion
    }
}
