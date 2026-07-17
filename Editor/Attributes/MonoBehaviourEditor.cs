using System.Reflection;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor
{
    /// <summary>
    /// 该类为所有 MonoBehaviour 提供了一个基础的自定义编辑器
    /// </summary>
    [CustomEditor(typeof(MonoBehaviour), true)]
    [CanEditMultipleObjects]
    public class MonoBehaviourEditor : OdinEditor
    {
        // Repainting
        private bool _requireRepaint;
        private bool _runtimeRepaint;

        protected override void OnEnable()
        {
            base.OnEnable();

            var attr = serializedObject.targetObject.GetType().GetCustomAttribute<ConstantRepaintAttribute>();
            if (attr != null)
            {
                _requireRepaint = true;

                if (attr.runtimeOnly)
                {
                    _runtimeRepaint = true;
                }
            }
        }

        public override bool RequiresConstantRepaint()
        {
            if (_requireRepaint)
            {
                if (_runtimeRepaint)
                {
                    return Application.isPlaying;
                }
                return true;
            }
            return false;
        }
    }
}