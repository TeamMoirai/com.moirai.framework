using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    /// <summary>
    /// 绑定数据收集器，负责从UI层级结构中收集组件绑定信息
    /// </summary>
    public static class UIBindDataCollector
    {
        private static readonly HashSet<string> s_ArrayComponents = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> s_ComponentTypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>
        /// 清空收集的绑定数据
        /// </summary>
        public static void Reset()
        {
            s_ArrayComponents.Clear();
            // 注意：s_ComponentTypeCache 保留，跨次生成复用缓存
        }

        /// <summary>
        /// 收集指定根节点下的所有绑定数据
        /// </summary>
        /// <param name="root">根变换节点</param>
        /// <param name="bindData">收集到的绑定数据列表</param>
        public static void Collect(Transform root, List<UIBindData> bindData)
        {
            if (root == null) return;

            foreach (Transform child in root.Cast<Transform>().Where(child => child != null))
            {
                if (ShouldSkipChild(child)) continue;
                var hasWidget = child.GetComponent<UIBindComponent>() != null;
                var isArrayComponent = IsArrayComponent(child.name);

                if (hasWidget)
                {
                    CollectWidget(child, bindData);
                }
                else if (isArrayComponent)
                {
                    ProcessArrayComponent(child, root, bindData);
                }
                else
                {
                    CollectComponent(child, bindData);
                    Collect(child, bindData);
                }
            }
        }

        /// <summary>
        /// 解析UI元素名称对应的组件类型
        /// </summary>
        public static Type ResolveUIElementComponentType(string uiName)
        {
            if (string.IsNullOrEmpty(uiName))
            {
                return null;
            }

            // 使用字典查找替代列表遍历
            var componentTypeName = UIGeneratorSettings.UIElementRegexConfigs
                ?.Where(pair => !string.IsNullOrEmpty(pair?.UIElementRegex))
                .OrderByDescending(pair => pair.UIElementRegex.Length) // 长前缀优先匹配
                .FirstOrDefault(pair => uiName.StartsWith(pair.UIElementRegex, StringComparison.Ordinal))
                ?.ComponentType;

            if (string.IsNullOrWhiteSpace(componentTypeName))
            {
                return null;
            }

            if (s_ComponentTypeCache.TryGetValue(componentTypeName, out var componentType))
            {
                return componentType;
            }

            componentType = ResolveConfiguredComponentType(componentTypeName);

            if (componentType == null)
            {
                Debug.LogError($"UIBindDataCollector: Could not resolve component type '{componentTypeName}' for '{uiName}'.");
                return null;
            }

            s_ComponentTypeCache[componentTypeName] = componentType;
            return componentType;
        }

        private static bool ShouldSkipChild(Transform child)
        {
            var keywords = UIGeneratorSettings.ExcludeKeywords;
            return keywords?.Any(k =>
                !string.IsNullOrEmpty(k) &&
                child.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) == true;
        }

        private static bool IsArrayComponent(string componentName)
        {
            var splitName = UIGeneratorSettings.ArrayComSplitName;
            return !string.IsNullOrEmpty(splitName) &&
                   componentName.StartsWith(splitName, StringComparison.Ordinal);
        }

        private static void ProcessArrayComponent(Transform child, Transform root, List<UIBindData> bindData)
        {
            var splitCode = UIGeneratorSettings.ArrayComSplitName;
            if (!TryGetArrayComponentGroupName(child.name, splitCode, out var groupName))
            {
                return;
            }

#if UNITY_6000_4_OR_NEWER
            string keyId = root.GetEntityId().ToString();
#else
            string keyId = root.GetInstanceID().ToString();
#endif

            var groupKey = $"{keyId}::{groupName}";
            if (!s_ArrayComponents.Add(groupKey))
            {
                return;
            }

            var arrayComponents = root.Cast<Transform>()
                .Where(sibling => IsArrayGroupMember(sibling.name, splitCode, groupName))
                .ToList();

            CollectArrayComponent(arrayComponents, groupName, bindData);
        }

        private static void CollectComponent(Transform node, List<UIBindData> bindData)
        {
            if (node == null) return;

            var componentArray = SplitComponentName(node.name);
            if (componentArray == null || componentArray.Length == 0) return;

            foreach (var com in componentArray.Where(com => !string.IsNullOrEmpty(com)))
            {
                var componentType = ResolveUIElementComponentType(com);
                if (componentType == null)
                {
                    continue;
                }

                if (!TryResolveActualComponentType(node.gameObject, componentType, out var actualComponentType))
                {
                    Debug.LogError($"{node.name} does not have component of type {componentType.FullName}");
                    continue;
                }

                var keyName = GetPrivateComponentName(com, node.name, EBindType.None);
                if (bindData.Exists(data => data.Name == keyName))
                {
                    Debug.LogError($"Duplicate key found: {keyName}");
                    continue;
                }

                bindData.Add(new UIBindData(keyName, node.gameObject, actualComponentType));
            }
        }

        private static void CollectWidget(Transform node, List<UIBindData> bindData)
        {
            if (node == null) return;

            if (node.name.Contains(UIGeneratorSettings.ComCheckEndName, StringComparison.Ordinal) &&
                node.name.Contains(UIGeneratorSettings.ComCheckSplitName, StringComparison.Ordinal))
            {
                Debug.LogWarning($"{node.name} child component cannot contain rule definition symbols!");
                return;
            }

            var component = node.GetComponent<UIBindComponent>();
            if (component == null)
            {
                Debug.LogError($"{node.name} expected to be a widget but does not have UIComponent.");
                return;
            }

            var keyName = GetPrivateComponentName(string.Empty, node.name, EBindType.Widget);
            if (bindData.Exists(data => data.Name == keyName))
            {
                Debug.LogError($"Duplicate key found: {keyName}");
                return;
            }

            bindData.Add(new UIBindData(keyName, component.gameObject, component.GetType(), EBindType.Widget));
        }

        private static void CollectArrayComponent(List<Transform> arrayNode, string nodeName, List<UIBindData> bindData)
        {
            if (arrayNode == null || !arrayNode.Any()) return;

            var componentArray = SplitComponentName(nodeName);
            if (componentArray == null || componentArray.Length == 0)
            {
                Debug.LogWarning($"CollectArrayComponent: {nodeName} has no component definitions.");
                return;
            }

            var orderedNodes = OrderArrayNodes(arrayNode);
            var tempBindData = CreateTempBindData(componentArray, nodeName);

            PopulateArrayComponents(componentArray, orderedNodes, tempBindData);
            bindData.AddRange(tempBindData);
        }

        private static List<Transform> OrderArrayNodes(List<Transform> arrayNode)
        {
            var splitCode = UIGeneratorSettings.ArrayComSplitName;
            return arrayNode
                .Select(node => new { Node = node, Index = ExtractArrayIndex(node.name, splitCode) })
                .OrderBy(x => x.Index ?? int.MaxValue)
                .Select(x => x.Node)
                .ToList();
        }

        private static List<UIBindData> CreateTempBindData(string[] componentArray, string nodeName)
        {
            return componentArray.Select(com =>
            {
                var keyName = GetPrivateComponentName(com, nodeName, EBindType.ListCom);
                return new UIBindData(keyName, new List<GameObject>(), null, EBindType.ListCom);
            }).ToList();
        }

        private static void PopulateArrayComponents(string[] componentArray, List<Transform> orderedNodes, List<UIBindData> tempBindData)
        {
            for (var index = 0; index < componentArray.Length; index++)
            {
                var com = componentArray[index];
                if (string.IsNullOrEmpty(com)) continue;

                var componentType = ResolveUIElementComponentType(com);
                if (componentType == null) continue;

                foreach (var node in orderedNodes)
                {
                    if (TryResolveActualComponentType(node.gameObject, componentType, out var actualComponentType))
                    {
                        tempBindData[index].AddResolvedObject(node.gameObject, actualComponentType);
                    }
                    else
                    {
                        Debug.LogError($"{node.name} does not have component of type {componentType.FullName}");
                    }
                }

                tempBindData[index].SetComponentType(
                    ResolveBindComponentType(componentType, tempBindData[index].ResolvedComponentTypes));
            }
        }

        private static bool TryGetArrayComponentGroupName(string nodeName, string splitCode, out string groupName)
        {
            groupName = null;
            if (string.IsNullOrEmpty(nodeName) || string.IsNullOrEmpty(splitCode))
            {
                return false;
            }

            var firstIndex = nodeName.IndexOf(splitCode, StringComparison.Ordinal);
            var lastIndex = nodeName.LastIndexOf(splitCode, StringComparison.Ordinal);
            if (firstIndex < 0 || lastIndex <= firstIndex)
            {
                return false;
            }

            groupName = nodeName.Substring(firstIndex + splitCode.Length, lastIndex - (firstIndex + splitCode.Length));
            return !string.IsNullOrEmpty(groupName);
        }

        private static bool IsArrayGroupMember(string nodeName, string splitCode, string groupName)
        {
            return TryGetArrayComponentGroupName(nodeName, splitCode, out var candidateGroupName) &&
                   candidateGroupName.Equals(groupName, StringComparison.Ordinal) &&
                   ExtractArrayIndex(nodeName, splitCode).HasValue;
        }

        private static int? ExtractArrayIndex(string nodeName, string splitCode)
        {
            if (string.IsNullOrEmpty(nodeName) || string.IsNullOrEmpty(splitCode)) return null;

            var lastIndex = nodeName.LastIndexOf(splitCode, StringComparison.Ordinal);
            if (lastIndex < 0) return null;

            var suffix = nodeName.Substring(lastIndex + splitCode.Length);
            return int.TryParse(suffix, out var idx) ? idx : (int?)null;
        }

        private static string[] SplitComponentName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (string.IsNullOrEmpty(UIGeneratorSettings.ComCheckEndName) || !name.Contains(UIGeneratorSettings.ComCheckEndName))
                return null;

            var endIndex = name.IndexOf(UIGeneratorSettings.ComCheckEndName, StringComparison.Ordinal);
            if (endIndex <= 0) return null;

            var comStr = name.Substring(0, endIndex);
            var split = UIGeneratorSettings.ComCheckSplitName ?? "#";

            return comStr.Split(new[] { split }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetPrivateComponentName(string regexName, string componentName, EBindType bindType)
        {
            var formatter = UIScriptGeneratorHelper.IdentifierFormatter;
            return formatter?.GetPrivateComponentName(regexName, componentName, bindType) ?? componentName;
        }

        private static bool TryResolveActualComponentType(GameObject source, Type componentType, out Type actualComponentType)
        {
            actualComponentType = null;

            if (source == null || componentType == null)
            {
                return false;
            }

            if (componentType == typeof(GameObject))
            {
                actualComponentType = typeof(GameObject);
                return true;
            }

            var component = ResolveAssignableComponent(source, componentType);
            if (component == null)
            {
                return false;
            }

            actualComponentType = component.GetType();
            return actualComponentType != null;
        }

        private static Type ResolveBindComponentType(Type configuredType, IEnumerable<Type> actualComponentTypes)
        {
            if (configuredType == typeof(GameObject))
            {
                return typeof(GameObject);
            }

            var resolvedTypes = actualComponentTypes?
                .Where(type => type != null)
                .Distinct()
                .ToList();

            if (resolvedTypes == null || resolvedTypes.Count == 0)
            {
                return configuredType;
            }

            if (resolvedTypes.Count == 1)
            {
                return resolvedTypes[0];
            }

            return FindCommonComponentBaseType(resolvedTypes) ?? configuredType;
        }

        private static Type FindCommonComponentBaseType(IReadOnlyList<Type> componentTypes)
        {
            if (componentTypes == null || componentTypes.Count == 0)
            {
                return null;
            }

            var candidateType = componentTypes[0];
            while (candidateType != null && candidateType != typeof(object))
            {
                if (typeof(Component).IsAssignableFrom(candidateType) &&
                    componentTypes.All(type => candidateType.IsAssignableFrom(type)))
                {
                    return candidateType;
                }

                candidateType = candidateType.BaseType;
            }

            return null;
        }

        private static Type ResolveConfiguredComponentType(string componentTypeName)
        {
            if (string.IsNullOrWhiteSpace(componentTypeName))
            {
                return null;
            }

            if (componentTypeName == nameof(GameObject))
            {
                return typeof(GameObject);
            }

            Type componentType = AssemblyUtility.GetType(componentTypeName);
            if (componentType != null && typeof(Component).IsAssignableFrom(componentType))
            {
                return componentType;
            }

            return AssemblyUtility.GetTypes()
                .Where(type => type != null && !type.IsAbstract && !type.IsInterface)
                .Where(type => typeof(Component).IsAssignableFrom(type))
                .Where(type => string.Equals(type.FullName, componentTypeName, StringComparison.Ordinal)
                               || string.Equals(type.Name, componentTypeName, StringComparison.Ordinal))
                .OrderBy(type => string.Equals(type.FullName, componentTypeName, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(type => string.Equals(type.Namespace, "UnityEngine.UI", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(type => type.FullName, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static Component ResolveAssignableComponent(GameObject source, Type componentType)
        {
            if (source == null || componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return null;
            }

            Component component = source.GetComponent(componentType);
            if (component != null)
            {
                return component;
            }

            Component[] components = source.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component candidate = components[i];
                if (candidate != null && componentType.IsInstanceOfType(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}