using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using App = Moirai.Atropos.GameApp;

namespace Core.GameApp
{
    /// <summary>
    /// <see cref="GameApp"/> 运行态契约测试：暂停引用计数（<c>PauseGame</c> / <c>ResumeGame</c> /
    /// <c>IsGamePaused</c>）与期望速度（<c>GameSpeed</c> / <c>ResetGameSpeed</c>）。
    /// <para>全部经 public 门面驱动，不碰私有计数——要锁的是「外部可见的行为」，不是实现细节。
    /// 暂停的判据落在引擎的 <c>Time.timeScale</c> 上，因此本夹具会写引擎全局状态；
    /// SetUp/TearDown 双向复位（编辑模式下写 <c>Time.timeScale</c> 改的是全局 TimeManager，
    /// 即 Project Settings &gt; Time 那份，不清理会污染编辑器与后续用例）。</para>
    /// <para>不调 <c>GameApp.Initialize</c> / <c>Shutdown</c>：它们会注入 PlayerLoop、物化宿主并关停
    /// 真实服务世界，编辑器里跑代价过大且不可逆。</para>
    /// </summary>
    public class GameAppRuntimeStateTests
    {
        private const float Tolerance = 1e-4f;

        private float _originalGameSpeed;
        private float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            _originalGameSpeed = App.GameSpeed;
            _originalTimeScale = Time.timeScale;

            // 复位到确定基线：计数 0、期望速度 1。编辑模式下 Initialize 从不运行，静态字段本该是
            // 声明时的初值，但仍显式复位——同域内的其它用例或上一轮的残留都可能改过它。
            while (App.IsGamePaused) App.ResumeGame();
            App.GameSpeed = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            while (App.IsGamePaused) App.ResumeGame();
            App.GameSpeed = _originalGameSpeed;

            // 门面只保证「未暂停时 timeScale == GameSpeed」；实况可能被绕过门面的写入分叉过，
            // 故时间戳单独还原，恢复进夹具时的原样
            Time.timeScale = _originalTimeScale;
        }

        #region 前置条件 [PRECONDITION]

        [Test]
        public void TimeScale_RoundTripsThroughEngineInEditMode()
        {
            // 本套件的暂停语义全靠 Time.timeScale 可观测来断言：Unity 把它存在全局 TimeManager
            // （与 Project Settings > Time 同一份），编辑模式下照样读写。该前提若不成立，
            // 下面所有断言都只是空壳——须整体改判 PlayMode 用例，而不是留一套骗绿的测试。
            Time.timeScale = 0.5f;
            Assert.AreEqual(0.5f, Time.timeScale, Tolerance,
                "引擎不回放亚 1 速，本套件的冻结判据不成立");

            Time.timeScale = 8f;
            Assert.AreEqual(8f, Time.timeScale, Tolerance,
                "引擎把大于 1 的 timeScale 夹住了：GameSpeed 的 1.5x~8x 预设与下面的回放断言都不成立");
        }

        #endregion

        #region 暂停引用计数 [PAUSE REFERENCE COUNT]

        [Test]
        public void PauseGame_FromIdle_FreezesTimeScaleAndMarksPaused()
        {
            App.PauseGame();

            Assert.IsTrue(App.IsGamePaused);
            Assert.AreEqual(0f, Time.timeScale, Tolerance);
        }

        [Test]
        public void PauseGame_NestedSources_OnlyLastResumeRestoresSpeed()
        {
            App.PauseGame(); // 弹窗
            App.PauseGame(); // 切后台
            App.PauseGame(); // 剧情过场

            App.ResumeGame();
            App.ResumeGame();

            Assert.IsTrue(App.IsGamePaused, "还有一层未配对");
            Assert.AreEqual(0f, Time.timeScale, Tolerance, "前两层 Resume 不该回速");

            App.ResumeGame();

            Assert.IsFalse(App.IsGamePaused);
            Assert.AreEqual(1f, Time.timeScale, Tolerance);
        }

