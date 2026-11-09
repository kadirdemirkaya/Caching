namespace Base.Caching.Lock;

/// <summary>
/// Type of lock entered by <see cref="ReaderWriteLockDisposable"/>.
/// </summary>
public enum ReaderWriteLockType
{
    /// <summary>
    /// Read lock.
    /// </summary>
    Read,
    /// <summary>
    /// Write lock.
    /// </summary>
    Write,
    /// <summary>
    /// Upgradeable read lock.
    /// </summary>
    UpgradeableRead
}
