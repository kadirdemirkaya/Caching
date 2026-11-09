using Base.Caching.Key;

namespace Base.Caching.Interfaces
{
    /// <summary>
    /// Reads, writes and removes cache entries and prepares cache keys.
    /// </summary>
    public interface ICacheManager
    {
        /// <summary>
        /// Returns the value cached under <paramref name="key"/>, or loads it with <paramref name="acquire"/> and caches it.
        /// </summary>
        /// <typeparam name="T">Type of the cached value.</typeparam>
        /// <param name="key">Prepared cache key.</param>
        /// <param name="acquire">Loads the value on a cache miss.</param>
        /// <param name="cancellationToken">Cancels the cache operations (passed to the distributed store).</param>
        /// <returns>The cached or loaded value.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the value cached under <paramref name="key"/>, or loads it with <paramref name="acquire"/> and caches it;
        /// <paramref name="acquire"/> receives <paramref name="cancellationToken"/>.
        /// </summary>
        /// <typeparam name="T">Type of the cached value.</typeparam>
        /// <param name="key">Prepared cache key.</param>
        /// <param name="acquire">Loads the value on a cache miss; receives <paramref name="cancellationToken"/>.</param>
        /// <param name="cancellationToken">Cancels the cache operations (passed to the distributed store) and is passed to
        /// <paramref name="acquire"/>.</param>
        /// <returns>The cached or loaded value.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task<T> GetAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> acquire, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the value cached under <paramref name="key"/>, or loads it with <paramref name="acquire"/> and caches it.
        /// </summary>
        /// <typeparam name="T">Type of the cached value.</typeparam>
        /// <param name="key">Prepared cache key.</param>
        /// <param name="acquire">Loads the value on a cache miss.</param>
        /// <param name="cancellationToken">Cancels the cache operations (passed to the distributed store).</param>
        /// <returns>The cached or loaded value.</returns>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task<T> GetAsync<T>(CacheKey key, Func<T> acquire, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the value cached under <paramref name="key"/> without loading it.
        /// </summary>
        /// <typeparam name="T">Type of the cached value.</typeparam>
        /// <param name="key">Prepared cache key.</param>
        /// <returns>The cached value, or <c>default</c> when nothing is cached, the key is <c>null</c> or its
        /// <see cref="CacheKey.CacheTime"/> is not positive.</returns>
        T? Get<T>(CacheKey key);

        /// <summary>
        /// Returns the value cached under <paramref name="key"/>, or loads it with <paramref name="acquire"/> and caches it.
        /// </summary>
        /// <typeparam name="T">Type of the cached value.</typeparam>
        /// <param name="key">Prepared cache key; when its <see cref="CacheKey.CacheTime"/> is not positive,
        /// <paramref name="acquire"/> is called and nothing is cached.</param>
        /// <param name="acquire">Loads the value on a cache miss; a <c>null</c> result is not cached.</param>
        /// <returns>The cached or loaded value.</returns>
        T Get<T>(CacheKey key, Func<T> acquire);

        /// <summary>
        /// Removes the entry of <paramref name="cacheKey"/>, formatted with <paramref name="cacheKeyParameters"/>.
        /// </summary>
        /// <param name="cacheKey">Cache key, optionally a format string for <paramref name="cacheKeyParameters"/>.</param>
        /// <param name="cacheKeyParameters">Values formatted into <paramref name="cacheKey"/>.</param>
        Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters);

        /// <summary>
        /// Removes the entry of <paramref name="cacheKey"/>, formatted with <paramref name="cacheKeyParameters"/>.
        /// </summary>
        /// <param name="cacheKey">Cache key, optionally a format string for <paramref name="cacheKeyParameters"/>.</param>
        /// <param name="cancellationToken">Cancels the operation (passed to the distributed store).</param>
        /// <param name="cacheKeyParameters">Values formatted into <paramref name="cacheKey"/>.</param>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task RemoveAsync(CacheKey cacheKey, CancellationToken cancellationToken, params object[] cacheKeyParameters);

