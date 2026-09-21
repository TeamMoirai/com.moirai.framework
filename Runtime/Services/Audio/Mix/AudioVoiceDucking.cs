using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 自动 Ducking：Voice 音轨有声在播时把混音切到 <see cref="EMixSnapshot.Dialogue"/>，全部播完再回落。
    /// <para>按「当前是否有 Voice 在播」评估而不是按播放/结束计数——计数一旦漏减就会永久压低混音，
    /// 而这两处判定都由各后端的声部表实算，漏一次 Tick 下次也会自动纠正。</para>
    /// <para>与快照状态机的优先级协同：duck 请求被更高优先级状态挡下时不记为自己生效，
    /// 回落也只在仍由本组件占着 Dialogue 时才做，不越权改写别人的混音。</para>
    /// </summary>
    internal static class AudioVoiceDucking
    {
        private static bool _ducked;
        private static EMixSnapshot _beforeDuck = EMixSnapshot.Default;

        /// <summary>
        /// 由后端 Tick 驱动。需要 <see cref="AudioServiceSettings.AutoDuckingOnVoice"/> 打开，
        /// 且混音快照里注册了 Dialogue，否则整条路径零成本。
        /// </summary>
        public static void Evaluate(AudioServiceHandler handler)
        {
            if (!AudioServiceSettings.AutoDuckingOnVoice)
            {
                // 运行中被关掉：把还挂着的 duck 落回去，不然混音会卡在 Dialogue
                if (_ducked) Release();
                return;
            }

            bool want = handler != null && handler.HasActiveAudioOn(EAudioTrack.Voice);
            if (want == _ducked) return;

            if (want)
            {
                _beforeDuck = AudioMixService.Current;
                // Request 在优先级不足时返回 false：此时不记为已 duck，回落也不做
                _ducked = AudioMixService.Request(EMixSnapshot.Dialogue, DuckBlendSeconds);
                return;
            }

            Release();
        }

        /// <summary>清空 duck 记账（服务重启/关停时调用）。</summary>
        public static void Reset()
        {
            _ducked = false;
            _beforeDuck = EMixSnapshot.Default;
        }

        private static void Release()
        {
            _ducked = false;

            // 只有还占着 Dialogue 才回收；已被其它状态接走说明归属已转移
            if (AudioMixService.Current != EMixSnapshot.Dialogue) return;

            // 回落必然是降优先级（Default 为 0），force 是这里的语义：归还自己借走的那一层
            AudioMixService.Request(_beforeDuck, DuckBlendSeconds, force: true);
        }

        /// <summary>duck 进/出的交叉淡变时长（秒）。</summary>
        public const float DuckBlendSeconds = 0.25f;
    }
}
