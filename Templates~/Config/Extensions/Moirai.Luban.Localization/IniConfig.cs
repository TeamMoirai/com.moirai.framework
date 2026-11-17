namespace Moirai.LubanLocalization;

/// <summary>
/// config.ini 的扁平解析器，口径与 gen.sh 一致：分节仅作分组注释，键名全局唯一；
/// ';' 与 '#' 起始行是注释；值不做修剪变形，路径一律相对 config.ini 所在目录。
/// </summary>
internal sealed class IniConfig
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

    internal string DirectoryPath { get; }

    internal IniConfig(string path)
    {
        path = Path.GetFullPath(path);
        DirectoryPath = Path.GetDirectoryName(path)!;
        int number = 0;
        foreach (string raw in File.ReadAllLines(path))
        {
            number++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) continue;
            int equals = line.IndexOf('=');
            if (equals < 1) throw new FormatException($"config.ini 第 {number} 行缺少键值对：{line}");
            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            if (key.Length == 0 || !key.All(c => char.IsLetterOrDigit(c) || c == '_'))
                throw new FormatException($"config.ini 第 {number} 行键名非法：{key}");
            if (!_keys.TryAdd(key, value))
                throw new FormatException($"config.ini 第 {number} 行键重复：{key}（旧值来自更早的行）");
        }
    }

    internal string Get(string key, bool required = false)
    {
        string value = _keys.TryGetValue(key, out string? found) ? found : string.Empty;
        if (required && value.Length == 0)
            throw new FormatException($"config.ini 缺少键 {key}。");
        return value;
    }

    internal string Resolve(string value) => Path.GetFullPath(Path.Combine(DirectoryPath, value));
}
