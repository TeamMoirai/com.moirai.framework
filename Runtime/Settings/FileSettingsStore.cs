using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Settings.Internal;
using Moirai.Atropos.Storage;

namespace Moirai.Atropos.Settings
{
    public static class FileSettingsStore
    {
        public static async UniTask<IKeyValueStore> LoadAsync(IFileSystem files,
            SettingsOptions options, CancellationToken cancellationToken = default)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (options == null) throw new ArgumentNullException(nameof(options));
            var writer = new FileSettingsWriter(files, options);
            Dictionary<string, string> loaded;

            StorageEntry entry = await files.GetEntryAsync(options.FilePath, cancellationToken);
            if (entry != null && !entry.IsDirectory && entry.Length > 0)
            {
                byte[] bytes = await files.ReadAllBytesAsync(options.FilePath, cancellationToken);
                loaded = SettingsFileFormat.Deserialize(bytes, options.FormatVersion);
            }
            else
            {
                loaded = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            return new BufferedKeyValueStore(loaded, writer);
        }
    }
}
