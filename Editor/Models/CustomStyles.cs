using UnityEngine;

namespace Moirai.Atropos.Editor
{
    public static class CustomStyles
    {
        private static GUIStyle _errorText;
        public static GUIStyle ErrorTextStyle
        {
            get
            {
                if (_errorText == null)
                {
                    _errorText = new GUIStyle(GUI.skin.label);
                    _errorText.normal.textColor = ColorsUtility.BestRed;
                    _errorText.wordWrap = true;
                }
                return _errorText;
            }
        }
        
        private static GUIStyle _infoText;
        public static GUIStyle InfoTextStyle
        {
            get
            {
                if (_infoText == null)
                {
                    _infoText = new GUIStyle(GUI.skin.box);
                    _infoText.alignment = TextAnchor.MiddleLeft;
                    _infoText.normal.textColor = ColorsUtility.Teal;
                    _infoText.wordWrap = true;
                }
                return _infoText;
            }
        }
    }
}