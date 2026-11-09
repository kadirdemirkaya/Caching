namespace Base.Caching.Interfaces
{
    /// <summary>
    /// Runs an action while holding a named lock, so that only one caller runs it at a time.
    /// </summary>
    public interface ICacheLocker
    {
        /// <summary>
        /// Runs <paramref name="action"/> while holding the lock <paramref name="resource"/> for at most
        /// <paramref name="expirationTime"/>.
        /// </summary>
        /// <param name="resource">Name of the lock.</param>
        /// <param name="expirationTime">Time after which the lock expires even if it was not released.</param>
        /// <param name="action">Action run while the lock is held.</param>
        /// <returns><c>true</c> when the lock was taken and the action ran; <c>false</c> when the lock is held by
        /// another caller. The lock is released after the action, also when it throws.</returns>
        bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action);

        /// <summary>
        /// Runs <paramref name="action"/> while holding the lock <paramref name="resource"/> for at most
        /// <paramref name="expirationTime"/>.
        /// </summary>
        /// <param name="resource">Name of the lock.</param>
        /// <param name="expirationTime">Time after which the lock expires even if it was not released.</param>
        /// <param name="action">Action run while the lock is held.</param>
        /// <param name="cancellationToken">Cancels the call before the lock is taken. The lock is always released after
        /// the action, even when the token is cancelled meanwhile.</param>
        /// <returns><c>true</c> when the lock was taken and the action ran; <c>false</c> when the lock is held by
        /// another caller.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the
        /// lock was taken.</exception>
        Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs <paramref name="action"/> while holding the lock <paramref name="resource"/> for at most
        /// <paramref name="expirationTime"/>; the action receives <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="resource">Name of the lock.</param>
        /// <param name="expirationTime">Time after which the lock expires even if it was not released.</param>
        /// <param name="action">Action run while the lock is held; receives <paramref name="cancellationToken"/>.</param>
        /// <param name="cancellationToken">Cancels the call before the lock is taken and is passed to
        /// <paramref name="action"/>. The lock is always released after the action, even when the token is cancelled
        /// meanwhile.</param>
        /// <returns><c>true</c> when the lock was taken and the action ran; <c>false</c> when the lock is held by
        /// another caller.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the
        /// lock was taken.</exception>
        Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
    }
}
