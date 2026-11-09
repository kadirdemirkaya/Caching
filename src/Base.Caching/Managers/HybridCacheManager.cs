using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Base.Caching.Managers
{
    /// <summary>
    /// Two-level cache manager: a local memory level in front of the distributed level, kept consistent across
    /// instances with invalidation messages. Locks are taken on the distributed level.
    /// </summary>
    public class HybridCacheManager : CacheKeyService, ICacheLocker, ICacheManager
    {
        private readonly MemoryCacheManager _memoryCache;
        private readonly DistributedCacheManager _distributedCache;
        private readonly HybridCacheInvalidationListener? _invalidationListener;

        /// <summary>
        /// Creates the hybrid cache manager.
        /// </summary>
        /// <param name="memoryCache">Local memory level.</param>
        /// <param name="distributedCache">Distributed level.</param>
        /// <param name="connectionMultiplexer">Redis connection used to publish and receive invalidation messages; without
        /// it other instances are not notified.</param>
        /// <param name="configuration">Distributed cache settings; <see cref="BaseDistributedCacheConfiguration.InstanceName"/>
        /// names the invalidation channel (<c>Base.Caching</c> when not set).</param>
        /// <param name="logger">Logger for invalidation failures.</param>
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

        /// <inheritdoc />
        public Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire, CancellationToken cancellationToken = default)
        {
            return GetAsync(key, _ => acquire(), cancellationToken);
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
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

        /// <inheritdoc />
        public Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            return RemoveAsync(cacheKey, CancellationToken.None, cacheKeyParameters);
        }

        /// <inheritdoc />
        public async Task RemoveAsync(CacheKey cacheKey, CancellationToken cancellationToken, params object[] cacheKeyParameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = PrepareKey(cacheKey, cacheKeyParameters);

            await _distributedCache.RemoveAsync(cacheKey, cancellationToken, cacheKeyParameters);
            await _memoryCache.RemoveAsync(cacheKey, cancellationToken, cacheKeyParameters);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveAction, key.Key);
        }

        /// <inheritdoc />
        public async Task SetAsync(CacheKey key, object data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (key is null || key.CacheTime <= 0 || data == null)
                return;

            await _distributedCache.SetAsync(key, data, cancellationToken);
            await _memoryCache.SetAsync(key, data, cancellationToken);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveAction, key.Key);
        }

        /// <inheritdoc />
        public Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            return RemoveByPrefixAsync(prefix, CancellationToken.None, prefixParameters);
        }

        /// <inheritdoc />
        public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken, params object[] prefixParameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var formattedPrefix = PrepareKeyPrefix(prefix, prefixParameters);

            await _distributedCache.RemoveByPrefixAsync(prefix, cancellationToken, prefixParameters);
            await _memoryCache.RemoveByPrefixAsync(prefix, cancellationToken, prefixParameters);

            PublishInvalidation(HybridCacheInvalidationListener.RemoveByPrefixAction, formattedPrefix);
        }

        /// <inheritdoc />
        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _distributedCache.ClearAsync(cancellationToken);
            await _memoryCache.ClearAsync(cancellationToken);

            PublishInvalidation(HybridCacheInvalidationListener.ClearAction, string.Empty);
        }

        /// <inheritdoc />
        public bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action)
        {
            return _distributedCache.PerformActionWithLock(resource, expirationTime, action);
        }

        /// <inheritdoc />
        public Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action, CancellationToken cancellationToken = default)
        {
            return _distributedCache.PerformActionWithLockAsync(resource, expirationTime, action, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            return _distributedCache.PerformActionWithLockAsync(resource, expirationTime, action, cancellationToken);
        }
    }
}
