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
        private readonly string _path;
        private readonly int _formatVersion;

        internal FileSettingsWriter(IFileSystem files, SettingsOptions options)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _path = options.FilePath;
            _formatVersion = options.FormatVersion;
        }

        public async UniTask<StorageCommitResult> CommitAsync(
            IReadOnlyDictionary<string, string> snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            cancellationToken.ThrowIfCancellationRequested();
            string staged = _path + "." + Guid.NewGuid().ToString("N") + ".staged";
            byte[] bytes = SettingsFileFormat.Serialize(snapshot, _formatVersion);
            try
            {
                await _files.WriteAllBytesAsync(staged, bytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return await _files.CommitAtomicAsync(staged, _path, cancellationToken);
            }
            catch (StorageCommitIndeterminateException) { throw; }
            catch (Exception failure)
            {
                try { await _files.DeleteAsync(staged, CancellationToken.None); }
                catch (Exception cleanupFailure) { throw new AggregateException(failure, cleanupFailure); }
                throw;
            }
        }
    }
}
