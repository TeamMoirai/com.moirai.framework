using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Storage;

namespace Moirai.Atropos.Settings.Internal
{
    internal static class SettingsFileFormat
    {
        internal const string Header = "MOIRAI-SETTINGS";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

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
            string text;
            try { text = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("Settings file is not valid UTF-8.", error);
            }
            string[] lines = text.Split('\n');
            string[] header = lines[0].Split(' ');
            if (header.Length != 3 || header[0] != Header)
                throw new InvalidDataException("Settings header is malformed.");
            if (!int.TryParse(header[1], out int format) || format < 0)
                throw new InvalidDataException("Settings format version is malformed.");
            if (format > supportedFormat)
                throw new InvalidOperationException(
                    "Settings file was written by a newer format: " + format);
            if (format < supportedFormat)
                throw new InvalidOperationException(
                    "Settings file uses an older format that requires migration: " + format);
            if (!int.TryParse(header[2], out int count) || count < 0)
                throw new InvalidDataException("Settings entry count is malformed.");

            int entries = lines.Length - 1;
            if (entries > 0 && lines[lines.Length - 1].Length == 0) entries--;
            if (entries < count)
                throw new InvalidDataException("Settings entries are truncated.");
            if (entries > count)
                throw new InvalidDataException("Settings file has more entries than declared.");

            var values = new Dictionary<string, string>(count, StringComparer.Ordinal);
            for (int i = 1; i <= count; i++)
            {
                string[] parts = lines[i].Split(' ');
                if (parts.Length != 2)
                    throw new InvalidDataException("Settings entry is malformed at line " + (i + 1) + ".");
                string key;
                string value;
                try
                {
                    key = StrictUtf8.GetString(Convert.FromBase64String(parts[0]));
                    value = StrictUtf8.GetString(Convert.FromBase64String(parts[1]));
                }
                catch (FormatException error)
                {
                    throw new InvalidDataException("Settings entry is not valid base64.", error);
                }
                catch (DecoderFallbackException error)
                {
                    throw new InvalidDataException("Settings entry is not valid UTF-8.", error);
                }
                if (key.Length == 0)
                    throw new InvalidDataException("Settings entry has an empty key at line " + (i + 1) + ".");
                if (values.ContainsKey(key))
                    throw new InvalidDataException("Settings entry repeats key '" + key + "'.");
                ValidateValue(key, value, i + 1);
                values[key] = value;
            }
            return values;
        }

        private static void ValidateValue(string key, string value, int line)
        {
            bool valid;
            switch (value.Length > 1 ? value[0] : '\0')
            {
                case 'b':
                    valid = bool.TryParse(value.Substring(2), out _);
                    break;
                case 'i':
                    valid = int.TryParse(value.Substring(2), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out _);
                    break;
                case 'f':
                    valid = float.TryParse(value.Substring(2), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out _);
                    break;
                case 's':
                    valid = value[1] == ':';
                    break;
                default:
                    throw new InvalidDataException(
                        "Settings entry '" + key + "' has an unknown value type at line " + line + ".");
            }
            if (!valid || value[1] != ':')
                throw new InvalidDataException(
                    "Settings entry '" + key + "' has a mistyped value at line " + line + ".");
        }
    }
}
