namespace Base.Caching
{
    public interface ICacheLocker
    {
        bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action);
    }
}
