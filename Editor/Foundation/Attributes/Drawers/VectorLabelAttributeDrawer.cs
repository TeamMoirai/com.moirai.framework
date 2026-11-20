using UnityEngine;
using UnityEditor;
#if UNITY_2021_3_OR_NEWER
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(VectorLabelAttribute))]
    public class VectorLabelAttributeDrawer : PropertyDrawer
    {
		#region IMGUI
		
	    protected static readonly GUIContent[] originalLabels = new GUIContent[] { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z"), new GUIContent("W") };
	    protected const int padding = 375;
		
		public override float GetPropertyHeight(SerializedProperty property, GUIContent guiContent)
		{
			int ratio = (padding > Screen.width) ? 2 : 1;
			return ratio * base.GetPropertyHeight(property, guiContent);
		}

		public override void OnGUI(Rect rect, SerializedProperty property, GUIContent guiContent)
		{
			VectorLabelAttribute vectorLabel = (VectorLabelAttribute)attribute;
            
			EditorGUI.BeginProperty(rect, guiContent, property);

			// 使用子属性而非直接向量访问
			if (property.propertyType == SerializedPropertyType.Vector2)
			{
				SerializedProperty[] props = new SerializedProperty[] { property.FindPropertyRelative("x"), property.FindPropertyRelative("y") };
				DrawFieldsWithProperties(rect, props, ObjectNames.NicifyVariableName(property.name), vectorLabel, guiContent);
			}
			else if (property.propertyType == SerializedPropertyType.Vector3)
			{
				SerializedProperty[] props = new SerializedProperty[] { property.FindPropertyRelative("x"), property.FindPropertyRelative("y"), property.FindPropertyRelative("z") };
				DrawFieldsWithProperties(rect, props, ObjectNames.NicifyVariableName(property.name), vectorLabel, guiContent);
			}
			else if (property.propertyType == SerializedPropertyType.Vector4)
			{
				SerializedProperty[] props = new SerializedProperty[] { property.FindPropertyRelative("x"), property.FindPropertyRelative("y"), property.FindPropertyRelative("z"), property.FindPropertyRelative("w") };
				DrawFieldsWithProperties(rect, props, ObjectNames.NicifyVariableName(property.name), vectorLabel, guiContent);
			}
			else if (property.propertyType == SerializedPropertyType.Vector2Int)
			{
				SerializedProperty[] props = new SerializedProperty[] { property.FindPropertyRelative("x"), property.FindPropertyRelative("y") };
				DrawFieldsWithProperties(rect, props, ObjectNames.NicifyVariableName(property.name), vectorLabel, guiContent);
			}
			else if (property.propertyType == SerializedPropertyType.Vector3Int)
			{
				SerializedProperty[] props = new SerializedProperty[] { property.FindPropertyRelative("x"), property.FindPropertyRelative("y"), property.FindPropertyRelative("z") };
				DrawFieldsWithProperties(rect, props, ObjectNames.NicifyVariableName(property.name), vectorLabel, guiContent);
			}

			EditorGUI.EndProperty();
		}

		protected void DrawFieldsWithProperties(Rect rect, SerializedProperty[] properties, string mainLabel, VectorLabelAttribute vectors, GUIContent originalGuiContent)
		{
			bool shortSpace = (Screen.width < padding);

			Rect mainLabelRect = rect;
			mainLabelRect.width = EditorGUIUtility.labelWidth;
			if (shortSpace)
			{
				mainLabelRect.height *= 0.5f;
			}

			Rect fieldRect = rect;
			if (shortSpace)
			{
				fieldRect.height *= 0.5f;
				fieldRect.y += fieldRect.height;
				fieldRect.width = rect.width / properties.Length;
			}
			else
			{
				fieldRect.x += mainLabelRect.width;
				fieldRect.width = (rect.width - mainLabelRect.width) / properties.Length;
			}

			GUIContent mainLabelContent = new GUIContent();
			mainLabelContent.text = mainLabel;
			mainLabelContent.tooltip = originalGuiContent.tooltip;
			EditorGUI.LabelField(mainLabelRect, mainLabelContent);

			for (int i = 0; i < properties.Length; i++)
			{
				GUIContent label = vectors.Labels.Length > i ? new GUIContent(vectors.Labels[i]) : originalLabels[i];
				Vector2 labelSize = EditorStyles.label.CalcSize(label);
				EditorGUIUtility.labelWidth = Mathf.Max(labelSize.x + 5, 0.3f * fieldRect.width);

				// 使用 EditorGUI.PropertyField 来正确处理多对象编辑
				EditorGUI.PropertyField(fieldRect, properties[i], label);

				fieldRect.x += fieldRect.width;
			}

			EditorGUIUtility.labelWidth = 0;
		}

		// 如需在其他地方保持向后兼容性，请保留旧方法
		private T[] DrawFields<T>(Rect rect, T[] vector, string mainLabel, System.Func<Rect, GUIContent, T, T> fieldDrawer, VectorLabelAttribute vectorLabel, GUIContent originalGuiContent)
		{
			T[] result = vector;

			bool shortSpace = (Screen.width < padding);

			Rect mainLabelRect = rect;
			mainLabelRect.width = EditorGUIUtility.labelWidth;
			if (shortSpace)
			{
				mainLabelRect.height *= 0.5f;
			}

			Rect fieldRect = rect;
			if (shortSpace)
			{
				fieldRect.height *= 0.5f;
				fieldRect.y += fieldRect.height;
				fieldRect.width = rect.width / vector.Length;
			}
			else
			{
				fieldRect.x += mainLabelRect.width;
				fieldRect.width = (rect.width - mainLabelRect.width) / vector.Length;
			}
			
			GUIContent mainLabelContent = new GUIContent();
			mainLabelContent.text = mainLabel;
			mainLabelContent.tooltip = originalGuiContent.tooltip;

			EditorGUI.LabelField(mainLabelRect, mainLabelContent);

			for (int i = 0; i < vector.Length; i++)
			{
				GUIContent label = vectorLabel.Labels.Length > i ? new GUIContent(vectorLabel.Labels[i]) : originalLabels[i];
				Vector2 labelSize = EditorStyles.label.CalcSize(label);
				EditorGUIUtility.labelWidth = Mathf.Max(labelSize.x + 5, 0.3f * fieldRect.width);
				result[i] = fieldDrawer(fieldRect, label, vector[i]);
				fieldRect.x += fieldRect.width;
			}

			EditorGUIUtility.labelWidth = 0;
			return result;
		}
		
		#endregion
		
// 		#region UI Toolkit
//
// #if UNITY_2021_3_OR_NEWER
// 	    
// 	    public override VisualElement CreatePropertyGUI(SerializedProperty property)
// 	    {
// 		    var root = new VisualElement(); 
// 		    VectorLabelsAttribute vector = (VectorLabelsAttribute)attribute;
//  
// 		    if (property.propertyType == SerializedPropertyType.Vector2)
// 		    {
// 			    var vector2Field = new Vector2Field
// 			    {
// 				    label = ObjectNames.NicifyVariableName(property.name),
// 				    value = property.vector2Value,
// 				    tooltip = property.tooltip
// 			    };
// 			    
// 	            // 设置自定义标签
// 	            var x = vector2Field.Q<FloatField>("unity-x-input").Q<Label>();
// 			    if (vector.Labels.Length > 0) x.text = vector.Labels[0];
// 	            x.style.minWidth = GetWidth(x.text);
// 	            
// 			    var y = vector2Field.Q<FloatField>("unity-y-input").Q<Label>();
// 			    if (vector.Labels.Length > 1) y.text = vector.Labels[1];
// 			    y.style.minWidth = GetWidth(y.text);
// 	            
// 			    root.Add(vector2Field);
// 		    }
// 			else if (property.propertyType == SerializedPropertyType.Vector3)
// 			{
// 				var vector3Field = new Vector3Field
// 			    {
// 				    label = ObjectNames.NicifyVariableName(property.name),
// 				    value = property.vector3Value,
// 				    tooltip = property.tooltip
// 			    };
// 			    
// 	            // 设置自定义标签
// 	            var x = vector3Field.Q<FloatField>("unity-x-input").Q<Label>();
// 				if (vector.Labels.Length > 0) x.text = vector.Labels[0];
// 	            x.style.minWidth = GetWidth(x.text);
// 	            
// 				var y = vector3Field.Q<FloatField>("unity-y-input").Q<Label>();
// 			    if (vector.Labels.Length > 1) y.text = vector.Labels[1];
// 			    y.style.minWidth = GetWidth(y.text);
// 			    
// 			    var z = vector3Field.Q<FloatField>("unity-z-input").Q<Label>();
// 			    if (vector.Labels.Length > 2) z.text = vector.Labels[2];
// 			    z.style.minWidth = GetWidth(z.text);
// 	            
// 			    root.Add(vector3Field);
// 			}
// 			else if (property.propertyType == SerializedPropertyType.Vector4)
// 			{
// 				var vector4Field = new Vector4Field
// 			    {
// 				    label = ObjectNames.NicifyVariableName(property.name),
// 				    value = property.vector4Value,
// 				    tooltip = property.tooltip
// 			    };
// 				
// 	            // 设置自定义标签
// 	            var x = vector4Field.Q<FloatField>("unity-x-input").Q<Label>();
// 				if (vector.Labels.Length > 0) x.text = vector.Labels[0];
// 	            x.style.minWidth = GetWidth(x.text);
// 	            
// 				var y = vector4Field.Q<FloatField>("unity-y-input").Q<Label>();
// 			    if (vector.Labels.Length > 1) y.text = vector.Labels[1];
// 			    y.style.minWidth = GetWidth(y.text);
// 			    
// 			    var z = vector4Field.Q<FloatField>("unity-z-input").Q<Label>();
// 			    if (vector.Labels.Length > 2) z.text = vector.Labels[2];
// 			    z.style.minWidth = GetWidth(z.text);
// 			    
// 			    var w = vector4Field.Q<FloatField>("unity-w-input").Q<Label>();
// 			    if (vector.Labels.Length > 3) w.text = vector.Labels[3];
// 			    w.style.minWidth = GetWidth(w.text);
// 	            
// 			    root.Add(vector4Field);
// 			}
// 			else if (property.propertyType == SerializedPropertyType.Vector2Int)
// 			{
// 				var vector2IntField = new Vector2IntField
// 			    {
// 				    label = ObjectNames.NicifyVariableName(property.name),
// 				    value = property.vector2IntValue,
// 				    tooltip = property.tooltip
// 			    };
// 			    
// 	            // 设置自定义标签
// 	            var x = vector2IntField.Q<IntegerField>("unity-x-input").Q<Label>();
// 			    if (vector.Labels.Length > 0) x.text = vector.Labels[0];
// 	            x.style.minWidth = GetWidth(x.text);
// 	            
// 			    var y = vector2IntField.Q<IntegerField>("unity-y-input").Q<Label>();
// 			    if (vector.Labels.Length > 1) y.text = vector.Labels[1];
// 			    y.style.minWidth = GetWidth(y.text);
// 	            
// 			    root.Add(vector2IntField);
// 			}
// 			else if (property.propertyType == SerializedPropertyType.Vector3Int)
// 			{
// 				var vector3IntField = new Vector3IntField
// 			    {
// 				    label = ObjectNames.NicifyVariableName(property.name),
// 				    value = property.vector3IntValue,
// 				    tooltip = property.tooltip
// 			    };
// 			    
// 	            // 设置自定义标签
// 	            var x = vector3IntField.Q<IntegerField>("unity-x-input").Q<Label>();
// 				if (vector.Labels.Length > 0) x.text = vector.Labels[0];
// 	            x.style.minWidth = GetWidth(x.text);
// 	            
// 				var y = vector3IntField.Q<IntegerField>("unity-y-input").Q<Label>();
// 			    if (vector.Labels.Length > 1) y.text = vector.Labels[1];
// 			    y.style.minWidth = GetWidth(y.text);
// 			    
// 			    var z = vector3IntField.Q<IntegerField>("unity-z-input").Q<Label>();
// 			    if (vector.Labels.Length > 2) z.text = vector.Labels[2];
// 			    z.style.minWidth = GetWidth(z.text);
// 	            
// 			    root.Add(vector3IntField);
// 			}
// 		    
// 		    return root;
// 	    }
//
// 	    // 单添加一些额外的空间
// 	    private float GetWidth(string label) => EditorStyles.label.CalcSize(new GUIContent(label)).x + 5;
// 	    
// #endif
//
// 	    #endregion

    }
}