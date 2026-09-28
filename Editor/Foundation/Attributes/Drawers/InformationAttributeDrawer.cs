using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
	/// <summary>
	/// 在序列化属性旁（之前或之后）显示消息框（警告、信息、错误等）。
	/// </summary>
	[CustomPropertyDrawer(typeof(InformationAttribute))]
	public class InformationAttributeDrawer : PropertyDrawer
	{
		// 决定帮助框之后的间距、文本框之前的间距，以及帮助框图标的宽度
		const int SPACE_BEFORE_THE_TEXT_BOX = 5;
		const int SPACE_AFTER_THE_TEXT_BOX = 10;
		const int ICON_WIDTH = 55;

		InformationAttribute informationAttribute { get { return ((InformationAttribute)attribute); } }

		/// <summary>
		/// 按指定顺序绘制属性与消息框。
		/// </summary>
		/// <param name="rect">整体绘制区域。</param>
		/// <param name="prop">序列化属性。</param>
		/// <param name="label">属性标签。</param>
		public override void OnGUI(Rect rect, SerializedProperty prop, GUIContent label)
		{
			EditorStyles.helpBox.richText = true;
			Rect helpPosition = rect;
			Rect textFieldPosition = rect;

			if (!informationAttribute.MessageAfterProperty)
			{
				// 将消息框放置在属性之前
				helpPosition.height = DetermineTextboxHeight(informationAttribute.Message);

				textFieldPosition.y += helpPosition.height + SPACE_BEFORE_THE_TEXT_BOX;
				textFieldPosition.height = GetPropertyHeight(prop, label);
			}
			else
			{
				// 先放置属性，再放置消息框
				textFieldPosition.height = GetPropertyHeight(prop, label);

				helpPosition.height = DetermineTextboxHeight(informationAttribute.Message);
				// 加上整体高度（属性 + 消息框，即本脚本中重写的 GetPropertyHeight 所得），再将两者相减即可得到仅属性区域的位置
				helpPosition.y += GetPropertyHeight(prop, label) - DetermineTextboxHeight(informationAttribute.Message) - SPACE_AFTER_THE_TEXT_BOX;
			}

			MessageType messageType = informationAttribute.Type switch
			{
				InformationAttribute.InformationType.Error => MessageType.Error,
				InformationAttribute.InformationType.Info => MessageType.Info,
				InformationAttribute.InformationType.None => MessageType.None,
				InformationAttribute.InformationType.Warning => MessageType.Warning,
				_ => MessageType.Info
			};
			
			EditorGUI.HelpBox(helpPosition, informationAttribute.Message, messageType);
			EditorGUI.PropertyField(textFieldPosition, prop, label, true);
		}

		/// <summary>
		/// 返回整个块（属性 + 帮助文本）的完整高度。
		/// </summary>
		/// <returns>块的完整高度。</returns>
		/// <param name="property">序列化属性。</param>
		/// <param name="label">属性标签。</param>
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUI.GetPropertyHeight(property) + DetermineTextboxHeight(informationAttribute.Message) + SPACE_AFTER_THE_TEXT_BOX + SPACE_BEFORE_THE_TEXT_BOX;
		}

		/// <summary>
		/// 计算消息框的高度。
		/// </summary>
		/// <returns>消息框高度。</returns>
		/// <param name="message">消息文本。</param>
		protected virtual float DetermineTextboxHeight(string message)
		{
			GUIStyle style = new GUIStyle(EditorStyles.helpBox);
			style.richText = true;

			float newHeight = style.CalcHeight(new GUIContent(message), EditorGUIUtility.currentViewWidth - ICON_WIDTH);
			return newHeight;
		}
	}
}