using JetBrains.Annotations;

namespace Moirai.Atropos
{
    public static partial class StringUtility
    {
        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format(string format)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T>(string format, T arg)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }

            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            var sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }
            
            StringHandler.IStringBuilder sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }

            StringHandler.IStringBuilder sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }

            StringHandler.IStringBuilder sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
            }
            finally
            {
                sb.Dispose();
            }
        }

        /// <summary>
        /// 格式化字符串。format 为 null 时抛出 <see cref="GameException"/>。
        /// </summary>
        [StringFormatMethod("format")]
        public static string Format<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(string format, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16)
        {
            if (format == null)
            {
                throw new GameException("Format is invalid.");
            }

            StringHandler.IStringBuilder sb = CreateStringBuilder();
            try
            {
                return sb.Format(format, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16);
            }
            finally
            {
                sb.Dispose();
            }
        }
    }
}
