using System;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 路径管理：存档目录/文件路径解析（实现迁至 <see cref="SavePathResolver"/> 中立基建）。
    /// </summary>
    /// <remarks>
    /// partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化； <br />
    /// 路径解析/校验/分层门的实现自本类迁出至 <c>Internal/Storage/SavePaths.cs</c>（契约与门面的中立基建）， <br />
    /// 本 partial 仅保留契约成员与既有内部调用面的转发壳。
    /// </remarks>
    public abstract partial class SaveServiceHandler
    {
        /// <summary>
        /// 获取文件夹的完整保存路径（以目录分隔符结尾）。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>保存路径。</returns>
        public string DetermineSavePath(string folderName = DEFAULT_FOLDER_NAME)
        {
            ValidateFolderName(folderName);
            return SavePathResolver.BuildFolderPath(folderName);
        }

        /// <summary>
        /// 校验并解析文件名/文件夹名为完整路径集合（转发 <see cref="SavePathResolver.ResolveSavePaths"/>）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>路径集合。</returns>
        internal static SavePaths ResolveSavePaths(string fileName, string folderName)
        {
            return SavePathResolver.ResolveSavePaths(fileName, folderName);
        }

        /// <summary>
        /// 拼装存档数据根目录完整路径（转发 <see cref="SavePathResolver.BuildDataRootDirectory"/>）。
        /// </summary>
        /// <returns>根目录路径。</returns>
        private static string BuildDataRootDirectory()
        {
            return SavePathResolver.BuildDataRootDirectory();
        }

        /// <summary>
        /// 拼装存档文件夹完整路径（转发 <see cref="SavePathResolver.BuildFolderPath"/>）。
        /// </summary>
        /// <param name="folderName">已校验的文件夹名称（可为空 = 数据根目录）。</param>
        /// <returns>文件夹路径。</returns>
        private static string BuildFolderPath(string folderName)
        {
            return SavePathResolver.BuildFolderPath(folderName);
        }

        /// <summary>
        /// 校验存档数据对象：非 null fail-fast。
        /// </summary>
        /// <param name="data">存档数据对象。</param>
        private static void ValidateBlockData<T>(T data)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }
        }

        /// <summary>
        /// 校验文件名：非空、不含路径分隔符/穿越段/非法字符/Windows 保留设备名/临时文件后缀（转发 resolver）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        private static void ValidateFileName(string fileName)
        {
            SavePathResolver.ValidateFileName(fileName);
        }

        /// <summary>
        /// 校验文件夹名称：可为空（数据根目录）；不含路径分隔符/穿越段/盘符/非法字符（转发 resolver）。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        private static void ValidateFolderName(string folderName)
        {
            SavePathResolver.ValidateFolderName(folderName);
        }

        /// <summary>
        /// 校验数据块键：非空白、长度受限、不含控制字符/路径分隔符、禁止保留前缀（转发 resolver）。
        /// </summary>
        /// <param name="key">数据块键。</param>
        private static void ValidateBlockKey(string key)
        {
            SavePathResolver.ValidateBlockKey(key);
        }
    }
}
