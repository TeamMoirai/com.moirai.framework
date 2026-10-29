using System;
using System.Collections.Generic;
using System.IO;
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
            var snapshot = new SettingsOptions
            {
                FilePath = options.FilePath,
                FormatVersion = options.FormatVersion
            };
            var writer = new FileSettingsWriter(files, snapshot);
            Dictionary<string, string> loaded;

            StorageEntry entry = await files.GetEntryAsync(snapshot.FilePath, cancellationToken);
            await UniTask.SwitchToMainThread();
            if (entry != null && entry.IsDirectory)
                throw new InvalidOperationException("Settings path is a directory: " + snapshot.FilePath);
            if (entry != null && entry.Length == 0)
                throw new InvalidDataException("Settings file is empty: " + snapshot.FilePath);
            if (entry != null)
            {
                byte[] bytes = await files.ReadAllBytesAsync(snapshot.FilePath, cancellationToken);
                await UniTask.SwitchToMainThread();
                loaded = SettingsFileFormat.Deserialize(bytes, snapshot.FormatVersion);
            }
            else
            {
                loaded = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            return new BufferedKeyValueStore(loaded, writer);
        }
    }
}
