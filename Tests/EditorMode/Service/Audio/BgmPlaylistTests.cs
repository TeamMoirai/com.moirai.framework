using System;
using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// BgmPlaylist 分层 ID 契约：显式 ID 撞车 fail-fast（报错且本实例不播放）、
    /// 自动分配走负区间与显式正数值域分离。
    /// <para>错误日志内容断言经 <see cref="LogUtility.OnMessageLogged"/> 捕获（Handler 无关），
    /// UTF 消除经 <see cref="UtfLogExpect"/>。</para>
    /// </summary>
    [TestFixture]
    public sealed class BgmPlaylistTests
    {
        private readonly List<string> _capturedMessages = new List<string>();

        [SetUp]
        public void SetUp()
        {
            ResetStaticIdRegistry();
            _capturedMessages.Clear();
            LogUtility.OnMessageLogged += OnMessageLogged;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= OnMessageLogged;
            ResetStaticIdRegistry();
        }

        [Test]
        public void ResolveLayerId_ExplicitIdClash_SecondInstanceFailsFast()
        {
            BgmPlaylist first = CreatePlaylist(101);
            InvokeResolveLayerId(first);
            Assert.AreEqual(101, GetLayerId(first), "首个实例应占住显式 ID");

            BgmPlaylist second = CreatePlaylist(101);
            UtfLogExpect.Error();
            InvokeResolveLayerId(second);

            Assert.IsTrue(Mentions("已被另一个播放列表占用"),
                "撞车必须报 Error（fail-fast），不再静默改派自动 ID");
            Assert.AreEqual(0, GetLayerId(second), "冲突实例不得占层（0 = 未解析）");
            Assert.IsTrue(IsLayerConflicted(second), "冲突实例应标记冲突并拒绝播放");
            Assert.IsFalse(IsLayerConflicted(first), "首个实例不受影响");

            DestroyPlaylist(first);
            DestroyPlaylist(second);
        }

        [Test]
        public void ResolveLayerId_ExplicitIdFirstClaimant_TakesRequestedId()
        {
            BgmPlaylist playlist = CreatePlaylist(256);
            InvokeResolveLayerId(playlist);

            Assert.AreEqual(256, GetLayerId(playlist));
            Assert.IsFalse(IsLayerConflicted(playlist));
            Assert.IsEmpty(_capturedMessages, "无冲突时不应有任何日志");

            DestroyPlaylist(playlist);
        }

        [Test]
        public void ResolveLayerId_AutoId_UsesNegativeRangeDisjointFromExplicit()
        {
            BgmPlaylist autoA = CreatePlaylist(BgmPlaylist.AutoId);
            InvokeResolveLayerId(autoA);
            BgmPlaylist autoB = CreatePlaylist(BgmPlaylist.AutoId);
            InvokeResolveLayerId(autoB);
            BgmPlaylist explicitOwner = CreatePlaylist(1);
            InvokeResolveLayerId(explicitOwner);

            int idA = GetLayerId(autoA);
            int idB = GetLayerId(autoB);

            Assert.Less(idA, 0, "自动 ID 必须走负区间，与显式正数值域分离");
            Assert.Less(idB, 0, "自动 ID 必须走负区间");
            Assert.AreNotEqual(idA, idB, "自动 ID 不得重复");
            Assert.AreEqual(1, GetLayerId(explicitOwner), "最小显式正数 ID 与自动区间不冲突");

            DestroyPlaylist(autoA);
            DestroyPlaylist(autoB);
            DestroyPlaylist(explicitOwner);
        }

        [Test]
        public void ResolveLayerId_NegativeExplicitId_IsRejectedAsConfigError()
        {
            BgmPlaylist playlist = CreatePlaylist(-7);
            UtfLogExpect.Error();
            InvokeResolveLayerId(playlist);

            Assert.IsTrue(Mentions("必须为正数"), "负数显式 ID 是配置错误，必须报出来");
            Assert.AreEqual(0, GetLayerId(playlist));
            Assert.IsTrue(IsLayerConflicted(playlist));

            DestroyPlaylist(playlist);
        }

        [Test]
        public void ConflictedInstance_PlayCommands_DoNotTouchLayerZero()
        {
            // 冲突实例调用任意播放入口：不占层（_id=0 是「未指定 ID」默认音组）、句柄保持 0
            BgmPlaylist first = CreatePlaylist(101);
            InvokeResolveLayerId(first);

            BgmPlaylist second = CreatePlaylist(101);
            UtfLogExpect.Error();
            InvokeResolveLayerId(second);

            AudioClip dummy = AudioClip.Create("bgm_conflict_dummy", 44100, 1, 44100, false);
            second.m_Tracks.Add(dummy);
            second.PlayIndex(0);

            Assert.AreEqual(0UL, second._handle, "冲突实例的播放命令必须被守卫拦截");
            Assert.IsFalse(second.IsPlaying);

            UnityEngine.Object.DestroyImmediate(dummy);
            DestroyPlaylist(first);
            DestroyPlaylist(second);
        }

        private static BgmPlaylist CreatePlaylist(int id)
        {
            var go = new GameObject("[BgmPlaylistTests]");
            var playlist = go.AddComponent<BgmPlaylist>();
            SetPrivateField(playlist, "m_ID", id);
            return playlist;
        }

        private static void DestroyPlaylist(BgmPlaylist playlist)
        {
            if (playlist != null) UnityEngine.Object.DestroyImmediate(playlist.gameObject);
        }

        private static void InvokeResolveLayerId(BgmPlaylist playlist)
        {
            var method = typeof(BgmPlaylist).GetMethod("ResolveLayerId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "BgmPlaylist.ResolveLayerId 应存在");
            method.Invoke(playlist, null);
        }

        private static int GetLayerId(BgmPlaylist playlist)
        {
            return (int)GetPrivateField(playlist, "_id");
        }

        private static bool IsLayerConflicted(BgmPlaylist playlist)
        {
            return (bool)GetPrivateField(playlist, "_layerConflicted");
        }

        private static object GetPrivateField(BgmPlaylist playlist, string name)
        {
            var field = typeof(BgmPlaylist).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"BgmPlaylist.{name} 字段应存在");
            return field.GetValue(playlist);
        }

        private static void SetPrivateField(BgmPlaylist playlist, string name, object value)
        {
            var field = typeof(BgmPlaylist).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"BgmPlaylist.{name} 字段应存在");
            field.SetValue(playlist, value);
        }

        /// <summary>清空静态 ID 注册表与自动游标，隔离夹具间与全量套件间的静态残留。</summary>
        private static void ResetStaticIdRegistry()
        {
            var claimed = typeof(BgmPlaylist).GetField("s_ClaimedIds",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(claimed, "BgmPlaylist.s_ClaimedIds 字段应存在");
            ((HashSet<int>)claimed.GetValue(null)).Clear();

            var cursor = typeof(BgmPlaylist).GetField("s_NextAutoId",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(cursor, "BgmPlaylist.s_NextAutoId 字段应存在");
            cursor.SetValue(null, -1);
        }

        private bool Mentions(string fragment)
        {
            return _capturedMessages.Exists(
                message => message != null && message.Contains(fragment, StringComparison.Ordinal));
        }

        private void OnMessageLogged(ELogLevel level, string message, Exception exception)
        {
            _capturedMessages.Add(message);
        }
    }
}
