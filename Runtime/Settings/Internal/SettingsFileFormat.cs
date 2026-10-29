using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Storage;

namespace Moirai.Atropos.Settings.Internal
{
    internal static class SettingsFileFormat
    {
        internal const string Header = "MIRAI-SETTINGS";

        internal static byte[] Serialize(IReadOnlyDictionary<string, string> snapshot, int format)
        {
            var text = new StringBuilder();
            text.Append(Header).Append(' ').Append(format).Append(' ').Append(snapshot.Count).Append('\n');
            foreach (KeyValuePair<string, string> pair in snapshot)
            {
                text.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key)))
                    .Append(' ')
                    .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value ?? string.Empty)))
                    .Append('\n');
            }
            return Encoding.UTF8.GetBytes(text.ToString());
        }

        internal static Dictionary<string, string> Deserialize(byte[] bytes, int supportedFormat)
        {
            string[] lines = Encoding.UTF8.GetString(bytes).Split('\n');
            string[] header = lines[0].Split(' ');
            if (header.Length != 3 || header[0] != Header)
                throw new InvalidDataException("Settings header is malformed.");
            if (!int.TryParse(header[1], out int format) || format < 0)
                throw new InvalidDataException("Settings format version is malformed.");
            if (format > supportedFormat)
                throw new InvalidOperationException(
                    "Settings file was written by a newer format: " + format);
            if (!int.TryParse(header[2], out int count) || count < 0)
                throw new InvalidDataException("Settings entry count is malformed.");
            var values = new Dictionary<string, string>(count, StringComparer.Ordinal);
            if (lines.Length - 1 < count)
                throw new InvalidDataException("Settings entries are truncated.");
            for (int i = 1; i <= count; i++)
            {
                string[] parts = lines[i].Split(' ');
                if (parts.Length != 2)
                    throw new InvalidDataException("Settings entry is malformed at line " + (i + 1) + ".");
                string key;
                string value;
                try
                {
                    key = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                    value = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                }
                catch (FormatException error)
                {
                    throw new InvalidDataException("Settings entry is not valid base64.", error);
                }
                values[key] = value;
            }
            return values;
        }
    }
}
