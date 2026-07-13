using System;
using System.Reflection;
using Moirai.Atropos.Attributes;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Moirai.Atropos
{
    /// <summary>
    /// 允许在指定路径中创建和保存 .curves 资源
    /// 此资源将包括来自 Tween 库的曲线（反曲线或非曲线），以便在任意需要动画曲线的地方使用
    /// </summary>
    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
    public class AnimationCurveGenerator : MonoBehaviour
    {
        [Header("保存设置 Save settings")]
        // 保存资源的路径
        public string AnimationCurveFilePath = "Assets/Tween/Editor/";
        // 资源的名称
        public string AnimationCurveFileName = "AnimationCurves.curves";

        [Header("动画曲线 Animation Curves")]
        // 点分辨率（越高越好）
        public int Resolution = 50;
        // 是否生成反曲线（y 从 1 到 0），false 为正曲线（y 从 0 到 1）
        public bool GenerateAntiCurves = false;
        
        [InspectorButton(nameof(GenerateAnimationCurvesAsset))]
        public bool GenerateAnimationCurvesButton;
        
        protected Type _scriptableObjectType;
        protected Keyframe _keyframe = new Keyframe();
        protected MethodInfo _addMethodInfo;
        protected object[] _parameters;
        
        /// <summary>
        /// 生成资源并将其保存在请求的路径中
        /// </summary>
        public virtual void GenerateAnimationCurvesAsset()
        {
            // 获取添加到对象的方法
            _scriptableObjectType = Type.GetType("UnityEditor.CurvePresetLibrary, UnityEditor");
            _addMethodInfo = _scriptableObjectType.GetMethod("Add");

            // 创建曲线资源的新实例
            ScriptableObject curveAsset = ScriptableObject.CreateInstance(_scriptableObjectType);
            
            // 对于每种类型的曲线，都会创建一个动画曲线
            foreach (EEaseType curve in Enum.GetValues(typeof(EEaseType)))
            {
                CreateAnimationCurve(curveAsset, curve, Resolution, GenerateAntiCurves);
            }

            // 将其保存到文件中
            #if UNITY_EDITOR
                AssetDatabase.CreateAsset(curveAsset, AnimationCurveFilePath + AnimationCurveFileName);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            #endif
        }

        /// <summary>
        /// 创建指定类型和分辨率的动画曲线，并将其添加到指定资源中
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="curveType"></param>
        /// <param name="curveResolution"></param>
        /// <param name="anti"></param>
        protected virtual void CreateAnimationCurve(ScriptableObject asset, EEaseType curveType, int curveResolution, bool anti)
        {
            // 生成动画曲线
            AnimationCurve animationCurve = new AnimationCurve();

            for (int i = 0; i < curveResolution; i++)
            {
                _keyframe.time = i / (curveResolution - 1f);
                if (anti)
                {
                    _keyframe.value = EaseUtility.Tween(_keyframe.time, 0f, 1f, 1f, 0f, curveType);
                }
                else
                {
                    _keyframe.value = EaseUtility.Tween(_keyframe.time, 0f, 1f, 0f, 1f, curveType);
                }
                animationCurve.AddKey(_keyframe);
            }
            // 平滑曲线的切线
            for (int j = 0; j < curveResolution; j++)
            {
                animationCurve.SmoothTangents(j, 0f);
            }

            // 将曲线添加到可编写脚本的对象中
            _parameters = new object[] { animationCurve, curveType.ToString() };
            _addMethodInfo.Invoke(asset, _parameters);

        }
    }
}
