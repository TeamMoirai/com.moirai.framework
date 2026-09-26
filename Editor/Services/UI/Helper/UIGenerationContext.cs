using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    internal sealed class UIGenerationContext
    {
        public UIGenerationContext(GameObject targetObject, UIScriptGenerateData scriptGenerateData, IReadOnlyList<UIBindData> bindData)
        {
            TargetObject = targetObject;
            ScriptGenerateData = scriptGenerateData;
            BindData = bindData;
        }

        public GameObject TargetObject { get; }

        public UIScriptGenerateData ScriptGenerateData { get; }

        public IReadOnlyList<UIBindData> BindData { get; }

        public string AssetPath { get; set; }

        public string ClassName { get; set; }

        public string FullTypeName =>
            string.IsNullOrWhiteSpace(ScriptGenerateData?.NameSpace) ? ClassName : $"{ScriptGenerateData.NameSpace}.{ClassName}";
    }

    internal readonly struct UIGenerationValidationResult
    {
        private UIGenerationValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            Message = message;
        }

        public bool IsValid { get; }

        public string Message { get; }

        public static UIGenerationValidationResult Success() => new(true, string.Empty);

        public static UIGenerationValidationResult Fail(string message) => new(false, message);
    }
}