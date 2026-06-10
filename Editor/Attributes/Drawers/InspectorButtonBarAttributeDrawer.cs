using System.Reflection;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_3_OR_NEWER
using UnityEngine.UIElements;
using UnityEditor.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(InspectorButtonBarAttribute))]
    public class InspectorButtonBarAttributeDrawer : PropertyDrawer
    {
        private MethodInfo[] _eventMethodInfos = null;

        #region IMGUI

        private float _buttonHeight = 0f;
        private float _propertyHeight = 0f;
        private const float SpaceHeight = 5f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
	        if (_buttonHeight == 0)
	        {
		        InspectorButtonBarAttribute inspectorButtonBarAttribute = (InspectorButtonBarAttribute)attribute;
		   
		        _buttonHeight = base.GetPropertyHeight(property, label);
		        _propertyHeight = (_buttonHeight + SpaceHeight) * inspectorButtonBarAttribute.Labels.Length;
	        }
	        return _propertyHeight;
        }


        private readonly GUIStyle _enableStyle = new GUIStyle("ButtonMid");
        private readonly GUIStyle _disableStyle = new GUIStyle("ProjectBrowserTopBarBg");
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
	        InspectorButtonBarAttribute inspectorButtonBarAttribute = (InspectorButtonBarAttribute)attribute;

	        if (_eventMethodInfos == null)
	        {
		        _eventMethodInfos = new MethodInfo[inspectorButtonBarAttribute.Methods.Length];
	        }
	        
	        float buttonLength = position.width;
	        Rect buttonRect = new Rect(position.x, position.y + SpaceHeight, buttonLength, _buttonHeight);
	      
	        for (int i = 0; i < inspectorButtonBarAttribute.Labels.Length; i++)
	        {
		        if (inspectorButtonBarAttribute.OnlyWhenPlaying[i])
		        {
			        GUI.Label(buttonRect, inspectorButtonBarAttribute.Labels[i], _disableStyle);
		        }
		        else
		        {
			        if (GUI.Button(buttonRect, inspectorButtonBarAttribute.Labels[i], _enableStyle))
			        {
				        System.Type eventOwnerType = property.serializedObject.targetObject.GetType(); 
				        string eventName = inspectorButtonBarAttribute.Methods[i];

				        if (_eventMethodInfos[i] == null)
				        {
					        _eventMethodInfos[i] = eventOwnerType.GetMethod(eventName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				        }

				        if (_eventMethodInfos[i] != null)
				        {
					        _eventMethodInfos[i].Invoke(property.serializedObject.targetObject, null);
				        }
				        else
				        {
					        Debug.LogWarning($"InspectorButton: Unable to find method {eventName} in {eventOwnerType}");
				        }
			        }
		        }
		        
		        buttonRect.y += _buttonHeight + SpaceHeight;
	        }
        }
        
        #endregion

        #region UI Toolkit

#if UNITY_2021_3_OR_NEWER
	    
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
	        InspectorButtonBarAttribute inspectorButtonBarAttribute = (InspectorButtonBarAttribute)attribute;
	        System.Type eventOwnerType = property.serializedObject.targetObject.GetType();

	        // add our root
	        var root = new VisualElement();

	        // add toolbar
	        Toolbar moveToControls = new Toolbar();
	        moveToControls.styleSheets.Add(Resources.Load<StyleSheet>(AttributesStaticRef.UITK_Toolbar));
	        moveToControls.AddToClassList("toolbar-field");

	        if (_eventMethodInfos == null)
	        {
		        _eventMethodInfos = new MethodInfo[inspectorButtonBarAttribute.Methods.Length];
	        }

	        // add each button
	        for (var i = 0; i < inspectorButtonBarAttribute.Labels.Length; i++)
	        {
		        var newButton = new ToolbarButton();
		        newButton.text = inspectorButtonBarAttribute.Labels[i];
		        newButton.style.flexGrow = 1;

		        if (inspectorButtonBarAttribute.UssClass[i] != "")
		        {
			        newButton.AddToClassList(inspectorButtonBarAttribute.UssClass[i]);
		        }

		        if (_eventMethodInfos[i] == null)
		        {
			        _eventMethodInfos[i] = eventOwnerType.GetMethod(inspectorButtonBarAttribute.Methods[i],
				        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		        }

		        if (_eventMethodInfos[i] != null)
		        {
			        var i1 = i;
			        newButton.clicked += () =>
				        _eventMethodInfos[i1].Invoke(property.serializedObject.targetObject, null);
		        }
		        else
		        {
			        Debug.LogWarning(string.Format("InspectorButton: Unable to find method {0} in {1}",
				        inspectorButtonBarAttribute.Methods[i], eventOwnerType));
		        }

		        if (inspectorButtonBarAttribute.OnlyWhenPlaying[i] && !Application.isPlaying)
		        {
			        newButton.SetEnabled(false);
		        }

		        moveToControls.Add(newButton);
	        }

	        root.Add(moveToControls);

	        return root;
        }
#endif
        
        #endregion
    }
}