namespace Moirai.Atropos.Pooling
{
    public interface IObjectPool<T> where T : class
    {
        T Rent();
        void Return(T value);
        int Outstanding { get; }
    }
}
