using System;
using System.Diagnostics;

namespace Sirenix.OdinInspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    [Conditional("UNITY_EDITOR")]
    public class ConditionDropdownAttribute : ValueDropdownAttribute
    {
        /// <summary>
        /// Optional resolved string that specifies a condition for whether to show the inline button or not.
        /// </summary>
        public string ShowIf;

        public ConditionDropdownAttribute(string valuesGetter) : base(valuesGetter)
        {
        }
    }
}