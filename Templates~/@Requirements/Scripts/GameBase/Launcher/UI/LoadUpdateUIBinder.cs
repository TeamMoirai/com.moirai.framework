using System.Text;
using Moirai.Atropos.UI;
using UnityEngine;

namespace Moirai.GameMain.UI
{
    /// <summary>
    /// 热更进度窗绑定器的手写分部：声明 <see cref="ILauncherBinder"/> 接缝，
    /// 用 <c>LoadUpdateUI.g.cs</c> 生成的私有序列化字段报名未接线的槽位。
    /// </summary>
    public partial class LoadUpdateUIBinder : UIBindComponent, ILauncherBinder
    {
        /// <inheritdoc />
        public string DescribeUnboundSlots()
        {
            var builder = new StringBuilder();
            AppendIfNull(builder, nameof(m_SliderProgress), m_SliderProgress);
            AppendIfNull(builder, nameof(m_TextUpdateDesc), m_TextUpdateDesc);
            AppendIfNull(builder, nameof(m_TextVersion), m_TextVersion);
            AppendIfNull(builder, nameof(m_TextAppid), m_TextAppid);
            return builder.Length == 0 ? null : builder.ToString();
        }

        private static void AppendIfNull(StringBuilder builder, string slotName, Object value)
        {
            if (value != null)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(slotName);
        }
    }
}
