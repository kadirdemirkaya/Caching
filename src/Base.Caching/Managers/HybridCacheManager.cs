using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Base.Caching.Managers
{
    public class HybridCacheManager : CacheKeyService, ICacheLocker, ICacheManager
    {
        private readonly MemoryCacheManager _memoryCache;
        private readonly DistributedCacheManager _distributedCache;
        private readonly HybridCacheInvalidationListener? _invalidationListener;

        public HybridCacheManager(
            MemoryCacheManager memoryCache,
            DistributedCacheManager distributedCache,
            IConnectionMultiplexer connectionMultiplexer = null,
            BaseDistributedCacheConfiguration configuration = null,
            ILogger<HybridCacheManager> logger = null)
        {
            _memoryCache = memoryCache;
            _distributedCache = distributedCache;

            var instanceName = configuration?.InstanceName ?? "Base.Caching";
            var channelName = $"{instanceName}:InvalidationChannel";

            if (connectionMultiplexer != null && memoryCache != null)
                _invalidationListener = HybridCacheInvalidationListener.GetOrCreate(memoryCache, connectionMultiplexer, channelName, logger);
        }

        private void PublishInvalidation(string action, string target)
        {
            _invalidationListener?.Publish(action, target);
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire)
        {
            if (key.CacheTime <= 0)
                return await acquire();

            var result = _memoryCache.Get<T>(key);
            if (result != null)
                return result;

            result = await _distributedCache.GetAsync<T>(key, acquire);

            if (result != null)
            {
                await _memoryCache.SetAsync(key, result);
            }

            return result;
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<T> acquire)
        {
            if (key.CacheTime <= 0)
                return acquire();

            var result = _memoryCache.Get<T>(key);
            if (result != null)
                return result;

            result = await _distributedCache.GetAsync<T>(key, acquire);

            if (result != null)
            {
                await _memoryCache.SetAsync(key, result);
            }

            return result;
        }

        public T Get<T>(CacheKey key)
        {
            if (key.CacheTime <= 0)
                return default;

            var result = _memoryCache.Get<T>(key);
            if (result != null)
                return result;

            result = _distributedCache.Get<T>(key);

            if (result != null)
            {
                _memoryCache.Set(key, result);
            }

            return result;
        }

        public T Get<T>(CacheKey key, Func<T> acquire)
        {
            if (key.CacheTime <= 0)
                return acquire();

            var result = _memoryCache.Get<T>(key);
            if (result != null)
                return result;

            result = _distributedCache.Get<T>(key, acquire);

            if (result != null)
            {
                _memoryCache.Set(key, result);
            }

            return result;
        }

        public async Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            var key = PrepareKey(cacheKey, cacheKeyParameters);

            await _distributedCache.RemoveAsync(cacheKey, cacheKeyParameters);
            await _memoryCache.RemoveAsync(cacheKey, cacheKeyParameters);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveAction, key.Key);
        }

        public async Task SetAsync(CacheKey key, object data)
        {
            if ((key?.CacheTime ?? 0) <= 0 || data == null)
                return;

            await _distributedCache.SetAsync(key, data);
            await _memoryCache.SetAsync(key, data);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveAction, key.Key);
        }

        public async Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            var formattedPrefix = PrepareKeyPrefix(prefix, prefixParameters);

            await _distributedCache.RemoveByPrefixAsync(prefix, prefixParameters);
            await _memoryCache.RemoveByPrefixAsync(prefix, prefixParameters);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveByPrefixAction, formattedPrefix);
        }

        public async Task ClearAsync()
        {
            await _distributedCache.ClearAsync();
            await _memoryCache.ClearAsync();

            PublishInvalidation(HybridCacheInvalidationListener.ClearAction, string.Empty);
        }

        public bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action)
        {
            return _distributedCache.PerformActionWithLock(resource, expirationTime, action);
        }

        public async Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            return await _distributedCache.PerformActionWithLockAsync(resource, expirationTime, action);
        }
    }
}
