using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 单个基准用例的测量结果：计时统计（min/mean/max ms、ns/次）、GC 分配增量与各用例上报的自定义指标。
    /// <para>由各 Benchmark 的测量逻辑填充，交给 <see cref="BenchmarkReport"/> 统一落日志与 XML。</para>
    /// </summary>
    public sealed class BenchmarkCaseResult
    {
        private readonly List<KeyValuePair<string, object>> _metrics = new();

        public string Name;
        public string Category;
        public int Iterations;
        public int Trials;
        public double MinMs;
        public double MeanMs;
        public double MaxMs;
        public double NsPerOp;
        public long GcAllocBytes;

        public List<KeyValuePair<string, object>> Metrics => _metrics;

        /// <summary>追加一个用例自定义指标（如 reserve/pages/hard），落日志与 XML。</summary>
        public BenchmarkCaseResult Metric(string key, object value)
        {
            _metrics.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        /// <summary>把结果格式化成一行人类可读文本，<paramref name="tag"/> 为各 Benchmark 自己的日志前缀。</summary>
        public string FormatLine(string tag)
        {
            StringBuilder line = new StringBuilder(160);
            line.Append(tag).Append(' ').Append(Name)
                .Append(" | ms=").Append(MinMs.ToString("F4", CultureInfo.InvariantCulture));
            if (Iterations > 0)
            {
                line.Append(" ns/op=").Append(NsPerOp.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" (x").Append(Iterations).Append(')');
            }

            line.Append(" gcAlloc=").Append(GcAllocBytes);
            for (int i = 0; i < _metrics.Count; i++)
                line.Append(' ').Append(_metrics[i].Key).Append('=').Append(_metrics[i].Value);

            return line.ToString();
        }
    }

    /// <summary>
    /// 框架级基准报告：收集 <see cref="BenchmarkCaseResult"/>，输出统一的自定义 <c>&lt;benchmark&gt;</c> XML。
    /// <para>&lt;benchmark&gt; 根节点的环境头（生成时间、Unity 版本、机器、CPU、内存）由本类固定写入，
    /// 各 Benchmark 只通过 <see cref="SetMetadata"/> 补自己关心的根属性（如 phase/trials/failures），
    /// 保证所有基准产物结构一致、可被同一套工具 diff。</para>
    /// </summary>
    public sealed class BenchmarkReport
    {
        /// <summary>约定的导出路径环境变量名，未设置时落到 &lt;工程根&gt;/Benchmarks/。</summary>
        public const string XML_PATH_ENV_VAR = "MOIRAI_BENCH_XML";

        private readonly List<BenchmarkCaseResult> _cases = new();
        private readonly List<KeyValuePair<string, string>> _metadata = new();

        public BenchmarkReport(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public double TotalMs { get; set; }
        public IReadOnlyList<BenchmarkCaseResult> Cases => _cases;
        public int CaseCount => _cases.Count;

        public void Clear()
        {
            _cases.Clear();
            _metadata.Clear();
            TotalMs = 0;
        }

        public BenchmarkCaseResult Add(BenchmarkCaseResult result)
        {
            _cases.Add(result);
            return result;
        }

        /// <summary>追加根节点属性；同名覆盖，写入顺序即首次设置顺序。</summary>
        public BenchmarkReport SetMetadata(string key, string value)
        {
            for (int i = 0; i < _metadata.Count; i++)
            {
                if (_metadata[i].Key == key)
                {
                    _metadata[i] = new KeyValuePair<string, string>(key, value);
                    return this;
                }
            }

            _metadata.Add(new KeyValuePair<string, string>(key, value));
            return this;
        }

        /// <summary>
        /// 解析导出路径：优先环境变量 <see cref="XML_PATH_ENV_VAR"/>，否则工程根下 Benchmarks/&lt;name&gt;-benchmark.xml。
        /// </summary>
        public string ResolveXmlPath()
        {
            string fromEnv = Environment.GetEnvironmentVariable(XML_PATH_ENV_VAR);
            if (!string.IsNullOrEmpty(fromEnv))
                return fromEnv;

            string projectRoot = Directory.GetParent(Application.temporaryCachePath)?.FullName ?? Application.temporaryCachePath;
            string fileName = (string.IsNullOrEmpty(Name) ? "benchmark" : Name.ToLowerInvariant()) + "-benchmark.xml";
            return Path.Combine(projectRoot, "Benchmarks", fileName);
        }

        /// <summary>把整份报告写成 <c>&lt;benchmark&gt;</c> XML；异常吞掉并经 <paramref name="log"/> 上报（默认 Debug.Log）。</summary>
        public void WriteXml(string path, Action<string> log = null)
        {
            log ??= Debug.Log;
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                XmlWriterSettings settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                using (XmlWriter writer = XmlWriter.Create(path, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("benchmark");
                    writer.WriteAttributeString("name", Name);
                    writer.WriteAttributeString("generatedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("unityVersion", Application.unityVersion);
                    writer.WriteAttributeString("machine", Environment.MachineName);
                    writer.WriteAttributeString("os", Application.platform.ToString());
                    writer.WriteAttributeString("cpu", SystemInfo.processorType);
                    writer.WriteAttributeString("memoryMb", SystemInfo.systemMemorySize.ToString(CultureInfo.InvariantCulture));

                    for (int i = 0; i < _metadata.Count; i++)
                        writer.WriteAttributeString(_metadata[i].Key, _metadata[i].Value);

                    writer.WriteAttributeString("totalMs", TotalMs.ToString("F2", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("cases", _cases.Count.ToString(CultureInfo.InvariantCulture));

                    for (int i = 0; i < _cases.Count; i++)
                        WriteCase(writer, _cases[i]);

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }

                log($"[BenchmarkReport] {Name} XML exported: {path}");
            }
            catch (Exception exception)
            {
                log($"[BenchmarkReport] {Name} XML export failed: {exception.Message}");
            }
        }

        private static void WriteCase(XmlWriter writer, BenchmarkCaseResult result)
        {
            writer.WriteStartElement("case");
            writer.WriteAttributeString("name", result.Name);
            writer.WriteAttributeString("category", result.Category);
            writer.WriteAttributeString("iterations", result.Iterations.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("trials", result.Trials.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("minMs", F(result.MinMs));
            writer.WriteAttributeString("meanMs", F(result.MeanMs));
            writer.WriteAttributeString("maxMs", F(result.MaxMs));
            writer.WriteAttributeString("nsPerOp", F(result.NsPerOp));
            writer.WriteAttributeString("gcAllocBytes", result.GcAllocBytes.ToString(CultureInfo.InvariantCulture));

            for (int m = 0; m < result.Metrics.Count; m++)
            {
                writer.WriteStartElement("metric");
                writer.WriteAttributeString("name", result.Metrics[m].Key);
                writer.WriteAttributeString("value", Convert.ToString(result.Metrics[m].Value, CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private static string F(double value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}