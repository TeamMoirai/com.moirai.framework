namespace Moirai.Atropos.Storage
{
    public sealed class StorageEntry
    {
        public StorageEntry(string path, bool isDirectory, long length)
        {
            Path = path;
            IsDirectory = isDirectory;
            Length = length;
        }

        public string Path { get; }
        public bool IsDirectory { get; }
        public long Length { get; }
    }
}
