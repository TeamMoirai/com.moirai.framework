using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 框架插件源的浏览与装卸窗口。
    /// </summary>
    /// <remarks>
    /// 本类只声明这一套管理器的身份与文案，交互与编排全部由 <see cref="PackageManagerWindowBase"/> 承担。<br />
    /// 另一条分支上的可安装包要在别的包/仓库里复用这套交互时，照本类写一份派生窗与
    /// <see cref="PackageManagerWindowBase.PackageManagerProfile"/> 即可，两份管理器各自持有独立的状态账本与检出落点。
    /// </remarks>
    internal sealed class PackageManagerWindow : PackageManagerWindowBase
    {
        private const int MENU_PRIORITY = 2300;

        [MenuItem("Tools/Moirai/Framework Package Manager", false, MENU_PRIORITY)]
        private static void Open()
        {
            PackageManagerWindow window = GetWindow<PackageManagerWindow>();
            window.titleContent = new GUIContent(FrameworkPackageProfile.Instance.WindowTitle);
            window.minSize = new Vector2(960f, 520f);
            window.Show();
        }

        protected override PackageManagerProfile CreateProfile()
        {
            return FrameworkPackageProfile.Instance;
        }
    }
}
