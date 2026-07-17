using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 这个属性会让自定义编辑器不断重新绘制
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ConstantRepaintAttribute : Attribute
    {

        #region Fields

        /// <summary>
        /// 只需要在运行时不断重新喷漆
        /// </summary>
        public bool runtimeOnly;

        #endregion

        #region Constructors

        public ConstantRepaintAttribute()
        {
            runtimeOnly = false;
        }

        public ConstantRepaintAttribute(bool runtimeOnly)
        {
            this.runtimeOnly = runtimeOnly;
        }

        #endregion

    }
}