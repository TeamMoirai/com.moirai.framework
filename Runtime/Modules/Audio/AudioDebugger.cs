using System;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频调试用
    /// </summary>
    public class AudioDebugger : MonoBehaviour
    {

    }

    #if UNITY_EDITOR

    [UnityEditor.CustomEditor(typeof(AudioDebugger))]
    public class AudioDebuggerEditor : UnityEditor.Editor
    {
        struct TrackStruct
        {
            public AudioTrack track;
            public bool isMaster;
            public float volume;
            public Color colorMute;
            public Color colorUnmute;
            public Color colorPause;
            public Color colorUnPause;
            public Color colorStop;

            public TrackStruct(AudioTrack track, float volume, Color colorMute, Color colorUnmute, Color colorPause, Color colorUnPause, Color colorStop)
            {
                this.track = track;
                isMaster = false;
                this.volume = volume;
                this.colorMute = colorMute;
                this.colorUnmute = colorUnmute;
                this.colorPause = colorPause;
                this.colorUnPause = colorUnPause;
                this.colorStop = colorStop;
            }

            public TrackStruct(float volume, Color colorMute, Color colorUnmute, Color colorPause, Color colorUnPause, Color colorStop)
            {
                track = default;
                isMaster = true;
                this.volume = volume;
                this.colorMute = colorMute;
                this.colorUnmute = colorUnmute;
                this.colorPause = colorPause;
                this.colorUnPause = colorUnPause;
                this.colorStop = colorStop;
            }
        }

        IAudioModule _target;

        private TrackStruct[] _tracks;
        private readonly Color[] _colorBases = new []{ ColorsUtility.YellowAmber, ColorsUtility.Coral, ColorsUtility.SteelBlue, ColorsUtility.Aquamarine, ColorsUtility.Burlywood };

        private readonly Color _baseColor = new Color32(150, 150, 150, 255);
        private readonly ColorExtensions.ColoringMode _coloringMode = ColorExtensions.ColoringMode.Add;

        public override bool RequiresConstantRepaint() => true;

        private void OnEnable()
        {
            _target = GameModule.Audio;

            // 初始化音频轨道
            if (UnityEditor.EditorApplication.isPlaying)
            {
                _tracks = new TrackStruct[_target.AudioCategories.Length + 1];

                // 添加主轨道
                _tracks[0] = new TrackStruct(1f,
                    _baseColor.Colorize(_colorBases[0], _coloringMode, 1f),
                    _baseColor.Colorize(_colorBases[0], _coloringMode, 0.9f),
                    _baseColor.Colorize(_colorBases[0], _coloringMode, 0.8f),
                    _baseColor.Colorize(_colorBases[0], _coloringMode, 0.7f),
                    _baseColor.Colorize(_colorBases[0], _coloringMode, 0.5f)
                );

                // 添加其他配置音轨
                for (int i = 0; i < _target.AudioCategories.Length; i++)
                {
                    var track = _target.AudioCategories[i].AudioTrack;
                    var baseColor = _colorBases[i + 1];
                    _tracks[i + 1] = new TrackStruct(track, 1f,
                        _baseColor.Colorize(baseColor, _coloringMode, 1f),
                        _baseColor.Colorize(baseColor, _coloringMode, 0.9f),
                        _baseColor.Colorize(baseColor, _coloringMode, 0.8f),
                        _baseColor.Colorize(baseColor, _coloringMode, 0.7f),
                        _baseColor.Colorize(baseColor, _coloringMode, 0.5f));
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (UnityEditor.EditorApplication.isPlaying)
            {
                // todo 绘制音频状态
                foreach (var track in _tracks)
                {
                    DrawTrack(track);
                }

                if (GUILayout.Button("Save Settings"))
                {
                    AudioModuleEvent.Trigger(AudioModuleEventType.SetSettings);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 为指定的音轨绘制相关控件
        /// </summary>
        /// <param name="trackStruct"></param>
        private void DrawTrack(TrackStruct trackStruct)
        {
            GUILayout.Space(10);
            GUILayout.Label(trackStruct.isMaster ? "Master" : trackStruct.track.ToString(), UnityEditor.EditorStyles.boldLabel);

            // 绘制音量滑动条
            UnityEditor.EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Volume");
            float newVolume = 0;
            if (trackStruct.isMaster)
            {
                trackStruct.volume = _target.MasterVolume;
                newVolume = UnityEditor.EditorGUILayout.Slider(trackStruct.volume, 0, 1);
                if (!Mathf.Approximately(newVolume, trackStruct.volume)) { _target.MasterVolume = newVolume; }
            }
            else
            {
                trackStruct.volume = _target.GetTrackVolume(trackStruct.track);
                newVolume = UnityEditor.EditorGUILayout.Slider(trackStruct.volume, 0, AudioGroupConfig.MAXIMAL_VOLUME);
                if (!Mathf.Approximately(newVolume, trackStruct.volume)) { _target.SetTrackVolume(trackStruct.track, newVolume); }
            }
            UnityEditor.EditorGUILayout.EndHorizontal();

            // 绘制功能按钮
            UnityEditor.EditorGUILayout.BeginHorizontal();
            {
                if (trackStruct.isMaster)
                {
                    bool mute = _target.MasterMute;
                    UnityEditor.EditorGUI.BeginDisabledGroup(mute);
                    {
                        DrawColoredButton("Mute", trackStruct.colorMute, () => _target.MasterMute = true, UnityEditor.EditorStyles.miniButtonLeft);
                    }
                    UnityEditor.EditorGUI.EndDisabledGroup();
                    UnityEditor.EditorGUI.BeginDisabledGroup(!mute);
                    {
                        DrawColoredButton("Unmute", trackStruct.colorUnmute, () => _target.MasterMute = false, UnityEditor.EditorStyles.miniButtonMid);
                    }
                    UnityEditor.EditorGUI.EndDisabledGroup();

                    DrawColoredButton("Pause", trackStruct.colorPause, () => _target.PauseAll(), UnityEditor.EditorStyles.miniButtonMid);
                    DrawColoredButton("UnPause", trackStruct.colorUnPause, () => _target.UnPauseAll(), UnityEditor.EditorStyles.miniButtonMid);
                    DrawColoredButton("Stop", trackStruct.colorStop, () => _target.StopAll(), UnityEditor.EditorStyles.miniButtonMid);
                }
                else
                {
                    bool mute = _target.GetTrackMute(trackStruct.track);
                    UnityEditor.EditorGUI.BeginDisabledGroup(mute);
                    {
                        DrawColoredButton("Mute", trackStruct.colorMute, () => _target.SetTrackMute(trackStruct.track, true), UnityEditor.EditorStyles.miniButtonLeft);
                    }
                    UnityEditor.EditorGUI.EndDisabledGroup();
                    UnityEditor.EditorGUI.BeginDisabledGroup(!mute);
                    {
                        DrawColoredButton("Unmute", trackStruct.colorUnmute, () => _target.SetTrackMute(trackStruct.track, false), UnityEditor.EditorStyles.miniButtonMid);
                    }
                    UnityEditor.EditorGUI.EndDisabledGroup();

                    DrawColoredButton("Pause", trackStruct.colorPause, () => _target.Pause(trackStruct.track), UnityEditor.EditorStyles.miniButtonMid);
                    DrawColoredButton("UnPause", trackStruct.colorUnPause, () => _target.UnPause(trackStruct.track), UnityEditor.EditorStyles.miniButtonMid);
                    DrawColoredButton("Stop", trackStruct.colorStop, () => _target.Stop(trackStruct.track), UnityEditor.EditorStyles.miniButtonMid);
                }
            }
            UnityEditor.EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制按钮
        /// </summary>
        /// <param name="buttonLabel"></param>
        /// <param name="buttonColor"></param>
        /// <param name="action"></param>
        /// <param name="styles"></param>
        private void DrawColoredButton(string buttonLabel, Color buttonColor, Action action, GUIStyle styles)
        {
            var originalBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = buttonColor;
            if (GUILayout.Button(buttonLabel, styles))
            {
                action.Invoke();
            }
            GUI.backgroundColor = originalBackgroundColor;
        }
    }

    #endif
}