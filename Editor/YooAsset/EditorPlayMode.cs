using Moirai.Atropos.Resource;
using UnityEditor;

namespace YooAsset.Editor
{
    public static class EditorPlayMode
    {
        private const int MENU_ITEM_PRIORITY = -100;

        #region EditorMode

        [MenuItem("YooAsset/Editor PlayMode/EditorMode (编辑器下的模拟模式)", false, MENU_ITEM_PRIORITY)]
        public static void EditorMode()
        {
            EditorPrefs.SetInt(ResourceModuleDriver.EDITOR_PLAY_MODE_KEY, (int)EPlayMode.EditorSimulateMode);
        }

        [MenuItem("YooAsset/Editor PlayMode/EditorMode (编辑器下的模拟模式)", true)]
        public static bool EditorModeValidation()
        {
            return EditorPrefs.GetInt(ResourceModuleDriver.EDITOR_PLAY_MODE_KEY) != (int)EPlayMode.EditorSimulateMode;
        }

        #endregion

        #region OfflinePlayMode

        [MenuItem("YooAsset/Editor PlayMode/OfflinePlayMode (单机模式)", false, MENU_ITEM_PRIORITY + 1)]
        public static void OfflinePlayMode()
        {
            EditorPrefs.SetInt(ResourceModuleDriver.EDITOR_PLAY_MODE_KEY, (int)EPlayMode.OfflinePlayMode);
        }

        [MenuItem("YooAsset/Editor PlayMode/OfflinePlayMode (单机模式)", true)]
        public static bool OfflinePlayModeValidation()
        {
            return EditorPrefs.GetInt(ResourceModuleDriver.EDITOR_PLAY_MODE_KEY) != (int)EPlayMode.OfflinePlayMode;
        }

        #endregion

        #region HostPlayMode

        [MenuItem("YooAsset/Editor PlayMode/HostPlayMode (联机运行模式)", false, MENU_ITEM_PRIORITY + 2)]
        public static void HostPlayMode()
        {
            EditorPrefs.SetInt(ResourceModuleDriver.EDITOR_PLAY_MODE_KEY, (int)EPlayMode.HostPlayMode);
        }

        [MenuItem("YooAsset/Editor PlayMode/HostPlayMode (联机运行模式)", true)]
        public static bool HostPlayModeValidation()
        {
            return EditorPrefs.GetInt(ResourceModuleDriver.EDITOR_PLAY_MODE_KEY) != (int)EPlayMode.HostPlayMode;
        }

        #endregion

        #region HostPlayMode

        [MenuItem("YooAsset/Editor PlayMode/WebGLPlayMode (WebGL运行模式)", false, MENU_ITEM_PRIORITY + 3)]
        public static void WebPlayMode()
        {
            EditorPrefs.SetInt(ResourceModuleDriver.EDITOR_PLAY_MODE_KEY, (int)EPlayMode.WebPlayMode);
        }

        [MenuItem("YooAsset/Editor PlayMode/WebGLPlayMode (WebGL运行模式)", true)]
        public static bool WebPlayModeValidation()
        {
            return EditorPrefs.GetInt(ResourceModuleDriver.EDITOR_PLAY_MODE_KEY) != (int)EPlayMode.WebPlayMode;
        }

        #endregion
    }
}