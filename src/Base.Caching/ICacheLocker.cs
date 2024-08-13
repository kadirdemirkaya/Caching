namespace Base.Caching
{
    public interface ICacheLocker
    {
        bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action);
        Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action);
    }
}
