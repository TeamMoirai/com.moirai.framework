using Moirai.Atropos.GameObjectPool;
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
                    IGameObjectPoolService gameObjectPoolService = GameServices.Provider?.GetService<IGameObjectPoolService>();
                    if (gameObjectPoolService != null)
                    {
                        if (GUILayout.Button("GameObject Pool Flush All", GUILayout.Height(30f)))
                        {
                            gameObjectPoolService.FlushAll();
                        }
                    }

                    IResourceService resourceService = GameServices.Provider?.GetService<IResourceService>();
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
                            GameServices.Shutdown();
                        }
                        if (GUILayout.Button("Shutdown Game Framework (Restart)", GUILayout.Height(30f)))
                        {
                            GameServices.Shutdown();
                            SceneManager.LoadScene(0);
                        }
                        if (GUILayout.Button("Shutdown Game Framework (Quit)", GUILayout.Height(30f)))
                        {
                            GameServices.Shutdown();
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