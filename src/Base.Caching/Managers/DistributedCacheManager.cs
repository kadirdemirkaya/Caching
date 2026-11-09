using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
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
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly BaseDistributedCacheConfiguration _distributedCacheConfiguration;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<DistributedCacheManager> _logger;

        private readonly CacheInvalidationState _fallbackState;
        private readonly ConcurrentDictionary<string, DateTime> _fallbackLocks;

        public DistributedCacheManager(
            IDistributedCache distributedCache, 
            IHttpContextAccessor httpContextAccessor,
            IConnectionMultiplexer connectionMultiplexer = null,
            BaseDistributedCacheConfiguration distributedCacheConfiguration = null,
            IMemoryCache memoryCache = null,
            ILogger<DistributedCacheManager> logger = null)
        {
            _distributedCache = distributedCache;
            _perRequestCache = new PerRequestCache(httpContextAccessor);
            _connectionMultiplexer = connectionMultiplexer;
            _distributedCacheConfiguration = distributedCacheConfiguration;
            _memoryCache = memoryCache;
            _logger = logger;
            _fallbackState = CacheInvalidationState.ForDistributedFallback(memoryCache);
            _fallbackLocks = _fallbackState.Locks;
        }

        private DistributedCacheEntryOptions PrepareEntryOptions(CacheKey key)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(key.CacheTime)
            };

            return options;
        }

        private MemoryCacheEntryOptions PrepareMemoryEntryOptions(CacheKey key)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(key.CacheTime)
            };

            return _fallbackState.AddExpirationTokens(options, key.Prefixes.ToList());
        }

        private bool IsRedisConnected => _connectionMultiplexer != null && _connectionMultiplexer.IsConnected;

        private async Task ExecuteWithFallbackAsync(Func<Task> action, Func<Task> fallbackAction, string operationName)
        {
            try
            {
                if (IsRedisConnected)
                {
                    await action();
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Redis connection failed during {Operation}. Falling back to memory cache.", operationName);
            }

            if (fallbackAction != null)
            {
                await fallbackAction();
            }
        }

        private async Task<T> ExecuteWithFallbackAsync<T>(Func<Task<T>> action, Func<Task<T>> fallbackAction, string operationName)
        {
            try
            {
                if (IsRedisConnected)
                {
                    return await action();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Redis connection failed during {Operation}. Falling back to memory cache.", operationName);
            }

            if (fallbackAction != null)
            {
                return await fallbackAction();
            }

            return default;
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

            if (fallbackAction != null)
            {
                return fallbackAction();
            }

            return default;
        }

        private void SetFallback(CacheKey key, object data)
        {
            if (_memoryCache != null && key != null && data != null && key.CacheTime > 0)
            {
                _memoryCache.Set(key.Key, data, PrepareMemoryEntryOptions(key));
            }
        }

        private T GetFallback<T>(CacheKey key)
        {
            if (_memoryCache != null && _memoryCache.TryGetValue(key.Key, out T val))
            {
                return val;
            }
            return default;
        }

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

        private async Task<(bool isSet, T item)> TryGetItemAsync<T>(CacheKey key)
        {
            return await ExecuteWithFallbackAsync(
                async () =>
                {
                    var json = await _distributedCache.GetStringAsync(key.Key);
                    if (string.IsNullOrEmpty(json))
                        return (false, default(T));

                    var item = JsonSerializer.Deserialize<T>(json);
                    _perRequestCache.Set(key.Key, item);
                    return (true, item);
                },
                async () =>
                {
                    if (_memoryCache != null && _memoryCache.TryGetValue(key.Key, out T val))
                    {
                        _perRequestCache.Set(key.Key, val);
                        return (true, val);
                    }
                    return (false, default(T));
                },
                "TryGetItemAsync"
            );
        }

        private (bool isSet, T item) TryGetItem<T>(CacheKey key)
        {
            return ExecuteWithFallback(
                () =>
                {
                    var json = _distributedCache.GetString(key.Key);
                    if (string.IsNullOrEmpty(json))
                        return (false, default(T));

                    var item = JsonSerializer.Deserialize<T>(json);
                    _perRequestCache.Set(key.Key, item);
                    return (true, item);
                },
                () =>
                {
                    if (_memoryCache != null && _memoryCache.TryGetValue(key.Key, out T val))
                    {
                        _perRequestCache.Set(key.Key, val);
                        return (true, val);
                    }
                    return (false, default(T));
                },
                "TryGetItem"
            );
        }

        private void Set(CacheKey key, object data)
        {
            if ((key?.CacheTime ?? 0) <= 0 || data == null)
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

        public async Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire)
        {
            if (_perRequestCache.IsSet(key.Key))
                return _perRequestCache.Get(key.Key, () => default(T));

            if (key.CacheTime <= 0)
                return await acquire();

            var (isSet, item) = await TryGetItemAsync<T>(key);

            if (isSet)
                return item;

            var result = await acquire();

            if (result != null)
                await SetAsync(key, result);

            return result;
        }

        public T Get<T>(CacheKey key)
        {
            return ExecuteWithFallback(
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

        public async Task SetAsync(CacheKey key, object data)
        {
            if ((key?.CacheTime ?? 0) <= 0 || data == null)
                return;

            await ExecuteWithFallbackAsync(
                async () =>
                {
                    await _distributedCache.SetStringAsync(key.Key, JsonSerializer.Serialize(data), PrepareEntryOptions(key));
                },
                async () =>
                {
                    SetFallback(key, data);
                },
                "SetAsync"
            );

            _perRequestCache.Set(key.Key, data);
        }

        public async Task<T> GetAsync<T>(CacheKey key, Func<T> acquire)
        {
            if (_perRequestCache.IsSet(key.Key))
                return _perRequestCache.Get(key.Key, () => default(T));

            if (key.CacheTime <= 0)
                return acquire();

            var (isSet, item) = await TryGetItemAsync<T>(key);

            if (isSet)
                return item;

            var result = acquire();

            if (result != null)
                await SetAsync(key, result);

            return result;
        }

        public T Get<T>(CacheKey key, Func<T> acquire)
        {
            if (_perRequestCache.IsSet(key.Key))
                return _perRequestCache.Get(key.Key, () => default(T));

            if (key.CacheTime <= 0)
                return acquire();

            var (isSet, item) = TryGetItem<T>(key);

            if (isSet)
                return item;

            var result = acquire();

            if (result != null)
                Set(key, result);

            return result;
        }

        public async Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            cacheKey = PrepareKey(cacheKey, cacheKeyParameters);

            await ExecuteWithFallbackAsync(
                async () =>
                {
                    await _distributedCache.RemoveAsync(cacheKey.Key);
                },
                async () =>
                {
                    RemoveFallback(cacheKey.Key);
                },
                "RemoveAsync"
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

        private async Task DeleteKeysByPrefixAsync(string prefix)
        {
            var instanceName = _distributedCacheConfiguration?.InstanceName ?? string.Empty;
            var pattern = $"{EscapeKeyPattern(instanceName)}{EscapeKeyPattern(prefix)}*";
            var database = _connectionMultiplexer.GetDatabase();
            var batch = new List<RedisKey>(KeyDeleteBatchSize);

            foreach (var endpoint in _connectionMultiplexer.GetEndPoints())
            {
                var server = _connectionMultiplexer.GetServer(endpoint);
                if (server.IsReplica)
                    continue;

                await foreach (var key in server.KeysAsync(database.Database, pattern, KeyDeleteBatchSize))
                {
                    batch.Add(key);
                    if (batch.Count < KeyDeleteBatchSize)
                        continue;

                    await database.KeyDeleteAsync(batch.ToArray());
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                await database.KeyDeleteAsync(batch.ToArray());
        }

        public async Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            prefix = PrepareKeyPrefix(prefix, prefixParameters);
            _perRequestCache.RemoveByPrefix(prefix);

            await ExecuteWithFallbackAsync(
                async () =>
                {
                    if (CanDeleteByPattern(prefix, "RemoveByPrefixAsync"))
                        await DeleteKeysByPrefixAsync(prefix);
                },
                async () =>
                {
                    _fallbackState.RemoveByPrefix(prefix);
                },
                "RemoveByPrefixAsync"
            );
        }

        public async Task ClearAsync()
        {
            await ExecuteWithFallbackAsync(
                async () =>
                {
                    if (CanDeleteByPattern(string.Empty, "ClearAsync"))
                        await DeleteKeysByPrefixAsync(string.Empty);
                },
                async () =>
                {
                    _fallbackState.Clear();
                },
                "ClearAsync"
            );

            _perRequestCache.Clear();
        }

        public bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action)
        {
            var lockAcquired = false;
            var now = DateTime.UtcNow;
            var lockExpiry = now.Add(expirationTime);

            ExecuteWithFallback(
                () =>
                {
                    var db = _connectionMultiplexer.GetDatabase();
                    lockAcquired = db.StringSet(resource, resource, expirationTime, When.NotExists);
                },
                () =>
                {
                    lockAcquired = AcquireLocalLock(resource, lockExpiry);
                },
                "PerformActionWithLock"
            );

            if (!lockAcquired)
                return false;

            try
            {
                action();
                return true;
            }
            finally
            {
                ExecuteWithFallback(
                    () =>
                    {
                        var db = _connectionMultiplexer.GetDatabase();
                        db.KeyDelete(resource);
                    },
                    () =>
                    {
                        ReleaseLocalLock(resource, lockExpiry);
                    },
                    "ReleaseLock"
                );
            }
        }

        public async Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            var lockAcquired = false;
            var now = DateTime.UtcNow;
            var lockExpiry = now.Add(expirationTime);

            await ExecuteWithFallbackAsync(
                async () =>
                {
                    var db = _connectionMultiplexer.GetDatabase();
                    lockAcquired = await db.StringSetAsync(resource, resource, expirationTime, When.NotExists);
                },
                async () =>
                {
                    lockAcquired = AcquireLocalLock(resource, lockExpiry);
                },
                "PerformActionWithLockAsync"
            );

            if (!lockAcquired)
                return false;

            try
            {
                await action();
                return true;
            }
            finally
            {
                await ExecuteWithFallbackAsync(
                    async () =>
                    {
                        var db = _connectionMultiplexer.GetDatabase();
                        await db.KeyDeleteAsync(resource);
                    },
                    async () =>
                    {
                        ReleaseLocalLock(resource, lockExpiry);
                    },
                    "ReleaseLockAsync"
                );
            }
        }
    }
}
