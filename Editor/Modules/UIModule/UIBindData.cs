using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    /// <summary>
    /// 绑定类型枚举，定义UI组件的绑定方式
    /// </summary>
    public enum EBindType
    {
        /// <summary>默认绑定</summary>
        None,
        /// <summary>UIWidget绑定</summary>
        Widget,
        /// <summary>数组组件绑定</summary>
        ListCom
    }

    /// <summary>
    /// UI绑定数据，存储单个组件的绑定信息
    /// </summary>
    [Serializable]
    public class UIBindData
    {
        /// <summary>绑定字段名</summary>
        public string Name { get; }

        /// <summary>绑定的GameObject列表（数组类型时包含多个）</summary>
        public List<GameObject> Objs { get; set; }

        /// <summary>绑定类型</summary>
        public EBindType BindType { get; }

        /// <summary>组件类型</summary>
        public Type ComponentType { get; private set; }

        /// <summary>解析后的组件类型列表</summary>
        public List<Type> ResolvedComponentTypes { get; }

        /// <summary>是否为GameObject类型</summary>
        public bool IsGameObject => ComponentType == typeof(GameObject);

        /// <summary>组件类型全名</summary>
        public string TypeName => ComponentType?.FullName ?? string.Empty;

        /// <summary>
        /// 获取第一个组件类型
        /// </summary>
        public Type GetFirstOrDefaultType() => ComponentType;

        public UIBindData(string name, List<GameObject> objs, Type componentType = null, EBindType bindType = EBindType.None)
        {
            Name = name;
            Objs = objs ?? new List<GameObject>();
            BindType = bindType;
            ComponentType = componentType;
            ResolvedComponentTypes = new List<Type>();

            if (componentType != null && Objs.Count > 0)
            {
                for (var i = 0; i < Objs.Count; i++)
                {
                    ResolvedComponentTypes.Add(componentType);
                }
            }
        }

        public UIBindData(string name, GameObject obj, Type componentType = null, EBindType bindType = EBindType.None)
            : this(name, new List<GameObject> { obj }, componentType, bindType)
        {
        }

        /// <summary>
        /// 设置组件类型
        /// </summary>
        public void SetComponentType(Type componentType)
        {
            ComponentType = componentType;
        }

        /// <summary>
        /// 添加解析后的对象和组件类型
        /// </summary>
        public void AddResolvedObject(GameObject obj, Type componentType)
        {
            if (obj == null)
            {
                return;
            }

            Objs.Add(obj);
            ResolvedComponentTypes.Add(componentType ?? ComponentType);
        }

        /// <summary>
        /// 获取指定索引的解析后组件类型
        /// </summary>
        public Type GetResolvedComponentType(int index)
        {
            if (index >= 0 && index < ResolvedComponentTypes.Count && ResolvedComponentTypes[index] != null)
            {
                return ResolvedComponentTypes[index];
            }

            return ComponentType;
        }
    }
}