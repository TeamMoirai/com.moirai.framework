using System;
using Moirai.Atropos;
using NUnit.Framework;

namespace Utility
{
    /// <summary>
    /// 验证 <see cref="PathUtility"/> 的路径规范化（Unity 风格 / 系统风格 / 远程前缀）与合并、URI 判定契约（存档与资源路径的跨平台基座）。
    /// </summary>
    /// <remarks><c>CommonPath</c> 与 <c>TruncatePath</c> 当前在运行时无调用方（<see cref="PathUtility.CommonPath"/> 对更短的后续路径会越界），不锁用例； <br />
    /// <c>GetPersistentDataPlatformPath</c> 依平台宏分叉，属 L3/平台验收面，不在此处判。</remarks>
    public class PathUtilityTests
    {
        [Test]
        public void FormatToUnityPath_Backslashes_BecomeForwardSlashes()
        {
            Assert.AreEqual("D:/Usr/Framework/", PathUtility.FormatToUnityPath(@"D:\Usr\Framework\"));
            Assert.AreEqual("Assets/Data/config.json", PathUtility.FormatToUnityPath(@"Assets\Data\config.json"));
            Assert.IsNull(PathUtility.FormatToUnityPath(null), "null 必须原样返回 null 而非抛出");
        }

        [Test]
        public void FormatToSysFilePath_ForwardSlashes_BecomeBackslashes()
        {
            Assert.AreEqual(@"D:\Usr\Framework\", PathUtility.FormatToSysFilePath("D:/Usr/Framework/"));
            Assert.IsNull(PathUtility.FormatToSysFilePath(null));
        }

        [Test]
        public void GetRemotePath_LocalPath_GetsFilePrefix()
        {
            Assert.AreEqual("file:///D:/Usr/data.json", PathUtility.GetRemotePath(@"D:\Usr\data.json"),
                "本地路径必须补 file:/// 前缀并规范化分隔符");
            Assert.IsNull(PathUtility.GetRemotePath(null));
        }

        [Test]
        public void GetRemotePath_ExistingScheme_IsLeftUntouched()
        {
            Assert.AreEqual("https://cdn.example.com/a/b.json", PathUtility.GetRemotePath("https://cdn.example.com/a/b.json"),
                "已带协议头的远程地址必须原样透传，不得再叠 file:// 前缀");
        }

        [Test]
        public void CombineUNCPath_MultipleSegments_JoinsWithBackslashes()
        {
            Assert.AreEqual(@"Resources\JsonData\config.json", PathUtility.CombineUNCPath("Resources", "JsonData", "config.json"),
                "UNC 合并必须用系统分隔符连接");
        }

        [Test]
        public void CombineURL_MultipleSegments_JoinsWithForwardSlashes()
        {
            Assert.AreEqual("github.com/Usr/Framework", PathUtility.CombineURL("github.com", "Usr", "Framework"),
                "URL 合并必须用正斜杠连接");
        }

        [Test]
        public void IsLegalUri_RequiresSchemeSeparator()
        {
            Assert.IsTrue(PathUtility.IsLegalUri("file:///a/b"), "带 :// 的地址是合法 URI");
            Assert.IsTrue(PathUtility.IsLegalUri("https://example.com"));
            Assert.IsFalse(PathUtility.IsLegalUri("relative/path"), "无协议分隔符不是合法 URI");
            Assert.IsFalse(PathUtility.IsLegalUri(""), "空串不是合法 URI");
            Assert.IsFalse(PathUtility.IsLegalUri(null), "null 不是合法 URI");
        }

        [Test]
        public void IsLegalHttpUri_AcceptsHttpAndHttps_CaseInsensitive()
        {
            Assert.IsTrue(PathUtility.IsLegalHttpUri("http://a.com"));
            Assert.IsTrue(PathUtility.IsLegalHttpUri("HTTPS://a.com"), "大写协议头同样合法");
            Assert.IsFalse(PathUtility.IsLegalHttpUri("file:///a/b"), "非 http(s) 协议头不通过");
            Assert.IsFalse(PathUtility.IsLegalHttpUri(null));
        }

        [Test]
        public void IsPath_DetectsBothSeparators()
        {
            Assert.IsTrue(PathUtility.IsPath(@"a\b"), "反斜杠判定为路径");
            Assert.IsTrue(PathUtility.IsPath("a/b"), "正斜杠判定为路径");
            Assert.IsFalse(PathUtility.IsPath("plainname"), "无分隔符的裸名不判定为路径");
        }
    }
}
