using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
	/// <summary>
	/// 信息提示特性：在 Inspector 中于目标字段上方或下方显示一条指定级别的提示消息。
	/// </summary>
	[Conditional("UNITY_EDITOR")]
	[System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
	public class InformationAttribute : PropertyAttribute
	{
		/// <summary>
		/// 提示消息的显示级别。
		/// </summary>
		public enum InformationType { Error, Info, None, Warning }

		/// <summary>
		/// 要显示的提示消息文本。
		/// </summary>
		public readonly string Message;
		/// <summary>
		/// 提示消息的显示级别。
		/// </summary>
		public readonly InformationType Type;
		/// <summary>
		/// 消息是否显示在字段下方（默认显示在上方）。
		/// </summary>
		public readonly bool MessageAfterProperty;

		/// <summary>
		/// 创建信息提示特性实例。
		/// </summary>
		/// <param name="message">要显示的提示消息文本。</param>
		/// <param name="type">提示消息的显示级别。</param>
		/// <param name="messageAfterProperty">消息是否显示在字段下方，默认显示在上方。</param>
		public InformationAttribute(string message, InformationType type, bool messageAfterProperty = false)
		{
			Message = message;
			Type = type;
			MessageAfterProperty = messageAfterProperty;
		}
	}
}