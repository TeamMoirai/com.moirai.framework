using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 路径管理：存档目录/文件路径解析。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="SaveServiceHandler"/>——公共 API 零变化。</remarks>
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
            return BuildFolderPath(folderName);
        }

        /// <summary>
        /// 分层门作用域：按「根目录 → 文件夹 → 文件」固定序取门，使整档删除、文件夹删除与根目录清空同块级读写互斥。
        /// </summary>
        /// <remarks>
        /// 取门顺序全局一致（防死锁）；根门为全局串行点，跨文件夹的存档 IO 互相排队。 <br />
        /// 各层级计数登记在 <see cref="SaveFileGate"/> 表上，<see cref="Leave"/> 幂等归还（只释放已持层级）。
        /// </remarks>
        private sealed class GateScope
        {
            /// <summary>层级键（取门顺序：根 → 文件夹 → 文件）。</summary>
            private readonly string[] _keys;

            /// <summary>层级信号量（与 <see cref="_keys"/> 对齐）。</summary>
            private readonly SemaphoreSlim[] _gates;

            /// <summary>已实际持门的前缀层级数（等门取消时只释放该前缀）。</summary>
            private int _acquiredCount;

            /// <summary>归还标记（幂等守卫）。</summary>
            private bool _left;

            private GateScope(string[] keys, SemaphoreSlim[] gates)
            {
                _keys = keys;
                _gates = gates;
            }

            /// <summary>
            /// 取文件级门作用域（根 + 文件夹 + 文件三级）。
            /// </summary>
            /// <param name="paths">已解析的路径集合。</param>
            /// <returns>门作用域。</returns>
            public static GateScope EnterFile(SavePaths paths)
            {
                return EnterAll(BuildDataRootDirectory(), paths.DirectoryPath, paths.SaveFilePath);
            }

            /// <summary>
            /// 取文件夹级门作用域（根 + 文件夹两级；文件夹批量删除用）。
            /// </summary>
            /// <param name="directoryPath">目标文件夹完整路径（与 <see cref="SavePaths.DirectoryPath"/> 同串）。</param>
            /// <returns>门作用域。</returns>
            public static GateScope EnterFolder(string directoryPath)
            {
                return EnterAll(BuildDataRootDirectory(), directoryPath);
            }

            /// <summary>
            /// 取根级门作用域（仅根目录；清空全部存档用）。
            /// </summary>
            /// <param name="rootDirectory">存档数据根目录完整路径。</param>
            /// <returns>门作用域。</returns>
            public static GateScope EnterRoot(string rootDirectory)
            {
                return EnterAll(rootDirectory);
            }

            private static GateScope EnterAll(params string[] keys)
            {
                var gates = new SemaphoreSlim[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    gates[i] = SaveFileGate.Enter(keys[i]);
                }

                return new GateScope(keys, gates);
            }

            /// <summary>
            /// 同步等待全部层级持门。
            /// </summary>
            public void Wait()
            {
                for (int i = 0; i < _gates.Length; i++)
                {
                    _gates[i].Wait();
                    _acquiredCount = i + 1;
                }
            }

            /// <summary>
            /// 异步等待全部层级持门（等门期取消时抛出，已持前缀层级由 <see cref="Leave"/> 正确归还）。
            /// </summary>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>持门完成的异步任务。</returns>
            public async UniTask WaitAsync(CancellationToken cancellationToken)
            {
                for (int i = 0; i < _gates.Length; i++)
                {
                    // ConfigureAwait(false)：取门续体留在工作线程。否则持门者的续体要等主线程 PlayerLoop，
                    // 而主线程可能正阻塞在同级门的同步 Wait() 上——续体永不执行，Leave 永不发生，门永久饥饿。
                    await _gates[i].WaitAsync(cancellationToken).ConfigureAwait(false);
                    _acquiredCount = i + 1;
                }
            }

            /// <summary>
            /// 归还全部层级（逆序释放已持前缀，未持层级仅归还占用计数；幂等——重复调用为空操作）。
            /// </summary>
            public void Leave()
            {
                if (_left)
                {
                    return;
                }

                _left = true;
                for (int i = _gates.Length - 1; i >= 0; i--)
                {
                    SaveFileGate.Leave(_keys[i], _gates[i], i < _acquiredCount);
                }

                _acquiredCount = 0;
            }
        }

        /// <summary>
        /// 单次存档操作的解析路径集合（调用线程解析完成后传入工作线程核心）。
        /// </summary>
        internal readonly struct SavePaths
        {
            /// <summary>目标文件夹完整路径。</summary>
            public readonly string DirectoryPath;

            /// <summary>目标存档文件完整路径。</summary>
            public readonly string SaveFilePath;

            /// <summary>API 层原始文件名（事件参数用；手工构造路径集合时为 <c>null</c>）。</summary>
            public readonly string FileName;

            /// <summary>API 层原始文件夹名称（事件参数用；手工构造路径集合时为 <c>null</c>）。</summary>
            public readonly string FolderName;

            /// <summary>
            /// 创建路径集合（无 API 层名称上下文）。
            /// </summary>
            /// <param name="directoryPath">目标文件夹完整路径。</param>
            /// <param name="saveFilePath">目标存档文件完整路径。</param>
            public SavePaths(string directoryPath, string saveFilePath)
                : this(directoryPath, saveFilePath, null, null)
            {
            }

            /// <summary>
            /// 创建路径集合。
            /// </summary>
            /// <param name="directoryPath">目标文件夹完整路径。</param>
            /// <param name="saveFilePath">目标存档文件完整路径。</param>
            /// <param name="fileName">API 层原始文件名。</param>
            /// <param name="folderName">API 层原始文件夹名称。</param>
            public SavePaths(string directoryPath, string saveFilePath, string fileName, string folderName)
            {
                DirectoryPath = directoryPath;
                SaveFilePath = saveFilePath;
                FileName = fileName;
                FolderName = folderName;
            }
        }

        /// <summary>
        /// 校验并解析文件名/文件夹名为完整路径集合（须在调用线程执行：触达 persistentDataPath 与设置实例）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="folderName">文件夹名称。</param>
        /// <returns>路径集合。</returns>
        internal static SavePaths ResolveSavePaths(string fileName, string folderName)
        {
            ValidateFileName(fileName);
            ValidateFolderName(folderName);
            string directoryPath = BuildFolderPath(folderName);
            string saveFilePath = Path.Combine(directoryPath, DetermineSaveFileName(fileName));
            return new SavePaths(directoryPath, saveFilePath, fileName, folderName);
        }

        /// <summary>
        /// 拼装存档文件夹完整路径（以目录分隔符结尾）。
        /// </summary>
        /// <param name="folderName">已校验的文件夹名称（可为空 = 数据根目录）。</param>
        /// <returns>文件夹路径。</returns>
        private static string BuildFolderPath(string folderName)
        {
            string directoryPath = string.IsNullOrEmpty(folderName)
                ? Path.Combine(BasePath, DATA_FOLDER_NAME)
                : Path.Combine(BasePath, DATA_FOLDER_NAME, folderName);
            return directoryPath + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// 拼装存档数据根目录完整路径。
        /// </summary>
        /// <returns>根目录路径。</returns>
        private static string BuildDataRootDirectory()
        {
            return Path.Combine(BasePath, DATA_FOLDER_NAME);
        }

        /// <summary>存档根路径（persistentDataPath，首次访问缓存；仅可在主线程访问；测试可经 <c>s_OverrideBasePath</c> 覆盖）。</summary>
        private static string BasePath
        {
            get
            {
                if (s_OverrideBasePath != null)
                {
                    return s_OverrideBasePath;
                }

                return s_BasePath ??= Application.persistentDataPath;
            }
        }

        /// <summary>
        /// 判断要保存的文件名称（自动追加配置的扩展名）。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <returns>保存文件名。</returns>
        private static string DetermineSaveFileName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName) + SaveServiceSettings.SaveFileExtension;
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
        /// 校验文件名：非空、不含路径分隔符/穿越段/非法字符/Windows 保留设备名/临时文件后缀。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        private static void ValidateFileName(string fileName)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Save file name is null or empty.", nameof(fileName));
            }

            if (fileName.IndexOfAny(s_PathSeparators) >= 0
                || fileName.Contains("..")
                || fileName.Contains(FileSaveStorageBackend.TEMP_FILE_SUFFIX))
            {
                throw new ArgumentException(StringUtility.Format("Save file name '{0}' contains path separators or reserved segments.", fileName), nameof(fileName));
            }

            // 先对原始输入做非法字符校验：GetFileNameWithoutExtension 会把 ':' 当卷分隔符切掉前段（"slot:1" → "1"），仅查基名会漏网
            if (fileName.IndexOfAny(s_InvalidFileNameChars) >= 0)
            {
                throw new ArgumentException(StringUtility.Format("Save file name '{0}' contains invalid characters.", fileName), nameof(fileName));
            }

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (s_ReservedDeviceNames.Contains(baseName))
            {
                throw new ArgumentException(StringUtility.Format("Save file name '{0}' is a reserved device name.", fileName), nameof(fileName));
            }
        }

        /// <summary>
        /// 校验文件夹名称：可为空（数据根目录）；不含路径分隔符/穿越段/盘符/非法字符。
        /// </summary>
        /// <param name="folderName">文件夹名称。</param>
        private static void ValidateFolderName(string folderName)
        {
            if (folderName == null)
            {
                throw new ArgumentNullException(nameof(folderName));
            }

            if (folderName.Length == 0)
            {
                return;
            }

            if (folderName.IndexOfAny(s_PathSeparators) >= 0
                || folderName.Contains("..")
                || folderName.Contains(":")
                || folderName.Contains(FileSaveStorageBackend.TEMP_FILE_SUFFIX)
                || folderName == ".")
            {
                throw new ArgumentException(StringUtility.Format("Save folder name '{0}' contains path separators or reserved segments.", folderName), nameof(folderName));
            }

            if (folderName.IndexOfAny(s_InvalidFileNameChars) >= 0)
            {
                throw new ArgumentException(StringUtility.Format("Save folder name '{0}' contains invalid characters.", folderName), nameof(folderName));
            }
        }

        /// <summary>
        /// 校验数据块键：非空白、长度受限、不含控制字符/路径分隔符、禁止保留前缀。
        /// </summary>
        /// <remarks>
        /// 保留前缀（<c>__</c>）禁止用户新建块，但对既有保留块（<c>__main__</c>/<c>__meta</c>）的读写/删除合法； <br />
        /// 经 <see cref="MAIN_BLOCK_KEY"/>/<see cref="META_BLOCK_KEY"/> 常量访问时豁免前缀校验。
        /// </remarks>
        /// <param name="key">数据块键。</param>
        private static void ValidateBlockKey(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Save block key is null or empty.", nameof(key));
            }

            if (key.Length > MAX_BLOCK_KEY_LENGTH)
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' exceeds the max length {1}.", key, MAX_BLOCK_KEY_LENGTH), nameof(key));
            }

            if (key.IndexOfAny(s_PathSeparators) >= 0 || key.IndexOfAny(s_InvalidFileNameChars) >= 0)
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' contains invalid characters.", key), nameof(key));
            }

            // 保留前缀仅豁免框架专用保留块（__main__/__meta），其余 __ 前缀一律拒绝
            if (key.StartsWith(RESERVED_BLOCK_KEY_PREFIX, StringComparison.Ordinal)
                && !string.Equals(key, MAIN_BLOCK_KEY, StringComparison.Ordinal)
                && !string.Equals(key, META_BLOCK_KEY, StringComparison.Ordinal))
            {
                throw new ArgumentException(StringUtility.Format("Save block key '{0}' uses the reserved prefix '{1}'.", key, RESERVED_BLOCK_KEY_PREFIX), nameof(key));
            }
        }

        /// <summary>
        /// 构建 Windows 保留设备名集合（CON/PRN/AUX/NUL/COM1-9/LPT1-9）。
        /// </summary>
        /// <returns>保留设备名集合（忽略大小写）。</returns>
        private static HashSet<string> BuildReservedDeviceNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL"
            };

            for (int i = 1; i <= 9; i++)
            {
                names.Add("COM" + i);
                names.Add("LPT" + i);
            }

            return names;
        }
    }
}
