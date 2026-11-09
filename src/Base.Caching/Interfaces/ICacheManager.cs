using Base.Caching.Key;

namespace Base.Caching.Interfaces
{
    public interface ICacheManager
    {
        Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire);

        Task<T> GetAsync<T>(CacheKey key, Func<T> acquire);

        /// <summary>
        /// Returns the value cached under <paramref name="key"/> without loading it.
        /// </summary>
        /// <typeparam name="T">Type of the cached value.</typeparam>
        /// <param name="key">Prepared cache key.</param>
        /// <returns>The cached value, or <c>default</c> when nothing is cached, the key is <c>null</c> or its
        /// <see cref="CacheKey.CacheTime"/> is not positive.</returns>
        T? Get<T>(CacheKey key);

        T Get<T>(CacheKey key, Func<T> acquire);

        Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters);

        Task SetAsync(CacheKey key, object data);

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

        Task ClearAsync();




        CacheKey PrepareKey(CacheKey cacheKey, params object[] cacheKeyParameters);

        CacheKey PrepareKeyForDefaultCache(CacheKey cacheKey, params object[] cacheKeyParameters);

        CacheKey PrepareKeyForShortTermCache(CacheKey cacheKey, params object[] cacheKeyParameters);

    }
}
