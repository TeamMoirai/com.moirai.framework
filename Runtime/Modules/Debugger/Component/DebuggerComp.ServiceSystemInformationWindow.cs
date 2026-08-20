using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed class ServiceSystemInformationWindow : ScrollableDebuggerWindowBase
        {
            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Service System</b>");
                GUILayout.BeginVertical("box");
                {
                    var infos = GameServices.GetDiagnosticInfo();

                    int appCount = 0, sceneCount = 0, gameplayCount = 0;
                    int updateCount = 0, fixedUpdateCount = 0, lateUpdateCount = 0, gizmoCount = 0;

                    for (int i = 0; i < infos.Count; i++)
                    {
                        var info = infos[i];
                        if (info.Scope == EServiceScopeKind.App) appCount++;
                        else if (info.Scope == EServiceScopeKind.Scene) sceneCount++;
                        else gameplayCount++;
                        if (info.HasUpdate) updateCount++;
                        if (info.HasFixedUpdate) fixedUpdateCount++;
                        if (info.HasLateUpdate) lateUpdateCount++;
                        if (info.HasGizmo) gizmoCount++;
                    }

                    DrawItem("Registered Services", infos.Count.ToString());
                    DrawItem("  App Scope", appCount.ToString());
                    DrawItem("  Scene Scope", sceneCount.ToString());
                    DrawItem("  Gameplay Scope", gameplayCount.ToString());
                    DrawItem("  Update", updateCount.ToString());
                    DrawItem("  FixedUpdate", fixedUpdateCount.ToString());
                    DrawItem("  LateUpdate", lateUpdateCount.ToString());
                    DrawItem("  Gizmo", gizmoCount.ToString());

                    GUILayout.Space(4);

                    DrawItem("App Container", GameServices.AppContainer != null ? "Active" : "—");
                    DrawItem("Scene Container", GameServices.SceneContainer != null ? "Active" : "—");
                    DrawItem("Gameplay Container", GameServices.GameplayContainer != null ? "Active" : "—");
                }
                GUILayout.EndVertical();

                GUILayout.Space(8);

                GUILayout.Label("<b>Service List</b>");
                GUILayout.BeginVertical("box");
                {
                    var list = GameServices.GetDiagnosticInfo();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var info = list[i];
                        var flags = new List<string>(4);
                        if (info.HasUpdate) flags.Add("U");
                        if (info.HasFixedUpdate) flags.Add("F");
                        if (info.HasLateUpdate) flags.Add("L");
                        if (info.HasGizmo) flags.Add("G");

                        string tickStr = flags.Count > 0 ? string.Join(" ", flags) : "—";
                        DrawItem(
                            StringUtility.Format("[{0}] {1}", info.Scope, info.InterfaceType),
                            StringUtility.Format("{0} (P:{1} [{2}])", info.ImplementationType, info.Priority, tickStr));
                    }
                }
                GUILayout.EndVertical();
            }
        }
    }
}
