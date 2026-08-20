namespace Moirai.Atropos
{
    public static partial class GameServices
    {
        /// <summary>
        /// 同一合约接口在三个作用域中的绑定。class 而非 struct——字典中直接修改字段无需回写。
        /// </summary>
        internal class ScopeBindings
        {
            public IService App { get; set; }
            public IService Scene { get; set; }
            public IService Gameplay { get; set; }

            public bool IsEmpty => App == null && Scene == null && Gameplay == null;

            /// <summary>获取指定作用域的绑定。</summary>
            public IService Get(EServiceScopeKind scope)
            {
                switch (scope)
                {
                    case EServiceScopeKind.App: return App;
                    case EServiceScopeKind.Scene: return Scene;
                    case EServiceScopeKind.Gameplay: return Gameplay;
                    default: return null;
                }
            }

            /// <summary>
            /// 跨作用域遮蔽查找：Gameplay > Scene > App。
            /// 用于运行时临时替换全局实现（如战斗内替换 ITimerService）。
            /// </summary>
            public IService GetBest()
            {
                if (Gameplay != null) return Gameplay;
                if (Scene != null) return Scene;
                return App;
            }
        }
    }
}
