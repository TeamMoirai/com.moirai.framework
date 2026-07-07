using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    public enum EBindType
    {
        None,
        Widget,
        ListCom
    }

    [Serializable]
    public class UIBindData
    {
        public UIBindData(string name, List<GameObject> objs, Type componentType = null, EBindType bindType = EBindType.None)
        {
            Name = name;
            Objs = objs ?? new List<GameObject>();
            BindType = bindType;
            ComponentType = componentType;
            ResolvedComponentTypes = new List<Type>();

            if (componentType != null && Objs.Count > 0)
            {
                for (var i = 0; i < Objs.Count; i++)
                {
                    ResolvedComponentTypes.Add(componentType);
                }
            }
        }

        public UIBindData(string name, GameObject obj, Type componentType = null, EBindType bindType = EBindType.None)
            : this(name, new List<GameObject> { obj }, componentType, bindType)
        {
        }

        public string Name { get; }

        public List<GameObject> Objs { get; set; }

        public EBindType BindType { get; }

        public Type ComponentType { get; private set; }

        public List<Type> ResolvedComponentTypes { get; }

        public bool IsGameObject => ComponentType == typeof(GameObject);

        public string TypeName => ComponentType?.FullName ?? string.Empty;

        public Type GetFirstOrDefaultType() => ComponentType;

        public void SetComponentType(Type componentType)
        {
            ComponentType = componentType;
        }

        public void AddResolvedObject(GameObject obj, Type componentType)
        {
            if (obj == null)
            {
                return;
            }

            Objs.Add(obj);
            ResolvedComponentTypes.Add(componentType ?? ComponentType);
        }

        public Type GetResolvedComponentType(int index)
        {
            if (index >= 0 && index < ResolvedComponentTypes.Count && ResolvedComponentTypes[index] != null)
            {
                return ResolvedComponentTypes[index];
            }

            return ComponentType;
        }
    }

    public static class UIScriptGeneratorHelper
    {
        private const string GENERATE_TYPE_NAME_KEY = "Moirai.Atropos.UI.Generate.TypeName";
        private const string GENERATE_INSTANCE_ID_KEY = "Moirai.Atropos.UI.Generate.InstanceId";
        private const string GENERATE_ASSET_PATH_KEY = "Moirai.Atropos.UI.Generate.AssetPath";
        private const string GENERATE_HIERARCHY_PATH_KEY = "Moirai.Atropos.UI.Generate.HierarchyPath";
        private static IUIIdentifierFormatter s_IdentifierFormatter;
        private static IUIResourcePathResolver s_ResourcePathResolver;
        private static IUIScriptCodeEmitter s_ScriptCodeEmitter;
        private static IUIScriptFileWriter s_ScriptFileWriter;
        private static readonly List<UIBindData> s_UIBindData = new List<UIBindData>();
        private static readonly HashSet<string> s_ArrayComponents = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> s_ComponentTypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        private static IUIIdentifierFormatter IdentifierFormatter =>
            ResolveConfiguredService(
                ref s_IdentifierFormatter,
                UIGeneratorSettings.UIIdentifierFormatterTypeName,
                typeof(DefaultUIIdentifierFormatter),
                nameof(IUIIdentifierFormatter));

        private static IUIResourcePathResolver ResourcePathResolver =>
            ResolveConfiguredService(
                ref s_ResourcePathResolver,
                UIGeneratorSettings.UIResourcePathResolverTypeName,
                typeof(DefaultUIResourcePathResolver),
                nameof(IUIResourcePathResolver));

        private static IUIScriptCodeEmitter ScriptCodeEmitter =>
            ResolveConfiguredService(
                ref s_ScriptCodeEmitter,
                UIGeneratorSettings.UIScriptCodeEmitterTypeName,
                typeof(DefaultUIScriptCodeEmitter),
                nameof(IUIScriptCodeEmitter));

        private static IUIScriptFileWriter ScriptFileWriter =>
            ResolveConfiguredService(
                ref s_ScriptFileWriter,
                UIGeneratorSettings.UIScriptFileWriterTypeName,
                typeof(DefaultUIScriptFileWriter),
                nameof(IUIScriptFileWriter));

        private static T ResolveConfiguredService<T>(ref T cachedService, string configuredTypeName, Type defaultType, string serviceName)
            where T : class
        {
            var resolvedTypeName = string.IsNullOrWhiteSpace(configuredTypeName) ? defaultType.FullName : configuredTypeName;
            if (cachedService != null && cachedService.GetType().FullName == resolvedTypeName)
            {
                return cachedService;
            }

            var configuredType = AssemblyUtility.GetType(resolvedTypeName);
            if (configuredType == null || !typeof(T).IsAssignableFrom(configuredType))
            {
                if (!string.Equals(resolvedTypeName, defaultType.FullName, StringComparison.Ordinal))
                {
                    Debug.LogError(
                        $"UIScriptGeneratorHelper: Could not load {serviceName} type '{resolvedTypeName}'. Falling back to {defaultType.FullName}.");
                }

                configuredType = defaultType;
            }

            cachedService = Activator.CreateInstance(configuredType, true) as T;
            if (cachedService != null)
            {
                return cachedService;
            }

            if (configuredType != defaultType)
            {
                Debug.LogError(
                    $"UIScriptGeneratorHelper: Failed to instantiate {resolvedTypeName} as {serviceName}. Falling back to {defaultType.FullName}.");
                cachedService = Activator.CreateInstance(defaultType, true) as T;
            }

            if (cachedService == null)
            {
                Debug.LogError($"UIScriptGeneratorHelper: Failed to instantiate fallback {defaultType.FullName} as {serviceName}.");
            }

            return cachedService;
        }

        private static bool EnsureGenerationStrategyReady()
        {
            return IdentifierFormatter != null &&
                   ResourcePathResolver != null &&
                   ScriptCodeEmitter != null &&
                   ScriptFileWriter != null;
        }

        private static bool EnsureVariableGenerationStrategyReady()
        {
            return IdentifierFormatter != null &&
                   ScriptCodeEmitter != null;
        }

        private static Type ResolveUIElementComponentType(string uiName)
        {
            if (string.IsNullOrEmpty(uiName))
            {
                return null;
            }

            var componentTypeName = UIGeneratorSettings.UIElementRegexConfigs
                ?.Where(pair => !string.IsNullOrEmpty(pair?.UIElementRegex))
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
                Debug.LogError($"UIScriptGeneratorHelper: Could not resolve component type '{componentTypeName}' for '{uiName}'.");
                return null;
            }

            s_ComponentTypeCache[componentTypeName] = componentType;
            return componentType;
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

        private static void CollectBindData(Transform root)
        {
            if (root == null) return;

            foreach (Transform child in root.Cast<Transform>().Where(child => child != null))
            {
                if (ShouldSkipChild(child)) continue;
                var hasWidget = child.GetComponent<UIBindComponent>() != null;
                var isArrayComponent = IsArrayComponent(child.name);

                if (hasWidget)
                {
                    CollectWidget(child);
                }
                else if (isArrayComponent)
                {
                    ProcessArrayComponent(child, root);
                }
                else
                {
                    CollectComponent(child);
                    CollectBindData(child);
                }
            }
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

        private static void ProcessArrayComponent(Transform child, Transform root)
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

            CollectArrayComponent(arrayComponents, groupName);
        }

        private static void CollectComponent(Transform node)
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
                if (s_UIBindData.Exists(data => data.Name == keyName))
                {
                    Debug.LogError($"Duplicate key found: {keyName}");
                    continue;
                }

                s_UIBindData.Add(new UIBindData(keyName, node.gameObject, actualComponentType));
            }
        }

        private static void CollectWidget(Transform node)
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
            if (s_UIBindData.Exists(data => data.Name == keyName))
            {
                Debug.LogError($"Duplicate key found: {keyName}");
                return;
            }

            s_UIBindData.Add(new UIBindData(keyName, component.gameObject, component.GetType(), EBindType.Widget));
        }

        private static void CollectArrayComponent(List<Transform> arrayNode, string nodeName)
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
            s_UIBindData.AddRange(tempBindData);
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

        public static void GenerateUIBindScript(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));
            if (scriptGenerateData == null) throw new ArgumentNullException(nameof(scriptGenerateData));

            if (!PrefabChecker.IsPrefabAsset(targetObject))
            {
                Debug.LogWarning("Please save the UI as a prefab asset before generating bindings.");
                return;
            }

            if (!EnsureGenerationStrategyReady())
            {
                return;
            }

            InitializeGenerationContext(targetObject);
            CollectBindData(targetObject.transform);

            var generationContext = new UIGenerationContext(targetObject, scriptGenerateData, s_UIBindData)
            {
                AssetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject),
                ClassName = GetClassGenerateName(targetObject, scriptGenerateData)
            };

            if (!CheckCanGenerate(generationContext))
            {
                CleanupContext();
                return;
            }

            var validationResult = ValidateGeneration(generationContext);
            if (!validationResult.IsValid)
            {
                Debug.LogError(validationResult.Message);
                CleanupContext();
                return;
            }

            GenerateScript(generationContext);
        }

        public static void CopyVariableContentToClipboard(GameObject targetObject)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));

            if (!EnsureVariableGenerationStrategyReady())
            {
                return;
            }

            string variableContent;
            try
            {
                ResetCollectedBindData();
                CollectBindData(targetObject.transform);
                variableContent = ScriptCodeEmitter.GetVariableContent(s_UIBindData, GetPublicComponentName);
            }
            finally
            {
                ResetCollectedBindData();
            }

            EditorGUIUtility.systemCopyBuffer = variableContent ?? string.Empty;
            if (string.IsNullOrEmpty(variableContent))
            {
                Debug.LogWarning($"UI生成绑定未找到可复制的属性内容，已清空剪贴板: {targetObject.name}");
                return;
            }

            Debug.Log($"UI生成绑定属性已复制到剪贴板: {targetObject.name}");
        }

        private static void InitializeGenerationContext(GameObject targetObject)
        {
#if UNITY_6000_4_OR_NEWER
            EditorPrefs.SetInt(GENERATE_INSTANCE_ID_KEY, targetObject.GetEntityId());
#else
            EditorPrefs.SetInt(GENERATE_INSTANCE_ID_KEY, targetObject.GetInstanceID());
#endif
            var assetPath = UIGenerateQuick.GetPrefabAssetPath(targetObject);
            if (!string.IsNullOrEmpty(assetPath))
            {
                EditorPrefs.SetString(GENERATE_ASSET_PATH_KEY, assetPath);
            }

            EditorPrefs.SetString(GENERATE_HIERARCHY_PATH_KEY, GetHierarchyPath(targetObject.transform, GetGenerationRootTransform(targetObject)));

            ResetCollectedBindData();
        }

        private static UIGenerationValidationResult ValidateGeneration(UIGenerationContext context)
        {
            if (context.TargetObject == null)
            {
                return UIGenerationValidationResult.Fail("UI generation target is null.");
            }

            if (context.ScriptGenerateData == null)
            {
                return UIGenerationValidationResult.Fail("UI generation config is null.");
            }

            if (string.IsNullOrWhiteSpace(context.ClassName))
            {
                return UIGenerationValidationResult.Fail("Generated class name is empty.");
            }

            return UIGenerationValidationResult.Success();
        }

        private static void GenerateScript(UIGenerationContext context)
        {
            const string templateText = @"
//----------------------------------------------------------
// <auto-generated>
// -This code was generated.
// -Changes to this file may cause incorrect behavior.
// -will be lost if the code is regenerated
// <auto-generated/>
//----------------------------------------------------------

#ReferenceNameSpace#
using Moirai.Atropos;
using Moirai.Atropos.UI;

namespace #ClassNameSpace#
{
    [DisallowMultipleComponent]
	public partial class #ClassName#Binder : UIBindComponent
	{
		#region 变量 [VARIABLES]

#Variable#

		#endregion
	}
#Controller#
}
";
            var processedText = ProcessTemplateText(context, templateText);
            EditorPrefs.SetString(GENERATE_TYPE_NAME_KEY, context.FullTypeName);
            WriteScriptContent(context, processedText);
        }

        private static string ProcessTemplateText(UIGenerationContext context, string templateText)
        {
            return templateText
                .Replace("#ReferenceNameSpace#", GetReferenceNamespace(context))
                .Replace("#ClassNameSpace#", context.ScriptGenerateData.NameSpace)
                .Replace("#ClassName#", context.ClassName)
                .Replace("#Variable#", GetVariableContent(context))
                .Replace("#Controller#", ScriptCodeEmitter.GetControllerContent(context.ClassName, context.BindData, GetPublicComponentName));
        }

        private static string GetClassGenerateName(GameObject targetObject, UIScriptGenerateData scriptGenerateData)
        {
            return IdentifierFormatter.GetClassName(targetObject);
        }

        private static bool CheckCanGenerate(UIGenerationContext context)
        {
            return ResourcePathResolver.CanGenerate(context.TargetObject, context.ScriptGenerateData);
        }

        private static string GetPrivateComponentName(string regexName, string componentName, EBindType bindType)
        {
            return IdentifierFormatter.GetPrivateComponentName(regexName, componentName, bindType);
        }

        private static string GetPublicComponentName(string variableName)
        {
            return IdentifierFormatter.GetPublicComponentName(variableName);
        }

        private static string GetResourceSavePath(UIGenerationContext context)
        {
            return ResourcePathResolver.GetResourcePath(context.TargetObject, context.ScriptGenerateData);
        }

        private static string GetReferenceNamespace(UIGenerationContext context)
        {
            return ScriptCodeEmitter.GetReferenceNamespaces(s_UIBindData);
        }

        private static string GetVariableContent(UIGenerationContext context)
        {
            return ScriptCodeEmitter.GetVariableContent(s_UIBindData, GetPublicComponentName);
        }

        private static void WriteScriptContent(UIGenerationContext context, string scriptContent)
        {
            var windowContent = ScriptCodeEmitter.GetWindowContent(
                context.ClassName,
                context.ScriptGenerateData.NameSpace,
                context.BindData,
                GetPublicComponentName);
            ScriptFileWriter.Write(context.TargetObject, context.ClassName, scriptContent, context.ScriptGenerateData, windowContent);
        }

        [DidReloadScripts]
        public static void BindUIScript()
        {
            if (!EditorPrefs.HasKey(GENERATE_TYPE_NAME_KEY)) return;

            var scriptTypeName = EditorPrefs.GetString(GENERATE_TYPE_NAME_KEY);
            var targetObject = ResolveGenerationTarget();

            if (targetObject == null)
            {
                Debug.LogWarning("UI script generation attachment object missing.");
                CleanupContext();
                return;
            }

            if (!EnsureGenerationStrategyReady())
            {
                CleanupContext();
                return;
            }

            ResetCollectedBindData();

            var bindSucceeded = false;
            CollectBindData(targetObject.transform);
            try
            {
                bindSucceeded = BindScriptPropertyField(targetObject, scriptTypeName);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                CleanupContext();
            }

            if (!bindSucceeded)
            {
                return;
            }

            EditorUtility.SetDirty(targetObject);
            Debug.Log($"Generate {scriptTypeName} successfully attached to game object.");
        }

        private static GameObject ResolveGenerationTarget()
        {
            var instanceId = EditorPrefs.GetInt(GENERATE_INSTANCE_ID_KEY, -1);
            var instanceTarget =
#if UNITY_6000_3_OR_NEWER
                EditorUtility.EntityIdToObject(instanceId)
#else
                EditorUtility.InstanceIDToObject(instanceId)
#endif
                    as GameObject;

            if (instanceTarget != null)
            {
                return instanceTarget;
            }

            var assetPath = EditorPrefs.GetString(GENERATE_ASSET_PATH_KEY, string.Empty);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null &&
                    !string.IsNullOrEmpty(prefabStage.assetPath) &&
                    string.Equals(prefabStage.assetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    var stageTarget = ResolveTargetByHierarchyPath(prefabStage.prefabContentsRoot?.transform);
                    if (stageTarget != null)
                    {
                        return stageTarget.gameObject;
                    }

                    return prefabStage.prefabContentsRoot;
                }
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            var assetTarget = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (assetTarget == null)
            {
                return null;
            }

            var resolvedAssetTarget = ResolveTargetByHierarchyPath(assetTarget.transform);
            return resolvedAssetTarget != null ? resolvedAssetTarget.gameObject : assetTarget;
        }

        private static Transform GetGenerationRootTransform(GameObject targetObject)
        {
            if (targetObject == null)
            {
                return null;
            }

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.IsPartOfPrefabContents(targetObject))
            {
                return prefabStage.prefabContentsRoot?.transform;
            }

            var prefabInstanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetObject);
            return prefabInstanceRoot != null ? prefabInstanceRoot.transform : targetObject.transform.root;
        }

        private static string GetHierarchyPath(Transform targetTransform, Transform rootTransform)
        {
            if (targetTransform == null)
            {
                return string.Empty;
            }

            if (rootTransform == null)
            {
                rootTransform = targetTransform.root;
            }

            var pathParts = new List<int>();
            var current = targetTransform;
            while (current != null && current != rootTransform)
            {
                pathParts.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            if (current != rootTransform)
            {
                return string.Empty;
            }

            pathParts.Reverse();
            return string.Join("/", pathParts);
        }

        private static Transform ResolveTargetByHierarchyPath(Transform rootTransform)
        {
            if (rootTransform == null)
            {
                return null;
            }

            var hierarchyPath = EditorPrefs.GetString(GENERATE_HIERARCHY_PATH_KEY, string.Empty);
            if (string.IsNullOrEmpty(hierarchyPath))
            {
                return rootTransform;
            }

            var current = rootTransform;
            var pathParts = hierarchyPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pathPart in pathParts)
            {
                if (!int.TryParse(pathPart, out var childIndex))
                {
                    return null;
                }

                if (childIndex < 0 || childIndex >= current.childCount)
                {
                    return null;
                }

                current = current.GetChild(childIndex);
            }

            return current;
        }

        private static void CleanupContext()
        {
            EditorPrefs.DeleteKey(GENERATE_TYPE_NAME_KEY);
            EditorPrefs.DeleteKey(GENERATE_INSTANCE_ID_KEY);
            EditorPrefs.DeleteKey(GENERATE_ASSET_PATH_KEY);
            EditorPrefs.DeleteKey(GENERATE_HIERARCHY_PATH_KEY);
            ResetCollectedBindData();
        }

        private static void ResetCollectedBindData()
        {
            s_UIBindData.Clear();
            s_ArrayComponents.Clear();
            s_ComponentTypeCache.Clear();
        }

        private static bool BindScriptPropertyField(GameObject targetObject, string scriptTypeName)
        {
            if (targetObject == null) throw new ArgumentNullException(nameof(targetObject));
            if (string.IsNullOrEmpty(scriptTypeName)) throw new ArgumentNullException(nameof(scriptTypeName));

            scriptTypeName += "Binder";
            var scriptType = FindScriptType(scriptTypeName);
            if (scriptType == null)
            {
                Debug.LogError($"Could not find the class: {scriptTypeName}");
                return false;
            }

            var targetHolder = targetObject.GetOrAddComponent(scriptType);
            return BindFieldsToComponents(targetHolder, scriptType);
        }

        private static Type FindScriptType(string scriptTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                var assemblyName = assembly.GetName().Name;
                if (assemblyName.EndsWith(".Editor", StringComparison.Ordinal) ||
                    assemblyName.Equals("UnityEditor", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var type in GetLoadableTypes(assembly))
                {
                    if (type != null &&
                        type.IsClass &&
                        !type.IsAbstract &&
                        type.FullName.Equals(scriptTypeName, StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<Type> GetLoadableTypes(System.Reflection.Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        private static bool BindFieldsToComponents(Component targetHolder, Type scriptType)
        {
            var fields = scriptType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Where(field =>
                field.GetCustomAttributes(typeof(SerializeField), false).Length > 0
            );

            var isSuccessful = true;
            foreach (var field in fields.Where(field => !string.IsNullOrEmpty(field.Name)))
            {
                var bindData = s_UIBindData.Find(data => data.Name == field.Name);
                if (bindData == null)
                {
                    Debug.LogError($"Field {field.Name} did not find matching component binding.");
                    isSuccessful = false;
                    continue;
                }

                if (!SetFieldValue(field, bindData, targetHolder))
                {
                    isSuccessful = false;
                }
            }

            return isSuccessful;
        }

        private static bool SetFieldValue(FieldInfo field, UIBindData bindData, Component targetComponent)
        {
            if (field.FieldType.IsArray)
            {
                return SetArrayFieldValue(field, bindData, targetComponent);
            }

            return SetSingleFieldValue(field, bindData, targetComponent);
        }

        private static bool SetArrayFieldValue(FieldInfo field, UIBindData bindData, Component targetComponent)
        {
            var elementType = field.FieldType.GetElementType();
            if (elementType == null)
            {
                Debug.LogError($"Field {field.Name} has unknown element type.");
                return false;
            }

            var components = bindData.Objs;
            var array = Array.CreateInstance(elementType, components.Count);
            var isSuccessful = true;
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] == null) continue;

                var componentObject = ResolveBoundObject(components[i], bindData.GetResolvedComponentType(i));

                if (componentObject != null && elementType.IsInstanceOfType(componentObject))
                {
                    array.SetValue(componentObject, i);
                }
                else
                {
                    Debug.LogError($"Element {i} type mismatch for field {field.Name}");
                    isSuccessful = false;
                }
            }

            field.SetValue(targetComponent, array);
            return isSuccessful;
        }

        private static bool SetSingleFieldValue(FieldInfo field, UIBindData bindData, Component targetComponent)
        {
            if (bindData.Objs.Count == 0)
            {
                return false;
            }

            var firstComponent = ResolveBoundObject(bindData.Objs[0], bindData.GetResolvedComponentType(0));
            if (firstComponent == null)
            {
                return false;
            }

            if (!field.FieldType.IsInstanceOfType(firstComponent))
            {
                Debug.LogError($"Field {field.Name} type mismatch");
                return false;
            }

            field.SetValue(targetComponent, firstComponent);
            return true;
        }

        private static object ResolveBoundObject(GameObject source, Type componentType)
        {
            if (source == null || componentType == null)
            {
                return null;
            }

            return componentType == typeof(GameObject) ? source : ResolveAssignableComponent(source, componentType);
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

        public static class PrefabChecker
        {
            public static bool IsEditingPrefabAsset(GameObject go)
            {
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                return prefabStage?.IsPartOfPrefabContents(go) == true;
            }

            public static bool IsPrefabAsset(GameObject go)
            {
                if (go == null) return false;

                var assetType = PrefabUtility.GetPrefabAssetType(go);
                var isRegularPrefab = assetType == PrefabAssetType.Regular ||
                                      assetType == PrefabAssetType.Variant ||
                                      assetType == PrefabAssetType.Model;

                return isRegularPrefab || IsEditingPrefabAsset(go);
            }
        }
    }
}