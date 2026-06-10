#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Text;

public static class FormattedRegistryEditor
{
    // ========== 配置区 ==========
    private static readonly List<ScopedRegistry> RegistriesToAdd = new List<ScopedRegistry>
    {
        // https://github.com/openupm/openupm
		new ScopedRegistry(
            name: "Open UPM",
            url: "https://package.openupm.com",
            scopes: new[] { "com.github-glitchenzo.nugetforunity" },
            overrideBuiltIns: false
        ),
     	// https://github.com/bdovaz/UnityNuGet
		new ScopedRegistry(
            name: "Unity NuGet",
            url: "https://unitynuget-registry.openupm.com",
            scopes: new[] { "org.nuget" },
            dependencies: new Dictionary<string, string> { { "org.nuget.scriban", "2.1.0" } }
        )
    };

    [MenuItem("Tools/Settings/Add Scoped Registries", priority = -999)]
    public static void AddScopedRegistries()
    {
        string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        string manifestJson = File.ReadAllText(manifestPath);

        StringBuilder modifiedJson = new StringBuilder(manifestJson);
        int addCount = 0;
        foreach (var registry in RegistriesToAdd)
        {
            if (AddRegistryWithFormat(ref modifiedJson, registry)) addCount++;
        }

        if (addCount > 0)
        {
            File.WriteAllText(manifestPath, modifiedJson.ToString());
            Debug.Log("注册表添加完成！");
        }
        else
        {
            Debug.Log("没有需要添加的注册表！");
        }
    }

    private static bool AddRegistryWithFormat(ref StringBuilder json, ScopedRegistry registry)
    {
        string registryEntry = BuildFormattedEntry(registry);
        if (json.ToString().Contains($"\"name\": \"{registry.Name}\"") ||
            json.ToString().Contains($"\"url\": \"{registry.Url}\"")) return false;
        
        Debug.Log($"添加：{registry.Name} 到 manifest.json");
        int registriesIndex = json.ToString().IndexOf("\"scopedRegistries\"");
        if (registriesIndex == -1)
        {
            InsertNewRegistryArray(ref json, registryEntry);
        }
        else
        {
            AppendToExistingArray(ref json, registriesIndex, registryEntry);
        }
        
        return true;
    }

    private static string BuildFormattedEntry(ScopedRegistry registry)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    {");
        sb.AppendLine($"      \"name\": \"{registry.Name}\",");
        sb.AppendLine($"      \"url\": \"{registry.Url}\",");

        // 处理 scopes 数组
        sb.AppendLine("      \"scopes\": [");
        for (int i = 0; i < registry.Scopes.Length; i++)
        {
            string end = (i < registry.Scopes.Length - 1) ? "," : "";
            sb.AppendLine($"        \"{registry.Scopes[i]}\"{end}");
        }
        sb.Append("      ]");

        // 可选字段处理
        if (registry.OverrideBuiltIns.HasValue)
        {
            sb.AppendLine(",");
            sb.Append($"      \"overrideBuiltIns\": {registry.OverrideBuiltIns.Value.ToString().ToLower()}");
        }

        if (registry.Dependencies != null && registry.Dependencies.Count > 0)
        {
            sb.AppendLine(",");
            sb.AppendLine("      \"dependencies\": {");
            bool first = true;
            foreach (var dep in registry.Dependencies)
            {
                if (!first) sb.AppendLine(",");
                sb.Append($"        \"{dep.Key}\": \"{dep.Value}\"");
                first = false;
            }
            sb.Append("\n      }");
        }

        sb.Append("\n    }");
        return sb.ToString();
    }

    private static void InsertNewRegistryArray(ref StringBuilder json, string registryEntry)
    {
        int insertPos = json.ToString().IndexOf('{') + 1;
        string newSection = $@"
  ""scopedRegistries"": [
{registryEntry}
  ],";
        json.Insert(insertPos, newSection);
    }

    private static void AppendToExistingArray(ref StringBuilder json, int registriesIndex, string registryEntry)
    {
        int arrayStart = json.ToString().IndexOf('[', registriesIndex);
        int arrayEnd = FindMatchingBracket(json, arrayStart);

        // 判断是否需要添加逗号
        string existingContent = json.ToString(arrayStart, arrayEnd - arrayStart);
        bool needsComma = existingContent.Trim().Length > 0 && !existingContent.TrimEnd().EndsWith("[");

        // 构建插入内容
        string insertion = (needsComma ? ",\n" : "") + registryEntry;

        // 定位插入位置
        int lastBrace = json.ToString().LastIndexOf('}', arrayEnd);
        if (lastBrace != -1 && lastBrace > arrayStart)
        {
            // 在最后一个元素后插入
            json.Insert(lastBrace + 1, insertion);
        }
        else
        {
            // 直接插入到数组末尾
            json.Insert(arrayEnd - 1, insertion);
        }
    }

    private static int FindMatchingBracket(StringBuilder sb, int startIndex)
    {
        int depth = 0;
        for (int i = startIndex; i < sb.Length; i++)
        {
            if (sb[i] == '[') depth++;
            else if (sb[i] == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    [System.Serializable]
    private class ScopedRegistry
    {
        public string Name;
        public string Url;
        public string[] Scopes;
        public Dictionary<string, string> Dependencies;
        public bool? OverrideBuiltIns;

        public ScopedRegistry(
            string name, 
            string url, 
            string[] scopes,
            Dictionary<string, string> dependencies = null,
            bool? overrideBuiltIns = null
        )
        {
            Name = name;
            Url = url;
            Scopes = scopes;
            Dependencies = dependencies;
            OverrideBuiltIns = overrideBuiltIns;
        }
    }
}
#endif