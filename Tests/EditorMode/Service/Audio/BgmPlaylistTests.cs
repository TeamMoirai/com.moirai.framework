using System;
using System.Collections.Generic;
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
    /// UTF 消除经 <see cref="UtfLogExpect"/>；成员触达一律走 internal 接缝（禁反射）。</para>
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
            first.ResolveLayerId();
            Assert.AreEqual(101, first._id, "首个实例应占住显式 ID");

            BgmPlaylist second = CreatePlaylist(101);
            UtfLogExpect.Error();
            second.ResolveLayerId();

            Assert.IsTrue(Mentions("已被另一个播放列表占用"),
                "撞车必须报 Error（fail-fast），不再静默改派自动 ID");
            Assert.AreEqual(0, second._id, "冲突实例不得占层（0 = 未解析）");
            Assert.IsTrue(second._layerConflicted, "冲突实例应标记冲突并拒绝播放");
            Assert.IsFalse(first._layerConflicted, "首个实例不受影响");

            DestroyPlaylist(first);
            DestroyPlaylist(second);
        }

        [Test]
        public void ResolveLayerId_ExplicitIdFirstClaimant_TakesRequestedId()
        {
            BgmPlaylist playlist = CreatePlaylist(256);
            playlist.ResolveLayerId();

            Assert.AreEqual(256, playlist._id);
            Assert.IsFalse(playlist._layerConflicted);
            Assert.IsEmpty(_capturedMessages, "无冲突时不应有任何日志");

            DestroyPlaylist(playlist);
        }

        [Test]
        public void ResolveLayerId_AutoId_UsesNegativeRangeDisjointFromExplicit()
        {
            BgmPlaylist autoA = CreatePlaylist(BgmPlaylist.AutoId);
            autoA.ResolveLayerId();
            BgmPlaylist autoB = CreatePlaylist(BgmPlaylist.AutoId);
            autoB.ResolveLayerId();
            BgmPlaylist explicitOwner = CreatePlaylist(1);
            explicitOwner.ResolveLayerId();

            int idA = autoA._id;
            int idB = autoB._id;

            Assert.Less(idA, 0, "自动 ID 必须走负区间，与显式正数值域分离");
            Assert.Less(idB, 0, "自动 ID 必须走负区间");
            Assert.AreNotEqual(idA, idB, "自动 ID 不得重复");
            Assert.AreEqual(1, explicitOwner._id, "最小显式正数 ID 与自动区间不冲突");

            DestroyPlaylist(autoA);
            DestroyPlaylist(autoB);
            DestroyPlaylist(explicitOwner);
        }

        [Test]
        public void ResolveLayerId_NegativeExplicitId_IsRejectedAsConfigError()
        {
            BgmPlaylist playlist = CreatePlaylist(-7);
            UtfLogExpect.Error();
            playlist.ResolveLayerId();

            Assert.IsTrue(Mentions("必须为正数"), "负数显式 ID 是配置错误，必须报出来");
            Assert.AreEqual(0, playlist._id);
            Assert.IsTrue(playlist._layerConflicted);

            DestroyPlaylist(playlist);
        }

        [Test]
        public void ConflictedInstance_PlayCommands_DoNotTouchLayerZero()
        {
            // 冲突实例调用任意播放入口：不占层（_id=0 是「未指定 ID」默认音组）、句柄保持 0
            BgmPlaylist first = CreatePlaylist(101);
            first.ResolveLayerId();

            BgmPlaylist second = CreatePlaylist(101);
            UtfLogExpect.Error();
            second.ResolveLayerId();

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
            playlist.m_ID = id;
            return playlist;
        }

        private static void DestroyPlaylist(BgmPlaylist playlist)
        {
            if (playlist != null) UnityEngine.Object.DestroyImmediate(playlist.gameObject);
        }

        /// <summary>清空静态 ID 注册表与自动游标，隔离夹具间与全量套件间的静态残留。</summary>
        private static void ResetStaticIdRegistry()
        {
            BgmPlaylist.s_ClaimedIds.Clear();
            BgmPlaylist.s_NextAutoId = -1;
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
