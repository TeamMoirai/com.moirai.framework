using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Toolbar = UnityEditor.UIElements.Toolbar;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Events.Editor
{
    /// <summary>
    /// 事件协调器下拉选项抽象接口，用于在调试器工具栏中呈现可选的事件协调器。
    /// </summary>
    internal interface ICoordinatorChoice
    {
        /// <summary>
        /// 获取选项对应的事件协调器。
        /// </summary>
        MonoEventCoordinator Coordinator { get; }
    }

    /// <summary>
    /// 基于 <see cref="MonoEventCoordinator"/> 的下拉选项默认实现。
    /// </summary>
    internal class CoordinatorChoice : ICoordinatorChoice
    {
        /// <summary>
        /// 获取选项对应的事件协调器。
        /// </summary>
        public MonoEventCoordinator Coordinator { get; }

        /// <summary>
        /// 使用指定的事件协调器创建选项。
        /// </summary>
        /// <param name="p">选项对应的事件协调器。</param>
        public CoordinatorChoice(MonoEventCoordinator p)
        {
            Coordinator = p;
        }

        /// <summary>
        /// 返回选项的显示文本，即协调器所在 GameObject 的名称。
        /// </summary>
        public override string ToString()
        {
            return Coordinator.gameObject.name;
        }
    }

    /// <summary>
    /// 事件协调器调试器基类，负责在调试器窗口工具栏中发现、选择并连接场景中的事件协调器。
    /// </summary>
    [Serializable]
    internal class CoordinatorDebugger : ICoordinatorDebugger
    {
        [SerializeField] private string m_LastVisualTreeName;

        protected EditorWindow m_DebuggerWindow;
        private ICoordinatorChoice m_SelectedCoordinator;
        protected VisualElement m_Toolbar;
        protected ToolbarMenu m_CoordinatorSelect;
        private List<ICoordinatorChoice> m_CoordinatorChoices;
        private IVisualElementScheduledItem m_ConnectWindowScheduledItem;
        private IVisualElementScheduledItem m_RestoreSelectionScheduledItem;

        /// <summary>
        /// 获取或设置当前正在调试的事件协调器。
        /// </summary>
        public MonoEventCoordinator CoordinatorDebug { get; set; }

        /// <summary>
        /// 使用宿主调试器窗口初始化调试器，并构建协调器选择工具栏。
        /// </summary>
        /// <param name="debuggerWindow">承载该调试器的编辑器窗口。</param>
        public void Initialize(EditorWindow debuggerWindow)
        {
            m_DebuggerWindow = debuggerWindow;

            m_Toolbar ??= new Toolbar();

            // 在工具栏上注册面板选项刷新回调，确保该事件先于 ToolbarPopup 的 clickable 得到处理。
            m_Toolbar.RegisterCallback<MouseDownEvent>((e) =>
            {
                if (e.target == m_CoordinatorSelect)
                    RefreshCoordinatorChoices();
            }, UnityEngine.UIElements.TrickleDown.TrickleDown);

            m_CoordinatorChoices = new List<ICoordinatorChoice>();
            m_CoordinatorSelect = new ToolbarMenu
            {
                name = "coordinatorSelectPopup",
                variant = ToolbarMenu.Variant.Popup,
                text = "Select a coordinator"
            };

            m_Toolbar.Insert(0, m_CoordinatorSelect);

            if (!string.IsNullOrEmpty(m_LastVisualTreeName))
                m_RestoreSelectionScheduledItem = m_Toolbar.schedule.Execute(RestoreCoordinatorSelection).Every(500);
        }

        /// <summary>
        /// 禁用时与当前协调器解除连接，并保留上次选中的树名称以便日后恢复。
        /// </summary>
        public void OnDisable()
        {
            var lastTreeName = m_LastVisualTreeName;
            SelectCoordinatorChoice(null);
            if (CoordinatorDebug) CoordinatorDebug.DetachDebugger(this);
            m_LastVisualTreeName = lastTreeName;
        }

        /// <summary>
        /// 与当前调试的协调器断开连接。
        /// </summary>
        public void Disconnect()
        {
            var lastTreeName = m_LastVisualTreeName;
            m_SelectedCoordinator = null;
            SelectCoordinatorChoice(null);

            m_LastVisualTreeName = lastTreeName;
        }

        /// <summary>
        /// 调度自动连接：每 500 毫秒查找一次场景中的协调器，找到后自动选中。
        /// </summary>
        /// <param name="window">发起自动连接的编辑器窗口。</param>
        public void ScheduleWindowToDebug(EditorWindow window)
        {
            if (window != null)
            {
                Disconnect();
                m_ConnectWindowScheduledItem = m_Toolbar.schedule.Execute(TrySelectWindow).Every(500);
            }
        }

        private void TrySelectWindow()
        {
            MonoEventCoordinator monoEventCoordinator = Object.FindAnyObjectByType<MonoEventCoordinator>();
            SelectCoordinatorToDebug(monoEventCoordinator);

            if (m_SelectedCoordinator != null)
            {
                m_ConnectWindowScheduledItem.Pause();
            }
        }

        /// <summary>
        /// 刷新调试器显示；默认实现为空，由子类按需重写。
        /// </summary>
        public virtual void Refresh()
        { }

        /// <summary>
        /// 校验指定协调器能否与该调试器建立连接。
        /// </summary>
        /// <param name="connection">待校验的事件协调器。</param>
        /// <returns>允许连接返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        protected virtual bool ValidateDebuggerConnection(IEventCoordinator connection)
        {
            return true;
        }

        /// <summary>
        /// 选中协调器后回调；默认实现为空，由子类按需重写。
        /// </summary>
        /// <param name="pdbg">新选中的事件协调器，取消选中时为 <c>null</c>。</param>
        protected virtual void OnSelectCoordinateDebug(IEventCoordinator pdbg) { }

        /// <summary>
        /// 恢复上次选中的协调器后回调；默认实现为空，由子类按需重写。
        /// </summary>
        protected virtual void OnRestoreCoordinatorSelection() { }

        /// <summary>
        /// 收集场景中所有事件协调器，填充下拉选项列表。
        /// </summary>
        /// <param name="coordinatorChoices">待填充的选项列表。</param>
        protected virtual void PopulateCoordinatorChoices(List<ICoordinatorChoice> coordinatorChoices)
        {
            MonoEventCoordinator[] monoEventCoordinators = Object.FindObjectsByType<MonoEventCoordinator>(FindObjectsSortMode.InstanceID);
            coordinatorChoices.AddRange(monoEventCoordinators.Select(x => new CoordinatorChoice(x)));
        }

        private void RefreshCoordinatorChoices()
        {
            m_CoordinatorChoices.Clear();
            PopulateCoordinatorChoices(m_CoordinatorChoices);

            var menu = m_CoordinatorSelect.menu;
            menu.ClearItems();

            foreach (var coordinatorChoice in m_CoordinatorChoices)
            {
                menu.AppendAction(coordinatorChoice.ToString(), OnSelectCoordinator, DropdownMenuAction.AlwaysEnabled, coordinatorChoice);
            }
        }

        private void OnSelectCoordinator(DropdownMenuAction action)
        {
            if (m_RestoreSelectionScheduledItem != null && m_RestoreSelectionScheduledItem.isActive)
                m_RestoreSelectionScheduledItem.Pause();

            SelectCoordinatorChoice(action.userData as ICoordinatorChoice);
        }

        private void RestoreCoordinatorSelection()
        {
            RefreshCoordinatorChoices();
            if (m_CoordinatorChoices.Count > 0)
            {
                if (!string.IsNullOrEmpty(m_LastVisualTreeName))
                {
                    // 尝试找回上次选中的 VisualTree
                    for (int i = 0; i < m_CoordinatorChoices.Count; i++)
                    {
                        var vt = m_CoordinatorChoices[i];
                        if (vt.ToString() == m_LastVisualTreeName)
                        {
                            SelectCoordinatorChoice(vt);
                            break;
                        }
                    }
                }

                if (m_SelectedCoordinator != null)
                    OnRestoreCoordinatorSelection();
                else
                    SelectCoordinatorChoice(null);

                m_RestoreSelectionScheduledItem.Pause();
            }
        }

        /// <summary>
        /// 选择指定的协调器选项，并处理调试器挂接、解除挂接与菜单文本更新。
        /// </summary>
        /// <param name="cc">要选中的选项，传 <c>null</c> 表示取消选中。</param>
        protected virtual void SelectCoordinatorChoice(ICoordinatorChoice cc)
        {
            // 将调试器与当前面板解除挂接
            if (CoordinatorDebug != null)
                CoordinatorDebug.DetachDebugger(this);
            string menuText;

            if (cc != null && ValidateDebuggerConnection(cc.Coordinator))
            {
                cc.Coordinator.AttachDebugger(this);
                m_SelectedCoordinator = cc;
                m_LastVisualTreeName = cc.ToString();

                OnSelectCoordinateDebug(CoordinatorDebug);
                menuText = cc.ToString();
            }
            else
            {
                // 未选中任何树
                m_SelectedCoordinator = null;
                m_LastVisualTreeName = null;

                OnSelectCoordinateDebug(null);
                menuText = "Select a coordinator";
            }

            m_CoordinatorSelect.text = menuText;
        }

        /// <summary>
        /// 在选项列表中查找并选中指定的事件协调器。
        /// </summary>
        /// <param name="coordinator">要调试的事件协调器。</param>
        protected void SelectCoordinatorToDebug(MonoEventCoordinator coordinator)
        {
            // 选中新树
            if (m_SelectedCoordinator?.Coordinator != coordinator)
            {
                SelectCoordinatorChoice(null);
                RefreshCoordinatorChoices();
                for (int i = 0; i < m_CoordinatorChoices.Count; i++)
                {
                    var pc = m_CoordinatorChoices[i];
                    if (pc.Coordinator == coordinator)
                    {
                        SelectCoordinatorChoice(pc);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 在事件派发前拦截事件；默认不拦截，由子类按需重写。
        /// </summary>
        /// <param name="ev">待派发的事件。</param>
        /// <returns>返回 <c>true</c> 表示拦截该事件（停止传播、阻止默认行为并终止派发），否则返回 <c>false</c>。</returns>
        public virtual bool InterceptEvent(EventBase ev)
        {
            return false;
        }

        /// <summary>
        /// 在事件派发完成后处理事件；默认为空，由子类按需重写。
        /// </summary>
        /// <param name="ev">已完成派发的事件。</param>
        public virtual void PostProcessEvent(EventBase ev)
        { }
    }
}