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
            IConnectionMultiplexer? connectionMultiplexer = null,
            BaseDistributedCacheConfiguration? configuration = null,
            ILogger<HybridCacheManager>? logger = null)
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

        public Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire, CancellationToken cancellationToken = default)
        {
            return GetAsync(key, _ => acquire(), cancellationToken);
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> acquire, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (key.CacheTime <= 0)
                return await acquire(cancellationToken);

            if (_memoryCache.TryGet(key, out T result))
                return result;

            if (!_memoryCache.PreventConcurrentAcquire)
                return await LoadAsync(key, () => _distributedCache.GetAsync<T>(key, acquire, cancellationToken), cancellationToken);

            using (await _memoryCache.AcquireLocks.LockAsync(key.Key, cancellationToken))
            {
                if (_memoryCache.TryGet(key, out result))
                    return result;

                return await LoadAsync(key, () => _distributedCache.GetOrAcquireAsync(key, acquire, false, cancellationToken), cancellationToken);
            }
        }

        private async Task<T> LoadAsync<T>(CacheKey key, Func<Task<T>> load, CancellationToken cancellationToken)
        {
            var result = await load();

            if (result != null)
            {
                await _memoryCache.SetAsync(key, result, cancellationToken);
            }

            return result;
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<T> acquire, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (key.CacheTime <= 0)
                return acquire();

            if (_memoryCache.TryGet(key, out T result))
                return result;

            if (!_memoryCache.PreventConcurrentAcquire)
                return await LoadAsync(key, () => _distributedCache.GetAsync<T>(key, acquire, cancellationToken), cancellationToken);

            using (await _memoryCache.AcquireLocks.LockAsync(key.Key, cancellationToken))
            {
                if (_memoryCache.TryGet(key, out result))
                    return result;

                return await LoadAsync(key, () => _distributedCache.GetOrAcquireAsync(key, _ => Task.FromResult(acquire()), false, cancellationToken), cancellationToken);
            }
        }

        public T? Get<T>(CacheKey key)
        {
            if (key.CacheTime <= 0)
                return default;

            if (_memoryCache.TryGet(key, out T result))
                return result;

            if (!_distributedCache.TryGet(key, out result))
                return result;

            _memoryCache.Set(key, result!);

            return result;
        }

        public T Get<T>(CacheKey key, Func<T> acquire)
        {
            if (key.CacheTime <= 0)
                return acquire();

            if (_memoryCache.TryGet(key, out T result))
                return result;

            result = _distributedCache.Get<T>(key, acquire);

            if (result != null)
            {
                _memoryCache.Set(key, result);
            }

            return result;
        }

        public Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            return RemoveAsync(cacheKey, CancellationToken.None, cacheKeyParameters);
        }

        public async Task RemoveAsync(CacheKey cacheKey, CancellationToken cancellationToken, params object[] cacheKeyParameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = PrepareKey(cacheKey, cacheKeyParameters);

            await _distributedCache.RemoveAsync(cacheKey, cancellationToken, cacheKeyParameters);
            await _memoryCache.RemoveAsync(cacheKey, cancellationToken, cacheKeyParameters);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveAction, key.Key);
        }

        public async Task SetAsync(CacheKey key, object data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (key is null || key.CacheTime <= 0 || data == null)
                return;

            await _distributedCache.SetAsync(key, data, cancellationToken);
            await _memoryCache.SetAsync(key, data, cancellationToken);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveAction, key.Key);
        }

        public Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            return RemoveByPrefixAsync(prefix, CancellationToken.None, prefixParameters);
        }

        public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken, params object[] prefixParameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var formattedPrefix = PrepareKeyPrefix(prefix, prefixParameters);

            await _distributedCache.RemoveByPrefixAsync(prefix, cancellationToken, prefixParameters);
            await _memoryCache.RemoveByPrefixAsync(prefix, cancellationToken, prefixParameters);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveByPrefixAction, formattedPrefix);
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _distributedCache.ClearAsync(cancellationToken);
            await _memoryCache.ClearAsync(cancellationToken);

            PublishInvalidation(HybridCacheInvalidationListener.ClearAction, string.Empty);
        }

        public bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action)
        {
            return _distributedCache.PerformActionWithLock(resource, expirationTime, action);
        }

        public Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action, CancellationToken cancellationToken = default)
        {
            return _distributedCache.PerformActionWithLockAsync(resource, expirationTime, action, cancellationToken);
        }

        public Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            return _distributedCache.PerformActionWithLockAsync(resource, expirationTime, action, cancellationToken);
        }
    }
}
