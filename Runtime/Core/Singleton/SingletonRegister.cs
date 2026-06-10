namespace Moirai.Atropos
{
    /// <summary>
    /// Clas 单例 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SingletonRegister<T> where T : class, new()
    {
        private static T m_Instance;
        private static readonly object m_Locker = new object();
        
        public static T Instance
        {
            get
            {
                if (m_Instance == null)
                {
                    lock (m_Locker)
                    {
                        m_Instance ??= new T();
                    }
                }
                
                return m_Instance;
            }
        }
    }
}