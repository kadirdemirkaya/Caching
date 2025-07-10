using Base.Caching.Key;

namespace Base.Caching
{
    public interface ICacheManager
    {
        Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire);

        Task<T> GetAsync<T>(CacheKey key, Func<T> acquire);

        T Get<T>(CacheKey key);

        T Get<T>(CacheKey key, Func<T> acquire);

        Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters);

        Task SetAsync(CacheKey key, object data);

        Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters);

        Task ClearAsync();




        CacheKey PrepareKey(CacheKey cacheKey, params object[] cacheKeyParameters);

        CacheKey PrepareKeyForDefaultCache(CacheKey cacheKey, params object[] cacheKeyParameters);

        CacheKey PrepareKeyForShortTermCache(CacheKey cacheKey, params object[] cacheKeyParameters);

    }
}