        /// <summary>
        /// Caches <paramref name="data"/> under <paramref name="key"/>. Nothing is stored when <paramref name="data"/> is
        /// <c>null</c> or the key's cache time is not positive.
        /// </summary>
        /// <param name="key">Prepared cache key; its <see cref="CacheKey.CacheDuration"/> and
        /// <see cref="CacheKey.SlidingExpiration"/> set the expiration.</param>
        /// <param name="data">Value to cache.</param>
        /// <param name="cancellationToken">Cancels the operation (passed to the distributed store).</param>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task SetAsync(CacheKey key, object data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the cache entries selected by <paramref name="prefix"/>, formatted with <paramref name="prefixParameters"/>
        /// like a key. What the prefix is compared with depends on the store:
        /// <list type="bullet">
        /// <item><description>In memory (<c>MemoryCacheManager</c>, the in-memory fallback of <c>DistributedCacheManager</c>
        /// and the local level of <c>HybridCacheManager</c>): entries whose <see cref="CacheKey.Prefixes"/> contain the
        /// prefix as an exact, case-sensitive string. The key name is not compared.</description></item>
        /// <item><description>In Redis (<c>DistributedCacheManager</c> and the distributed level of
        /// <c>HybridCacheManager</c>): keys whose name starts with <c>{InstanceName}{prefix}</c>, compared literally.
        /// <see cref="CacheKey.Prefixes"/> are not stored in Redis and are not compared.</description></item>
        /// </list>
        /// <c>DistributedCacheManager</c> also removes the values it cached for the current HTTP request whose key starts
        /// with the prefix, on both the Redis and the fallback path.
        /// A key is removed by every store when its name starts with the prefix and the same prefix is listed in its
        /// <see cref="CacheKey.Prefixes"/>, for example <c>new CacheKey("user.{0}", "user.")</c> removed with
        /// <c>RemoveByPrefixAsync("user.")</c>.
        /// </summary>
        /// <param name="prefix">Prefix, optionally a format string for <paramref name="prefixParameters"/>.</param>
        /// <param name="prefixParameters">Values formatted into <paramref name="prefix"/>.</param>
        Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters);

        /// <summary>
        /// Removes the cache entries selected by <paramref name="prefix"/>, formatted with
        /// <paramref name="prefixParameters"/>; same semantics as
        /// <see cref="RemoveByPrefixAsync(string, object[])"/>. In Redis the token is checked between delete batches, so a
        /// cancelled call may have removed part of the keys.
        /// </summary>
        /// <param name="prefix">Prefix, optionally a format string for <paramref name="prefixParameters"/>.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <param name="prefixParameters">Values formatted into <paramref name="prefix"/>.</param>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken, params object[] prefixParameters);

        /// <summary>
        /// Removes every entry of the cache. In Redis the token is checked between delete batches, so a cancelled call may
        /// have removed part of the keys.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task ClearAsync(CancellationToken cancellationToken = default);




        /// <summary>
        /// Returns a copy of <paramref name="cacheKey"/> whose key and prefixes are formatted with
        /// <paramref name="cacheKeyParameters"/>; the expiration is kept.
        /// </summary>
        /// <param name="cacheKey">Key template.</param>
        /// <param name="cacheKeyParameters">Values formatted into the key and its prefixes.</param>
        /// <returns>The prepared key.</returns>
        CacheKey PrepareKey(CacheKey cacheKey, params object[] cacheKeyParameters);

        /// <summary>
        /// Like <see cref="PrepareKey(CacheKey, object[])"/>, and sets the cache time of the prepared key to
        /// <see cref="CachingDefaults.CacheTime"/>.
        /// </summary>
        /// <param name="cacheKey">Key template.</param>
        /// <param name="cacheKeyParameters">Values formatted into the key and its prefixes.</param>
        /// <returns>The prepared key.</returns>
        CacheKey PrepareKeyForDefaultCache(CacheKey cacheKey, params object[] cacheKeyParameters);

        /// <summary>
        /// Like <see cref="PrepareKey(CacheKey, object[])"/>, and sets the cache time of the prepared key to
        /// <see cref="CachingDefaults.ShortCacheTime"/>.
        /// </summary>
        /// <param name="cacheKey">Key template.</param>
        /// <param name="cacheKeyParameters">Values formatted into the key and its prefixes.</param>
        /// <returns>The prepared key.</returns>
        CacheKey PrepareKeyForShortTermCache(CacheKey cacheKey, params object[] cacheKeyParameters);

    }
}
