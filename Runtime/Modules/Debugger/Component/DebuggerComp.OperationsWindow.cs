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
                    IObjectPoolService objectPoolService = ServiceSystem.GetService<IObjectPoolService>();
                    if (objectPoolService != null)
                    {
                        if (GUILayout.Button("Object Pool Release", GUILayout.Height(30f)))
                        {
                            objectPoolService.Release();
                        }

                        if (GUILayout.Button("Object Pool Release All Unused", GUILayout.Height(30f)))
                        {
                            objectPoolService.ReleaseAllUnused();
                        }
                    }

                    IResourceService resourceService = ServiceSystem.GetService<IResourceService>();
                    if (resourceService != null)
                    {
                        if (GUILayout.Button("Unload Unused Assets", GUILayout.Height(30f)))
                        {
                            resourceService.ForceUnloadUnusedAssets(false);
                        }

                        if (GUILayout.Button("Unload Unused Assets and Garbage Collect", GUILayout.Height(30f)))
                        {
                            resourceService.ForceUnloadUnusedAssets(true);
                        }
                        
                        if (GUILayout.Button("Shutdown Game Framework (None)", GUILayout.Height(30f)))
                        {
                            ServiceSystem.Shutdown();
                        }
                        if (GUILayout.Button("Shutdown Game Framework (Restart)", GUILayout.Height(30f)))
                        {
                            ServiceSystem.Shutdown();
                            SceneManager.LoadScene(0);
                        }
                        if (GUILayout.Button("Shutdown Game Framework (Quit)", GUILayout.Height(30f)))
                        {
                            ServiceSystem.Shutdown();
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