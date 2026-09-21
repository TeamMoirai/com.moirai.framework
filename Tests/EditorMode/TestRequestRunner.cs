using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Moirai.Atropos.Tests.EditorMode
{
    /// <summary>
    /// 请求式测试驱动：让开着 Unity 编辑器的工作机（或本仓库的 Agent 会话）不抢锁、不重启就能跑 Test Runner。
    /// <para>batchmode 打不开同一个工程（<c>Temp/UnityLockfile</c> 被占），而"改完代码要证据"这件事又不该
    /// 每次都等人去点 Test Runner。这里在测试程序集里挂一个 <c>EditorApplication.update</c> 轮询：
    /// 工程 <c>Temp/</c> 下出现请求文件就按里面的过滤器执行一轮，把结果与逐格进度回写到指定路径。</para>
    /// <para>协议（都是 <c>Temp/</c> 下的普通文件，调用方只轮询、不双向通信）：</para>
    /// <list type="bullet">
    ///   <item><description>请求 <c>Temp/MoiraiTestRequest.json</c>：<c>{id, mode(EditMode|PlayMode), output, assemblies[], tests[]}</c>。读到即删除，避免重复执行。</description></item>
    ///   <item><description>进度 <c>{output}.progress</c>：先写 <c>STARTED</c>，随后是正在跑的用例全名。</description></item>
    ///   <item><description>结果 <c>{output}</c>：计数 + 逐格失败详情；写完再落 <c>{output}.done</c>，内容为请求里的 <c>id</c>。</description></item>
    /// </list>
    /// <para>调用方必须自带唯一 <c>id</c> 并只认配对的 <c>.done</c>，否则会把上一轮留下的旧报告当成这次的结论。</para>
    /// <para>驱动只存在于测试程序集（<c>UNITY_INCLUDE_TESTS</c> 门控），不进玩家包；它是编辑器内的便利设施，
    /// 不替代发布流程里的自动化测试。</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class TestRequestRunner
    {
        private const string RequestPath = "Temp/MoiraiTestRequest.json";
        private const string Marker = "MOIRAI-TEST-RUN";

        private static readonly TestRunnerApi Api = ScriptableObject.CreateInstance<TestRunnerApi>();
        private static readonly List<string> Failures = new List<string>();

        private static Request _current;
        private static int _pass;
        private static int _fail;
        private static int _skip;
        private static double _duration;

        [Serializable]
        private sealed class Request
        {
            public string id;
            public string mode;
            public string output;
            public string[] assemblies;
            public string[] tests;
        }

        static TestRequestRunner()
        {
            Api.RegisterCallbacks(new Callbacks());
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            // 上一轮没跑完不接新单；编译中、导入中、正在播放中也不接（那时执行的域状态不属于本轮判据）。
            if (_current != null || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath))
            {
                return;
            }

            Request request = ReadAndConsume(RequestPath);
            if (!string.IsNullOrEmpty(request?.output))
            {
                Start(request);
            }
        }

        private static Request ReadAndConsume(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
                File.Delete(path);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestRequestRunner] 读不到请求文件：{exception.Message}");
                return null;
            }

            try
            {
                return UnityEngine.JsonUtility.FromJson<Request>(text);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestRequestRunner] 请求 JSON 不合法：{exception.Message}");
                return null;
            }
        }

        private static void Start(Request request)
        {
            _current = request;
            Failures.Clear();
            _pass = _fail = _skip = 0;
            _duration = 0d;

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(request.output));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Write(request.output + ".progress", request.id, "STARTED");
            }
            catch (Exception exception)
            {
                Complete($"无法准备输出路径：{exception.Message}");
                return;
            }

            try
            {
                Api.Execute(new ExecutionSettings(new Filter
                {
                    testMode = string.Equals(request.mode, "PlayMode", StringComparison.OrdinalIgnoreCase)
                        ? TestMode.PlayMode
                        : TestMode.EditMode,
                    assemblyNames = request.assemblies,
                    testNames = request.tests
                }));
            }
            catch (Exception exception)
            {
                _current = null;
                Write(request.output + ".error", exception.ToString());
                Debug.LogError($"[TestRequestRunner] 执行失败：{exception.Message}");
            }
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void TestStarted(ITestAdaptor test)
            {
                Request request = _current;
                if (request != null)
                {
                    Write(request.output + ".progress", request.id, test.FullName);
                }
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                // 套件/夹具节点也会回调，只统计叶子用例，否则计数翻倍。
                if (result.HasChildren)
                {
                    return;
                }

                string state = result.ResultState;
                if (state.StartsWith("Passed", StringComparison.Ordinal))
                {
                    _pass++;
                    return;
                }

                if (state.StartsWith("Skipped", StringComparison.Ordinal) ||
                    state.StartsWith("Inconclusive", StringComparison.Ordinal))
                {
                    _skip++;
                    return;
                }

                _fail++;
                Failures.Add($"{result.FullName} [{state}]\n{Indent(FirstLines(result.Message, 6))}\n{Indent(FirstLines(result.StackTrace, 8))}");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _duration = result.Duration;
                Complete(null);
            }
        }

        private static void Complete(string error)
        {
            Request request = _current;
            if (request == null)
            {
                return;
            }

            _current = null;
            StringBuilder report = new StringBuilder();
            report.Append("run ").Append(request.id ?? string.Empty);
            report.Append(error == null
                ? $" | passed {_pass} | failed {_fail} | skipped {_skip} | {_duration.ToString("F2", CultureInfo.InvariantCulture)}s"
                : $" | ABORTED: {error}");
            report.Append('\n');
            foreach (string failure in Failures)
            {
                report.Append('\n').Append(failure).Append('\n');
            }

            Write(request.output, report.ToString());
            Write(request.output + ".done", request.id ?? string.Empty);
        }

        private static string FirstLines(string text, int count)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int take = Math.Min(count, lines.Length);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < take; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(lines[i]);
            }

            if (lines.Length > take)
            {
                builder.Append($"\n    … 另有 {lines.Length - take} 行");
            }

            return builder.ToString();
        }

        private static string Indent(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "    (无)";
            }

            return "    " + text.Replace("\n", "\n    ");
        }

        private static void Write(string path, string id, string content)
        {
            Write(path, id.Length == 0 ? content : $"{Marker} {id} {content}");
        }

        private static void Write(string path, string content)
        {
            try
            {
                File.WriteAllText(Path.GetFullPath(path), content);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TestRequestRunner] 写 {path} 失败：{exception.Message}");
            }
        }
    }
}
