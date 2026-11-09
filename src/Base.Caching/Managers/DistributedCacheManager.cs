using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Base.Caching.Lock;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Base.Caching.Managers
{
    public class DistributedCacheManager : CacheKeyService, ICacheLocker, ICacheManager
    {
        private readonly IDistributedCache _distributedCache;
        private readonly PerRequestCache _perRequestCache;
        private readonly IConnectionMultiplexer? _connectionMultiplexer;
        private readonly BaseDistributedCacheConfiguration? _distributedCacheConfiguration;
        private readonly IMemoryCache? _memoryCache;
        private readonly ILogger<DistributedCacheManager>? _logger;

        private readonly CacheInvalidationState _fallbackState;
        private readonly ConcurrentDictionary<string, DateTime> _fallbackLocks;
        private readonly bool _preventConcurrentAcquire;

        /// <summary>
        /// Creates the distributed cache manager.
        /// </summary>
        /// <param name="distributedCache">Distributed store.</param>
        /// <param name="httpContextAccessor">Gives access to the per-request cache.</param>
        /// <param name="connectionMultiplexer">Redis connection used for locks, prefix removal and clearing; without it the
        /// manager uses the in-memory fallback.</param>
        /// <param name="distributedCacheConfiguration">Distributed cache settings.</param>
        /// <param name="memoryCache">Memory cache of the in-memory fallback.</param>
        /// <param name="logger">Logger for connection failures.</param>
        /// <param name="cacheConfiguration">Cache settings; <see cref="BaseCacheConfiguration.PreventConcurrentAcquire"/> is
        /// read once here. <c>null</c> keeps every setting at its default.</param>
        public DistributedCacheManager(
            IDistributedCache distributedCache, 
            IHttpContextAccessor httpContextAccessor,
            IConnectionMultiplexer? connectionMultiplexer = null,
            BaseDistributedCacheConfiguration? distributedCacheConfiguration = null,
            IMemoryCache? memoryCache = null,
            ILogger<DistributedCacheManager>? logger = null,
            BaseCacheConfiguration? cacheConfiguration = null)
        {
            _distributedCache = distributedCache;
            _perRequestCache = new PerRequestCache(httpContextAccessor);
            _connectionMultiplexer = connectionMultiplexer;
            _distributedCacheConfiguration = distributedCacheConfiguration;
            _memoryCache = memoryCache;
            _logger = logger;
            _fallbackState = CacheInvalidationState.ForDistributedFallback(memoryCache);
            _fallbackLocks = _fallbackState.Locks;
            _preventConcurrentAcquire = cacheConfiguration?.PreventConcurrentAcquire ?? false;
        }

        internal KeyedAsyncLock AcquireLocks => _fallbackState.AcquireLocks;

        private DistributedCacheEntryOptions PrepareEntryOptions(CacheKey key)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = key.CacheDuration,
                SlidingExpiration = key.SlidingExpiration
            };

            return options;
        }

        private MemoryCacheEntryOptions PrepareMemoryEntryOptions(CacheKey key)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = key.CacheDuration,
                SlidingExpiration = key.SlidingExpiration
            };

            return _fallbackState.AddExpirationTokens(options, key.Prefixes.ToList());
        }

        private bool IsRedisConnected => _connectionMultiplexer != null && _connectionMultiplexer.IsConnected;

        private IConnectionMultiplexer Connection => _connectionMultiplexer ?? throw new InvalidOperationException("No Redis connection is configured.");

        private static bool IsCancellation(Exception exception, CancellationToken cancellationToken)
            => exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

        private async Task ExecuteWithFallbackAsync(Func<Task> action, Func<Task> fallbackAction, string operationName, CancellationToken cancellationToken = default)
        {
            try
            {
                if (IsRedisConnected)
                {
                    await action();
                    return;
                }
            }
            catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
            {
                _logger?.LogWarning(ex, "Redis connection failed during {Operation}. Falling back to memory cache.", operationName);
            }

            if (fallbackAction != null)
            {
                await fallbackAction();
            }
        }

        private async Task<T> ExecuteWithFallbackAsync<T>(Func<Task<T>> action, Func<Task<T>> fallbackAction, string operationName, CancellationToken cancellationToken = default)
        {
            try
            {
                if (IsRedisConnected)
                {
                    return await action();
                }
            }
            catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
            {
                _logger?.LogWarning(ex, "Redis connection failed during {Operation}. Falling back to memory cache.", operationName);
            }

            return await fallbackAction();
        }

        private void ExecuteWithFallback(Action action, Action fallbackAction, string operationName)
        {
            try
            {
                if (IsRedisConnected)
                {
                    action();
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Redis connection failed during {Operation}. Falling back to memory cache.", operationName);
            }

            fallbackAction?.Invoke();
        }

        private T ExecuteWithFallback<T>(Func<T> action, Func<T> fallbackAction, string operationName)
        {
            try
            {
                if (IsRedisConnected)
                {
                    return action();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Redis connection failed during {Operation}. Falling back to memory cache.", operationName);
            }

            return fallbackAction();
        }

        private void SetFallback(CacheKey key, object data)
        {
            if (_memoryCache != null && key != null && data != null && key.CacheTime > 0)
            {
                _memoryCache.Set(key.Key, data, PrepareMemoryEntryOptions(key));
            }
        }

        private T? GetFallback<T>(CacheKey key)
        {
            return TryGetFallback(key, out T val) ? val : default;
        }

        private bool TryGetFallback<T>(CacheKey key, out T value)
        {
            if (_memoryCache != null && _memoryCache.TryGetValue(key.Key, out T? cached))
            {
                value = cached!;
                return true;
            }

            value = default!;
            return false;
        }

        private static (bool isSet, T item) Miss<T>() => (false, default!);

        private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

        private void RemoveFallback(string key)
        {
            _memoryCache?.Remove(key);
        }

        private bool AcquireLocalLock(string resource, DateTime lockExpiry)
        {
            var now = DateTime.UtcNow;
            while (true)
            {
                if (_fallbackLocks.TryAdd(resource, lockExpiry))
                {
                    return true;
                }

                if (_fallbackLocks.TryGetValue(resource, out var existingExpiry))
                {
                    if (existingExpiry < now)
                    {
                        _fallbackLocks.TryRemove(KeyValuePair.Create(resource, existingExpiry));
                        continue;
                    }
                }

                return false;
            }
        }

        private void ReleaseLocalLock(string resource, DateTime lockExpiry)
        {
            _fallbackLocks.TryRemove(KeyValuePair.Create(resource, lockExpiry));
        }

        private async Task<(bool isSet, T item)> TryGetItemAsync<T>(CacheKey key, CancellationToken cancellationToken)
        {
            return await ExecuteWithFallbackAsync(
                async () =>
                {
                    var json = await _distributedCache.GetStringAsync(key.Key, cancellationToken);
                    if (string.IsNullOrEmpty(json))
                        return Miss<T>();

                    var item = Deserialize<T>(json);
                    _perRequestCache.Set(key.Key, item);
                    return (true, item);
                },
                async () =>
                {
                    if (TryGetFallback(key, out T val))
                    {
                        _perRequestCache.Set(key.Key, val);
                        return (true, val);
                    }
                    return Miss<T>();
                },
                "TryGetItemAsync",
                cancellationToken
            );
        }

        private (bool isSet, T item) TryGetItem<T>(CacheKey key)
        {
            return ExecuteWithFallback(
                () =>
                {
                    var json = _distributedCache.GetString(key.Key);
                    if (string.IsNullOrEmpty(json))
                        return Miss<T>();

                    var item = Deserialize<T>(json);
                    _perRequestCache.Set(key.Key, item);
                    return (true, item);
                },
                () =>
                {
                    if (TryGetFallback(key, out T val))
                    {
                        _perRequestCache.Set(key.Key, val);
                        return (true, val);
                    }
                    return Miss<T>();
                },
                "TryGetItem"
            );
        }

        internal bool TryGet<T>(CacheKey key, out T item)
        {
            bool isSet;
            (isSet, item) = TryGetItem<T>(key);
            return isSet;
        }

        private void Set(CacheKey key, object data)
        {
            if (key is null || key.CacheTime <= 0 || data == null)
                return;

            ExecuteWithFallback(
                () =>
                {
                    _distributedCache.SetString(key.Key, JsonSerializer.Serialize(data), PrepareEntryOptions(key));
                },
                () =>
                {
                    SetFallback(key, data);
                },
                "Set"
            );

            _perRequestCache.Set(key.Key, data);
        }

        public void Dispose()
        {
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

            return await GetOrAcquireAsync(key, acquire, _preventConcurrentAcquire, cancellationToken);
        }

        internal async Task<T> GetOrAcquireAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> acquire, bool preventConcurrentAcquire, CancellationToken cancellationToken)
        {
            if (_perRequestCache.TryGet(key.Key, out T cached))
                return cached;

            var (isSet, item) = await TryGetItemAsync<T>(key, cancellationToken);

            if (isSet)
                return item;

            if (!preventConcurrentAcquire)
                return await AcquireAndSetAsync(key, acquire, cancellationToken);

            using (await _fallbackState.AcquireLocks.LockAsync(key.Key, cancellationToken))
            {
                (isSet, item) = await TryGetItemAsync<T>(key, cancellationToken);

                if (isSet)
                    return item;

                return await AcquireAndSetAsync(key, acquire, cancellationToken);
            }
        }

        private async Task<T> AcquireAndSetAsync<T>(CacheKey key, Func<CancellationToken, Task<T>> acquire, CancellationToken cancellationToken)
        {
            var result = await acquire(cancellationToken);

            if (result != null)
                await SetAsync(key, result, cancellationToken);

            return result;
        }

        public T? Get<T>(CacheKey key)
        {
            if (key is null || key.CacheTime <= 0)
                return default;

            return ExecuteWithFallback<T?>(
                () =>
                {
                    var json = _distributedCache.GetString(key.Key);
                    if (string.IsNullOrEmpty(json))
                        return default;

                    var item = JsonSerializer.Deserialize<T>(json);
                    _perRequestCache.Set(key.Key, item);
                    return item;
                },
                () =>
                {
                    return GetFallback<T>(key);
                },
                "Get"
            );
        }

        public async Task SetAsync(CacheKey key, object data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (key is null || key.CacheTime <= 0 || data == null)
                return;

            await ExecuteWithFallbackAsync(
                async () =>
                {
                    await _distributedCache.SetStringAsync(key.Key, JsonSerializer.Serialize(data), PrepareEntryOptions(key), cancellationToken);
                },
                async () =>
                {
                    SetFallback(key, data);
                },
                "SetAsync",
                cancellationToken
            );

            _perRequestCache.Set(key.Key, data);
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<T> acquire, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (key.CacheTime <= 0)
                return acquire();

            return await GetOrAcquireAsync(key, _ => Task.FromResult(acquire()), _preventConcurrentAcquire, cancellationToken);
        }

        public T Get<T>(CacheKey key, Func<T> acquire)
        {
            if (key.CacheTime <= 0)
                return acquire();

            if (_perRequestCache.TryGet(key.Key, out T cached))
                return cached;

            var (isSet, item) = TryGetItem<T>(key);

            if (isSet)
                return item;

            var result = acquire();

            if (result != null)
                Set(key, result);

            return result;
        }

        public Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            return RemoveAsync(cacheKey, CancellationToken.None, cacheKeyParameters);
        }

        public async Task RemoveAsync(CacheKey cacheKey, CancellationToken cancellationToken, params object[] cacheKeyParameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            cacheKey = PrepareKey(cacheKey, cacheKeyParameters);

            await ExecuteWithFallbackAsync(
                async () =>
                {
                    await _distributedCache.RemoveAsync(cacheKey.Key, cancellationToken);
                },
                async () =>
                {
                    RemoveFallback(cacheKey.Key);
                },
                "RemoveAsync",
                cancellationToken
            );

            _perRequestCache.Remove(cacheKey.Key);
        }

        internal const int KeyDeleteBatchSize = 1000;

        internal static string EscapeKeyPattern(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (character is '*' or '?' or '[' or ']' or '\\')
                    builder.Append('\\');

                builder.Append(character);
            }

            return builder.ToString();
        }

        private bool CanDeleteByPattern(string prefix, string operationName)
        {
            if (!string.IsNullOrEmpty(_distributedCacheConfiguration?.InstanceName)
                || !string.IsNullOrEmpty(prefix)
                || (_distributedCacheConfiguration?.AllowClearWithoutInstanceName ?? false))
                return true;

            _logger?.LogWarning(
                "{Operation} skipped in Redis: InstanceName is empty, so the operation would delete every key of the database. Set an InstanceName or enable AllowClearWithoutInstanceName.",
                operationName);
            return false;
        }

        private async Task DeleteKeysByPrefixAsync(string prefix, CancellationToken cancellationToken)
        {
            var instanceName = _distributedCacheConfiguration?.InstanceName ?? string.Empty;
            var pattern = $"{EscapeKeyPattern(instanceName)}{EscapeKeyPattern(prefix)}*";
            var connection = Connection;
            var database = connection.GetDatabase();
            var batch = new List<RedisKey>(KeyDeleteBatchSize);
            var lockKeyPrefix = LockKeyPrefix;

            foreach (var endpoint in connection.GetEndPoints())
            {
                var server = connection.GetServer(endpoint);
                if (server.IsReplica)
                    continue;

                await foreach (var key in server.KeysAsync(database.Database, pattern, KeyDeleteBatchSize).WithCancellation(cancellationToken))
                {
                    if (IsLockKey(key, lockKeyPrefix))
                        continue;

                    batch.Add(key);
                    if (batch.Count < KeyDeleteBatchSize)
                        continue;

                    cancellationToken.ThrowIfCancellationRequested();
                    await database.KeyDeleteAsync(batch.ToArray());
                    batch.Clear();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (batch.Count > 0)
                await database.KeyDeleteAsync(batch.ToArray());
        }

        public Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            return RemoveByPrefixAsync(prefix, CancellationToken.None, prefixParameters);
        }

        public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken, params object[] prefixParameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            prefix = PrepareKeyPrefix(prefix, prefixParameters);
            _perRequestCache.RemoveByPrefix(prefix);

            await ExecuteWithFallbackAsync(
                async () =>
                {
                    if (CanDeleteByPattern(prefix, "RemoveByPrefixAsync"))
                        await DeleteKeysByPrefixAsync(prefix, cancellationToken);
                },
                async () =>
                {
                    _fallbackState.RemoveByPrefix(prefix);
                },
                "RemoveByPrefixAsync",
                cancellationToken
            );
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ExecuteWithFallbackAsync(
                async () =>
                {
                    if (CanDeleteByPattern(string.Empty, "ClearAsync"))
                        await DeleteKeysByPrefixAsync(string.Empty, cancellationToken);
                },
                async () =>
                {
                    _fallbackState.Clear();
                },
                "ClearAsync",
                cancellationToken
            );

            _perRequestCache.Clear();
        }

        private const string LockKeySegment = "lock:";

        private enum LockPath
        {
            None,
            Redis,
            Local
        }

        private readonly record struct LockLease(LockPath Path, string Token, DateTime LocalExpiry);

        private string LockKeyPrefix => $"{_distributedCacheConfiguration?.InstanceName}{LockKeySegment}";

        private RedisKey GetLockKey(string resource) => $"{LockKeyPrefix}{resource}";

        private static bool IsLockKey(RedisKey key, string lockKeyPrefix) => ((string?)key)?.StartsWith(lockKeyPrefix, StringComparison.Ordinal) ?? false;

        private static string NewLockToken() => Guid.NewGuid().ToString("N");

        private LockLease AcquireLock(string resource, TimeSpan expirationTime, string operationName)
        {
            var localExpiry = DateTime.UtcNow.Add(expirationTime);

            return ExecuteWithFallback(
                () =>
                {
                    var token = NewLockToken();
                    var acquired = Connection.GetDatabase().LockTake(GetLockKey(resource), token, expirationTime);
                    return new LockLease(acquired ? LockPath.Redis : LockPath.None, token, localExpiry);
                },
                () => new LockLease(AcquireLocalLock(resource, localExpiry) ? LockPath.Local : LockPath.None, string.Empty, localExpiry),
                operationName
            );
        }

        private async Task<LockLease> AcquireLockAsync(string resource, TimeSpan expirationTime, string operationName, CancellationToken cancellationToken)
        {
            var localExpiry = DateTime.UtcNow.Add(expirationTime);

            return await ExecuteWithFallbackAsync(
                async () =>
                {
                    var token = NewLockToken();
                    var acquired = await Connection.GetDatabase().LockTakeAsync(GetLockKey(resource), token, expirationTime);
                    return new LockLease(acquired ? LockPath.Redis : LockPath.None, token, localExpiry);
                },
                () => Task.FromResult(new LockLease(AcquireLocalLock(resource, localExpiry) ? LockPath.Local : LockPath.None, string.Empty, localExpiry)),
                operationName,
                cancellationToken
            );
        }

        private void ReleaseLock(string resource, LockLease lease)
        {
            if (lease.Path == LockPath.Local)
            {
                ReleaseLocalLock(resource, lease.LocalExpiry);
                return;
            }

            try
            {
                if (!Connection.GetDatabase().LockRelease(GetLockKey(resource), lease.Token))
                    LogLockNotOwned(resource);
            }
            catch (Exception ex)
            {
                LogLockReleaseFailed(ex, resource);
            }
        }

        private async Task ReleaseLockAsync(string resource, LockLease lease)
        {
            if (lease.Path == LockPath.Local)
            {
                ReleaseLocalLock(resource, lease.LocalExpiry);
                return;
            }

            try
            {
                if (!await Connection.GetDatabase().LockReleaseAsync(GetLockKey(resource), lease.Token))
                    LogLockNotOwned(resource);
            }
            catch (Exception ex)
            {
                LogLockReleaseFailed(ex, resource);
            }
        }

        private void LogLockNotOwned(string resource)
        {
            _logger?.LogWarning("Lock {Resource} was no longer held when released: it expired before the action finished.", resource);
        }

        private void LogLockReleaseFailed(Exception ex, string resource)
        {
            _logger?.LogWarning(ex, "Releasing lock {Resource} in Redis failed; it is removed when its expiration time elapses.", resource);
        }

        public bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action)
        {
            var lease = AcquireLock(resource, expirationTime, "PerformActionWithLock");

            if (lease.Path == LockPath.None)
                return false;

            try
            {
                action();
                return true;
            }
            finally
            {
                ReleaseLock(resource, lease);
            }
        }

        public Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action, CancellationToken cancellationToken = default)
        {
            return PerformActionWithLockAsync(resource, expirationTime, _ => action(), cancellationToken);
        }

        public async Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lease = await AcquireLockAsync(resource, expirationTime, "PerformActionWithLockAsync", cancellationToken);

            if (lease.Path == LockPath.None)
                return false;

            try
            {
                await action(cancellationToken);
                return true;
            }
            finally
            {
                await ReleaseLockAsync(resource, lease);
            }
        }
    }
}
