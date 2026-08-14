namespace Moirai.Atropos
{
    public static partial class ModuleSystem
    {
        internal struct ScopeBindings
        {
            public Module App;
            public Module Scene;
            public Module Gameplay;

            public bool IsEmpty => App == null && Scene == null && Gameplay == null;

            /// <summary>获取指定作用域槽位的绑定，未注册时返回 null。</summary>
            public Module Get(ModuleScope scope)
            {
                switch (scope)
                {
                    case ModuleScope.App: return App;
                    case ModuleScope.Scene: return Scene;
                    case ModuleScope.Gameplay: return Gameplay;
                    default: return null;
                }
            }

            /// <summary>跨作用域查找：Gameplay > Scene > App。</summary>
            public Module GetBest()
            {
                if (Gameplay != null) return Gameplay;
                if (Scene != null) return Scene;
                return App;
            }
        }
    }
}