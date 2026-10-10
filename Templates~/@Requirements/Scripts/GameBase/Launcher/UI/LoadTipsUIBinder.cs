using System.Text;
using Moirai.Atropos.UI;
using UnityEngine;

namespace Moirai.GameMain.UI
{
    /// <summary>
    /// 热更提示窗绑定器的手写分部：声明 <see cref="ILauncherBinder"/> 接缝，
    /// 用 <c>LoadTipsUI.g.cs</c> 生成的私有序列化字段报名未接线的槽位。
    /// </summary>
    public partial class LoadTipsUIBinder : UIBindComponent, ILauncherBinder
    {
        /// <inheritdoc />
        public string DescribeUnboundSlots()
        {
            var builder = new StringBuilder();
            AppendIfNull(builder, nameof(m_TextDesc), m_TextDesc);
            AppendIfNull(builder, nameof(m_BtnConfirm), m_BtnConfirm);
            AppendIfNull(builder, nameof(m_BtnUpdate), m_BtnUpdate);
            AppendIfNull(builder, nameof(m_BtnCancel), m_BtnCancel);
            AppendIfNull(builder, nameof(m_TextConfirm), m_TextConfirm);
            AppendIfNull(builder, nameof(m_TextUpdate), m_TextUpdate);
            AppendIfNull(builder, nameof(m_TextCancel), m_TextCancel);
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
