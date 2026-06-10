using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
	[Conditional("UNITY_EDITOR")]
	[System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
	public class InformationAttribute : PropertyAttribute
	{
		public enum InformationType { Error, Info, None, Warning }

		public readonly string Message;
		public readonly InformationType Type;
		public readonly bool MessageAfterProperty;

		public InformationAttribute(string message, InformationType type, bool messageAfterProperty = false)
		{
			Message = message;
			Type = type;
			MessageAfterProperty = messageAfterProperty;
		}
	}
}