        [Test]
        public void PauseGame_WhilePaused_KeepsResumeTargetIntact()
        {
            // 回归：旧实现另存 s_GameSpeedBeforePause，第二层暂停会拿初值把用户设定盖掉
            App.PauseGame();
            App.GameSpeed = 0.5f;

            App.PauseGame();

            Assert.AreEqual(0.5f, App.GameSpeed, Tolerance, "叠加暂停不该改写期望速度");

            App.ResumeGame();
            Assert.IsTrue(App.IsGamePaused, "还剩一层");
            Assert.AreEqual(0f, Time.timeScale, Tolerance);

            App.ResumeGame();
            Assert.AreEqual(0.5f, Time.timeScale, Tolerance, "最后一层才重放 0.5x");
        }

        [Test]
        public void ResumeGame_AtZeroDepth_IsNoOp()
        {
            App.GameSpeed = 0.5f;

            App.ResumeGame();
            App.ResumeGame();

            Assert.IsFalse(App.IsGamePaused);
            Assert.AreEqual(0.5f, App.GameSpeed, Tolerance);
            Assert.AreEqual(0.5f, Time.timeScale, Tolerance, "计数已 0 时不该把速度拉回任何初值");
        }

        [Test]
        public void ResumeGame_AfterFreezeAtZeroSpeed_StaysFrozen()
        {
            // 回归：先用 GameSpeed = 0 定格一局，再 Pause/Resume 一来一回，
            // 旧实现把速度弹回 s_GameSpeedBeforePause 的初值 1，画面突然动起来
            App.GameSpeed = 0f;
            Assert.IsFalse(App.IsGamePaused, "调到 0 速是慢放/定格，不算暂停请求");

            App.PauseGame();
            App.ResumeGame();

            Assert.AreEqual(0f, App.GameSpeed, Tolerance);
            Assert.AreEqual(0f, Time.timeScale, Tolerance, "重放的是实况期望速度，不是 1");
        }

        #endregion

        #region 期望速度 [EXPECTED SPEED]

        [Test]
        public void GameSpeed_WhilePaused_UpdatesTargetWithoutTouchingEngine()
        {
            App.PauseGame();

            App.GameSpeed = 8f;

            Assert.AreEqual(8f, App.GameSpeed, Tolerance, "期望速度即时可读");
            Assert.AreEqual(0f, Time.timeScale, Tolerance, "暂停优先于速度设定");

            App.ResumeGame();
            Assert.AreEqual(8f, Time.timeScale, Tolerance, "计数归零时重放暂停期间的写入");
        }

        [Test]
        public void GameSpeed_Negative_IsClampedToZero()
        {
            App.GameSpeed = -3f;

            Assert.AreEqual(0f, App.GameSpeed, Tolerance, "负速度按 0 处理");
            Assert.AreEqual(0f, Time.timeScale, Tolerance, "负值不该塞给引擎");
        }

        [Test]
        public void ResetGameSpeed_WhilePaused_UpdatesTargetWithoutResuming()
        {
            App.GameSpeed = 0.25f;
            App.PauseGame();

            App.ResetGameSpeed();

            Assert.IsTrue(App.IsGamePaused, "重置速度不该顺手解除暂停");
            Assert.IsTrue(App.IsNormalGameSpeed);
            Assert.AreEqual(0f, Time.timeScale, Tolerance);

            App.ResumeGame();
            Assert.AreEqual(1f, Time.timeScale, Tolerance);
        }

        #endregion

        #region 关闭态契约 [POST-SHUTDOWN CONTRACT]

        [Test]
        public void RuntimeStateApi_WhileFrameworkShutdown_StillDrivesEngine()
        {
            // 编辑模式下 Initialize 永不运行（唯一调用点是 play 态的 GameAppSettings.Initiation），
            // 此刻正是关闭态。运行态属性不判 IsShutdown：它是引擎状态的门面而非框架状态，
            // 关闭后调用不抛、立即作用于引擎，并在下一次 Initialize 时被重新播种成基线
            // （契约见 App.Shutdown 的注释与 App.md「运行态与暂停」一节）。
            Assert.IsTrue(App.IsShutdown, "本用例的前提是框架未启动");

            App.PauseGame();
            Assert.IsTrue(App.IsGamePaused);
            Assert.AreEqual(0f, Time.timeScale, Tolerance);

            App.ResumeGame();
            Assert.IsFalse(App.IsGamePaused);
            Assert.AreEqual(1f, Time.timeScale, Tolerance);
        }

        #endregion
    }
}
