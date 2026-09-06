using System.Collections.Generic;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器窗口注册表（路径树导航模型，纯数据结构无生命周期副作用）。
    /// <para>扁平字典提供 O(1) 路径检索；树节点仅供侧边栏导航渲染。窗口生命周期（<see cref="IDebuggerWindow.Initialize"/> / <see cref="IDebuggerWindow.Shutdown"/>）由服务处理器在注册表之外管理。</para>
    /// </summary>
    public sealed class DebuggerWindowRegistry
    {
        #region 字段 [FIELDS]

        private readonly Dictionary<string, IDebuggerWindow> _windowsByPath = new Dictionary<string, IDebuggerWindow>();
        private readonly Dictionary<string, DebuggerWindowNode> _nodesByPath = new Dictionary<string, DebuggerWindowNode>();
        private readonly List<DebuggerWindowNode> _childBuffer = new List<DebuggerWindowNode>();
        private readonly DebuggerWindowNode _root;
        private DebuggerWindowNode _selectedNode;
        private int _version;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化调试器窗口注册表的新实例。
        /// </summary>
        public DebuggerWindowRegistry()
        {
            _root = new DebuggerWindowNode
            {
                Name = string.Empty,
                Path = string.Empty,
                Window = null
            };
            _root.Expanded = true;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取根节点（虚拟节点，不对应任何路径）。
        /// </summary>
        public DebuggerWindowNode Root => _root;

        /// <summary>
        /// 获取当前选中的窗口节点（未选中为 null；目录节点不可选中）。
        /// </summary>
        public DebuggerWindowNode SelectedNode => _selectedNode;

        /// <summary>
        /// 获取当前选中的窗口。
        /// </summary>
        public IDebuggerWindow SelectedWindow => _selectedNode?.Window;

        /// <summary>
        /// 获取已注册窗口数量。
        /// </summary>
        public int WindowCount => _windowsByPath.Count;

        /// <summary>
        /// 获取结构版本号（注册/注销/选中时递增——宿主据此重建侧边栏）。
        /// </summary>
        public int Version => _version;

        #endregion

        #region 注册 [REGISTRATION]

        /// <summary>
        /// 注册调试器窗口（按路径自动创建中间目录节点）。
        /// </summary>
        /// <param name="path">调试器窗口路径（以 '/' 分隔，如 "Profiler/Memory/Texture"）。</param>
        /// <param name="window">要注册的调试器窗口。</param>
        /// <exception cref="GameException">路径无效或已注册同名窗口。</exception>
        public void Register(string path, IDebuggerWindow window)
        {
            if (string.IsNullOrEmpty(path) || !IsValidPath(path))
            {
                throw new GameException("Path is invalid.");
            }

            if (window == null)
            {
                throw new GameException("Debugger window is invalid.");
            }

            if (_windowsByPath.ContainsKey(path) || _nodesByPath.ContainsKey(path))
            {
                throw new GameException(StringUtility.Format("Debugger window '{0}' has been registered.", path));
            }

            DebuggerWindowNode parentNode = EnsureDirectoryNode(path);
            int lastSeparator = path.LastIndexOf('/');
            string leafName = lastSeparator < 0 ? path : path.Substring(lastSeparator + 1);

            DebuggerWindowNode node = new DebuggerWindowNode
            {
                Name = leafName,
                Path = path,
                Window = window
            };
            parentNode.AddChild(node);
            _nodesByPath[path] = node;
            _windowsByPath[path] = window;
            _version++;
        }

        /// <summary>
        /// 解除注册调试器窗口（自动剪除因此变空的目录节点）。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否解除注册成功（路径不存在时为 false）。</returns>
        public bool Unregister(string path)
        {
            if (string.IsNullOrEmpty(path) || !_windowsByPath.TryGetValue(path, out IDebuggerWindow window))
            {
                return false;
            }

            if (!_nodesByPath.TryGetValue(path, out DebuggerWindowNode node))
            {
                return false;
            }

            DebuggerWindowNode parent = node.Parent;
            parent?.RemoveChild(node);
            _nodesByPath.Remove(path);
            _windowsByPath.Remove(path);
            PruneEmptyAncestors(parent);

            if (ReferenceEquals(_selectedNode, node))
            {
                _selectedNode = null;
            }

            _version++;
            return true;
        }

        #endregion

        #region 检索与选中 [LOOKUP AND SELECTION]

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>要获取的调试器窗口（路径不存在时为 null）。</returns>
        public IDebuggerWindow GetWindow(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return _windowsByPath.TryGetValue(path, out IDebuggerWindow window) ? window : null;
        }

        /// <summary>
        /// 按路径选中调试器窗口（自动展开其祖先目录）。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否成功选中（路径不存在或指向目录节点时为 false）。</returns>
        public bool SelectWindow(string path)
        {
            if (string.IsNullOrEmpty(path) || !_nodesByPath.TryGetValue(path, out DebuggerWindowNode node) || node.Window == null)
            {
                return false;
            }

            return SelectNode(node);
        }

        /// <summary>
        /// 按节点选中调试器窗口（侧边栏点击路径）。
        /// </summary>
        /// <param name="node">目标窗口节点。</param>
        /// <returns>是否成功选中（目录节点为 false）。</returns>
        public bool SelectNode(DebuggerWindowNode node)
        {
            if (node == null || node.Window == null)
            {
                return false;
            }

            _selectedNode = node;
            for (DebuggerWindowNode ancestor = node.Parent; ancestor != null && ancestor != _root; ancestor = ancestor.Parent)
            {
                ancestor.Expanded = true;
            }

            _version++;
            return true;
        }

        /// <summary>
        /// 收集全部已注册窗口（服务关闭时批量 Shutdown 用）。
        /// </summary>
        /// <param name="results">输出收集列表（调用前不清空由调用方决定）。</param>
        public void CollectWindows(List<IDebuggerWindow> results)
        {
            foreach (KeyValuePair<string, IDebuggerWindow> pair in _windowsByPath)
            {
                results.Add(pair.Value);
            }
        }

        #endregion

        #region 私有 [PRIVATE]

        private static bool IsValidPath(string path)
        {
            int segmentStart = 0;
            for (int i = 0; i <= path.Length; i++)
            {
                if (i == path.Length || path[i] == '/')
                {
                    if (i == segmentStart)
                    {
                        return false;
                    }

                    segmentStart = i + 1;
                }
            }

            return true;
        }

        private DebuggerWindowNode EnsureDirectoryNode(string path)
        {
            int separator = path.IndexOf('/');
            if (separator < 0)
            {
                return _root;
            }

            string directoryPath = path.Substring(0, separator);
            if (!_nodesByPath.TryGetValue(directoryPath, out DebuggerWindowNode node))
            {
                node = new DebuggerWindowNode
                {
                    Name = directoryPath,
                    Path = directoryPath,
                    Window = null
                };
                _root.AddChild(node);
                _nodesByPath[directoryPath] = node;
            }
            else if (node.Window != null)
            {
                throw new GameException(StringUtility.Format("Debugger window '{0}' has been registered, can not create debugger window group.", directoryPath));
            }

            return EnsureDirectoryNodeRecursive(node, path, separator + 1);
        }

        private DebuggerWindowNode EnsureDirectoryNodeRecursive(DebuggerWindowNode parent, string fullPath, int segmentStart)
        {
            int separator = fullPath.IndexOf('/', segmentStart);
            int nextSegmentStart = separator < 0 ? fullPath.Length : separator + 1;
            if (nextSegmentStart >= fullPath.Length)
            {
                return parent;
            }

            string directoryPath = fullPath.Substring(0, separator < 0 ? fullPath.Length : separator);
            if (!_nodesByPath.TryGetValue(directoryPath, out DebuggerWindowNode node))
            {
                node = new DebuggerWindowNode
                {
                    Name = fullPath.Substring(segmentStart, directoryPath.Length - segmentStart),
                    Path = directoryPath,
                    Window = null
                };
                parent.AddChild(node);
                _nodesByPath[directoryPath] = node;
            }

            return EnsureDirectoryNodeRecursive(node, fullPath, nextSegmentStart);
        }

        private void PruneEmptyAncestors(DebuggerWindowNode node)
        {
            while (node != null && node != _root && node.Children.Count == 0 && node.Window == null)
            {
                DebuggerWindowNode parent = node.Parent;
                parent?.RemoveChild(node);
                _nodesByPath.Remove(node.Path);
                node = parent;
            }
        }

        #endregion
    }
}
