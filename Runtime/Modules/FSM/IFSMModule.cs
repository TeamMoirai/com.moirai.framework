using System;
using System.Collections.Generic;

namespace Moirai.Atropos.FSM
{
    /// <summary>
    /// 有限状态机管理器。
    /// </summary>
    public interface IFSMModule
    {
        /// <summary>
        /// 获取有限状态机数量。
        /// </summary>
        int Count
        {
            get;
        }

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <returns>是否存在有限状态机。</returns>
        bool HasFSM<T>() where T : class;

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <returns>是否存在有限状态机。</returns>
        bool HasFSM(Type ownerType);

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">有限状态机名称。</param>
        /// <returns>是否存在有限状态机。</returns>
        bool HasFSM<T>(string name) where T : class;

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <param name="name">有限状态机名称。</param>
        /// <returns>是否存在有限状态机。</returns>
        bool HasFSM(Type ownerType, string name);

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <returns>要获取的有限状态机。</returns>
        IFSM<T> GetFSM<T>() where T : class;

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <returns>要获取的有限状态机。</returns>
        FSMBase GetFSM(Type ownerType);

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">有限状态机名称。</param>
        /// <returns>要获取的有限状态机。</returns>
        IFSM<T> GetFSM<T>(string name) where T : class;

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <param name="name">有限状态机名称。</param>
        /// <returns>要获取的有限状态机。</returns>
        FSMBase GetFSM(Type ownerType, string name);

        /// <summary>
        /// 获取所有有限状态机。
        /// </summary>
        /// <returns>所有有限状态机。</returns>
        // ReSharper disable once InconsistentNaming
        FSMBase[] GetAllFSMs();

        /// <summary>
        /// 获取所有有限状态机。
        /// </summary>
        /// <param name="results">所有有限状态机。</param>
        // ReSharper disable once InconsistentNaming
        void GetAllFSMs(List<FSMBase> results);

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>要创建的有限状态机。</returns>
        IFSM<T> CreateFSM<T>(T owner, params FSMState<T>[] states) where T : class;

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">有限状态机名称。</param>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>要创建的有限状态机。</returns>
        IFSM<T> CreateFSM<T>(string name, T owner, params FSMState<T>[] states) where T : class;

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>要创建的有限状态机。</returns>
        IFSM<T> CreateFSM<T>(T owner, List<FSMState<T>> states) where T : class;

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">有限状态机名称。</param>
        /// <param name="owner">有限状态机持有者。</param>
        /// <param name="states">有限状态机状态集合。</param>
        /// <returns>要创建的有限状态机。</returns>
        IFSM<T> CreateFSM<T>(string name, T owner, List<FSMState<T>> states) where T : class;

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <returns>是否销毁有限状态机成功。</returns>
        bool DestroyFSM<T>() where T : class;

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        bool DestroyFSM(Type ownerType);

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="name">要销毁的有限状态机名称。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        bool DestroyFSM<T>(string name) where T : class;

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <param name="ownerType">有限状态机持有者类型。</param>
        /// <param name="name">要销毁的有限状态机名称。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        bool DestroyFSM(Type ownerType, string name);

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <typeparam name="T">有限状态机持有者类型。</typeparam>
        /// <param name="fsm">要销毁的有限状态机。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        bool DestroyFSM<T>(IFSM<T> fsm) where T : class;

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        /// <param name="fsm">要销毁的有限状态机。</param>
        /// <returns>是否销毁有限状态机成功。</returns>
        bool DestroyFSM(FSMBase fsm);
    }
}
