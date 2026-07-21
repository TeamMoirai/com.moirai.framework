namespace Moirai.Atropos
{
    /// <summary>
    /// Clas 单例 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SingletonRegister<T> where T : class, new()
    {
        private static T s_Instance;
        private static readonly object s_Locker = new object();
        
        public static T Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    lock (s_Locker)
                    {
                        s_Instance ??= new T();
                    }
                }
                
                return s_Instance;
            }
        }
    }
}