using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Storage;

namespace Moirai.Atropos.Settings.Internal
{
    internal sealed class FileSettingsWriter : ISettingsWriter
    {
        private readonly IFileSystem _files;
        private readonly SettingsOptions _options;

        internal FileSettingsWriter(IFileSystem files, SettingsOptions options)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async UniTask<StorageCommitResult> CommitAsync(
            IReadOnlyDictionary<string, string> snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            byte[] bytes = SettingsFileFormat.Serialize(snapshot, _options.FormatVersion);
            string staged = _options.FilePath + ".tmp";
            await _files.WriteAllBytesAsync(staged, bytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await _files.CommitAtomicAsync(staged, _options.FilePath, cancellationToken);
        }
    }
}
