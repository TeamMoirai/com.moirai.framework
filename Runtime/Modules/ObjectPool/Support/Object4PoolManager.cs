using UnityEngine;
using UnityEngine.Events;

namespace Moirai.Atropos.ObjectPool
{
    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
    public class Object4PoolManager : MonoBehaviour
    {
        [Header("事件 [Events]")]
        [SerializeField] private UnityEvent m_OnEnable;
        [SerializeField] private UnityEvent m_OnDisable;

        [Header("Poolable Object")]
        [Tooltip("对象的生命周期(以秒为单位)。0 表示永远存在")]
        [SerializeField] private float m_LifeTime = 0f;

        // 对象池引用对象
        private PoolObject PoolObjectRef { get; set; }

        /// <summary>
        /// 生成一个实例对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Spawn<T>() where T : Object4PoolManager
        {
            PoolObject poolObject = GameObjectPoolMgr.Instance.Spawn(gameObject);
            T result = poolObject.TargetGameObject.GetComponent<T>();
            // 注册对象池引用对象
            result.PoolObjectRef = poolObject;
            return result;
        }

        /// <summary>
        /// 将实例变为非活动状态，以便复用。
        /// </summary>
        public virtual void Despawn()
        {
            GameObjectPoolMgr.Instance.Despawn(PoolObjectRef);
        }

        /// <summary>
        /// 当对象被启用时（通常是在从 ObjectPooler 池中池化之后），启动其生命周期结束倒计时。
        /// </summary>
        protected virtual void OnEnable()
        {
            if (m_LifeTime > 0f)
            {
                Invoke(nameof(Despawn), m_LifeTime);
            }
            m_OnEnable?.Invoke();
        }

        /// <summary>
        /// 当对象被禁用时（可能它越界了），取消它的程序化死亡
        /// </summary>
        protected virtual void OnDisable()
        {
            m_OnDisable?.Invoke();
            CancelInvoke();
        }
    }
}