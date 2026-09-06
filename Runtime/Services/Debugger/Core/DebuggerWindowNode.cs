using System.Collections.Generic;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器窗口树节点。
    /// <para>侧边栏导航的数据模型：叶子节点持有 <see cref="Window"/>，目录节点仅作分组（<see cref="Window"/> 为 null）。</para>
    /// <para>树仅承载导航语义——不参与窗口生命周期（生命周期由注册表与宿主管理）。</para>
    /// </summary>
    public sealed class DebuggerWindowNode
    {
        #region 字段 [FIELDS]

        private readonly List<DebuggerWindowNode> _children = new List<DebuggerWindowNode>();

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取节点名（路径末段）。
        /// </summary>
        public string Name
        {
            get;
            internal set;
        }

        /// <summary>
        /// 获取从根到本节点的完整路径。
        /// </summary>
        public string Path
        {
            get;
            internal set;
        }

        /// <summary>
        /// 获取父节点（根节点为 null）。
        /// </summary>
        public DebuggerWindowNode Parent
        {
            get;
            internal set;
        }

        /// <summary>
        /// 获取绑定的调试器窗口（目录节点为 null）。
        /// </summary>
        public IDebuggerWindow Window
        {
            get;
            internal set;
        }

        /// <summary>
        /// 获取或设置目录节点展开状态（侧边栏折叠记忆）。
        /// </summary>
        public bool Expanded
        {
            get;
            set;
        }

        /// <summary>
        /// 获取子节点集合。
        /// </summary>
        public IReadOnlyList<DebuggerWindowNode> Children => _children;

        /// <summary>
        /// 获取是否为目录节点（含子节点）。
        /// </summary>
        public bool IsGroup => _children.Count > 0;

        #endregion

        #region 内部操作 [INTERNAL OPERATIONS]

        internal void AddChild(DebuggerWindowNode child)
        {
            child.Parent = this;
            _children.Add(child);
        }

        internal bool RemoveChild(DebuggerWindowNode child)
        {
            return _children.Remove(child);
        }

        #endregion
    }
}
