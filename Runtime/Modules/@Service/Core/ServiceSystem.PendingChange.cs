using System;

namespace Moirai.Atropos
{
    public static partial class ServiceSystem
    {
        /// <summary>
        /// 迭代期间产生的延迟变更（注册/注销），本轮迭代结束后统一应用。
        /// </summary>
        internal struct PendingChange
        {
            public readonly bool IsRegister;
            public readonly IService Service;
            public readonly Type InterfaceType;
            public readonly ServiceScope Scope;

            private PendingChange(bool isRegister, IService service, Type interfaceType, ServiceScope scope)
            {
                IsRegister = isRegister;
                Service = service;
                InterfaceType = interfaceType;
                Scope = scope;
            }

            public static PendingChange Register(IService service, Type interfaceType, ServiceScope scope)
                => new PendingChange(true, service, interfaceType, scope);

            /// <summary>注销无需接口与作用域信息，由服务 entry 直接持有。</summary>
            public static PendingChange Unregister(IService service)
                => new PendingChange(false, service, null, default);
        }
    }
}
