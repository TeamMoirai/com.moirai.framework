using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class OperationsWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Operations</b>");
                GUILayout.BeginVertical("box");
                {
                    IObjectPoolModule objectPoolModule = ModuleSystem.GetModule<IObjectPoolModule>();
                    if (objectPoolModule != null)
                    {
                        if (GUILayout.Button("Object Pool Release", GUILayout.Height(30f)))
                        {
                            objectPoolModule.Release();
                        }

                        if (GUILayout.Button("Object Pool Release All Unused", GUILayout.Height(30f)))
                        {
                            objectPoolModule.ReleaseAllUnused();
                        }
                    }

                    IResourceModule resourceModule = ModuleSystem.GetModule<IResourceModule>();
                    if (resourceModule != null)
                    {
                        if (GUILayout.Button("Unload Unused Assets", GUILayout.Height(30f)))
                        {
                            resourceModule.ForceUnloadUnusedAssets(false);
                        }

                        if (GUILayout.Button("Unload Unused Assets and Garbage Collect", GUILayout.Height(30f)))
                        {
                            resourceModule.ForceUnloadUnusedAssets(true);
                        }
                        
                        if (GUILayout.Button("Shutdown Game Framework (None)", GUILayout.Height(30f)))
                        {
                            ModuleSystem.Shutdown();
                        }
                        if (GUILayout.Button("Shutdown Game Framework (Restart)", GUILayout.Height(30f)))
                        {
                            ModuleSystem.Shutdown();
                            SceneManager.LoadScene(0);
                        }
                        if (GUILayout.Button("Shutdown Game Framework (Quit)", GUILayout.Height(30f)))
                        {
                            ModuleSystem.Shutdown();
                            Application.Quit();
#if UNITY_EDITOR
                            UnityEditor.EditorApplication.isPlaying = false;
#endif
                        }
                    }
                }
                GUILayout.EndVertical();
            }
        }
    }
}