using System;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// 资源包名下拉行的唯一构建器：清单取自 <see cref="ResourcePackageBridge.GetPackageNames"/>（按域缓存），
    /// 显示行缓存随清单换数组实例（<see cref="EditorApplication.projectChanged"/> 后桥重建）或后端默认包名变动而失效重建。
    /// </summary>
    /// <remarks>
    /// 三处宿主共用同一份行与置顶规则：<c>AssetReference</c> 引用行（带「留空」项）、<c>[ResourcePackageDropdown]</c> 特性行（不带，宿主字段为必填包名）、
    /// 以及包外自绘引用行的宿主（如玩法包 <c>ImageInfo</c> 的包名行）； <br />
    /// 逐绘制帧调用，常规路径零分配——悬空包名置顶是状态相关的稀有路径，才走每调用小数组。
    /// </remarks>
    public static class ResourcePackagePopup
    {
        /// <summary>「留空」状态项的标记文案（全角括号使其区别于真实包裹名）。</summary>
        private const string EMPTY_PACKAGE_TEXT = "（留空 · 默认资源包）";

        private const string EMPTY_PACKAGE_HINT = "留空：运行时使用后端配置的默认资源包";

        /// <summary>收集器配置缺失或未配置任何包裹时的占位项。</summary>
        public static readonly GUIContent NoPackages = new GUIContent("(No collector packages configured)");

        /// <summary>空清单占位行（绘制期零分配）。</summary>
        public static readonly GUIContent[] NoPackagesRow = { NoPackages };

        /// <summary>「仅包名」显示行缓存：随清单数组实例引用比对失效。</summary>
        private static GUIContent[] s_PlainRow;

        private static string[] s_PlainRowSource;

        /// <summary>「留空 + 包名」显示行缓存：清单换数组实例或默认包名变动（留空项提示语含它）时失效重建。</summary>
        private static GUIContent[] s_EmptyRow;

        private static string[] s_EmptyRowSource;

        private static string s_EmptyRowDefault;

        /// <summary>
        /// 构建包名下拉显示行：<c>GUIContent.text</c> 即写回包名（<paramref name="includeEmptyItem"/> 为真时 0 位是「留空」状态项，
        /// 其 <c>text</c> 非包名，取值请走 <see cref="Pick"/>）；当前值已不在清单中（包裹被改名/删除）时置顶（当前值与清单之间，恒在「留空」项后）；
        /// 空清单返回占位单元素行且 <paramref name="manifestEmpty"/> 置真、索引为 -1。
        /// </summary>
        /// <param name="current">当前包名；空串在带「留空」项时选中该项，不带时索引为 -1（无选中项）。</param>
        /// <param name="includeEmptyItem">顶部是否加「留空 · 默认资源包」项（留空即运行时用后端默认资源包）。</param>
        public static GUIContent[] Build(string current, bool includeEmptyItem, out int index, out bool manifestEmpty)
        {
            // 后端自报不消费包名轴时桥给 null：三处宿主都在进这里之前挡掉了这种后端，此处再兜一次空清单走占位项
            string[] names = ResourcePackageBridge.GetPackageNames() ?? Array.Empty<string>();
            if (names.Length == 0)
            {
                index = -1;
                manifestEmpty = true;
                return NoPackagesRow;
            }

            manifestEmpty = false;
            int offset = includeEmptyItem ? 1 : 0;
            GUIContent[] row = includeEmptyItem
                ? BuildEmptyItemRow(names)
                : BuildPlainRow(names);

            int at = IndexOf(names, current);
            if (string.IsNullOrEmpty(current))
            {
                index = offset > 0 ? 0 : -1;
                return row;
            }

            if (at >= 0)
            {
                index = at + offset;
                return row;
            }

            // 稀有路径：当前值悬空（包裹已改名/删除），置顶于留空项后
            var dangling = new GUIContent[names.Length + offset + 1];
            int w = 0;
            if (offset > 0)
            {
                dangling[w++] = row[0];
            }

            dangling[w++] = new GUIContent(current);
            for (int i = 0; i < names.Length; i++, w++)
            {
                dangling[w] = row[i + offset];
            }

            index = offset;
            return dangling;
        }

        /// <summary>
        /// 取下拉选中项要写回的包名：带「留空」项时 0 位的 <c>text</c> 是标记文案而非包名，写回空串（留空即运行时用后端默认资源包）。
        /// 调用方须自持 <c>selected</c> 的取值范围判定（越界不该写值）。
        /// </summary>
        public static string Pick(GUIContent[] row, bool includeEmptyItem, int selected) =>
            includeEmptyItem && selected == 0 ? string.Empty : row[selected].text;

        /// <summary>「仅包名」行：与清单同序，按数组引用失效重建。</summary>
        private static GUIContent[] BuildPlainRow(string[] names)
        {
            if (s_PlainRow != null && ReferenceEquals(s_PlainRowSource, names))
            {
                return s_PlainRow;
            }

            s_PlainRow = new GUIContent[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                s_PlainRow[i] = new GUIContent(names[i]);
            }

            s_PlainRowSource = names;
            return s_PlainRow;
        }

        /// <summary>「留空 + 包名」行：0 位为「留空」状态项，提示语含后端默认包名，两者任一变动即重建。</summary>
        /// <remarks>默认包名取自设置资产（<see cref="ResourceServiceSettings"/>）而非 <c>ResourceService</c>：编辑器里服务未初始化时后者句柄为 null。</remarks>
        private static GUIContent[] BuildEmptyItemRow(string[] names)
        {
            string defaultName = ResourceServiceSettings.ResourceServiceHandler?.DefaultPackageName ?? string.Empty;
            if (s_EmptyRow != null && ReferenceEquals(s_EmptyRowSource, names) && s_EmptyRowDefault == defaultName)
            {
                return s_EmptyRow;
            }

            s_EmptyRow = new GUIContent[names.Length + 1];
            s_EmptyRow[0] = new GUIContent(EMPTY_PACKAGE_TEXT,
                defaultName.Length == 0
                    ? EMPTY_PACKAGE_HINT
                    : $"留空：运行时使用后端默认资源包（{defaultName}）");
            for (int i = 0; i < names.Length; i++)
            {
                s_EmptyRow[i + 1] = new GUIContent(names[i]);
            }

            s_EmptyRowSource = names;
            s_EmptyRowDefault = defaultName;
            return s_EmptyRow;
        }

        /// <summary>包名在清单中的位置，无则 -1（逐绘制帧调用，零分配）。</summary>
        private static int IndexOf(string[] names, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return -1;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
