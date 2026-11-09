using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Base.Caching.Managers
{
    public class HybridCacheManager : CacheKeyService, ICacheLocker, ICacheManager
    {
        private readonly MemoryCacheManager _memoryCache;
        private readonly DistributedCacheManager _distributedCache;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ILogger<HybridCacheManager> _logger;
        private readonly string _channelName;
        private readonly string _instanceId = Guid.NewGuid().ToString();

        public HybridCacheManager(
            MemoryCacheManager memoryCache,
            DistributedCacheManager distributedCache,
            IConnectionMultiplexer connectionMultiplexer = null,
            BaseDistributedCacheConfiguration configuration = null,
            ILogger<HybridCacheManager> logger = null)
        {
            _memoryCache = memoryCache;
            _distributedCache = distributedCache;
            _connectionMultiplexer = connectionMultiplexer;
            _logger = logger;

            var instanceName = configuration?.InstanceName ?? "Base.Caching";
            _channelName = $"{instanceName}:InvalidationChannel";

            SubscribeToInvalidation();
        }

        private void SubscribeToInvalidation()
        {
            if (_connectionMultiplexer == null || !_connectionMultiplexer.IsConnected)
                return;

            try
            {
                var subscriber = _connectionMultiplexer.GetSubscriber();
                subscriber.Subscribe(RedisChannel.Literal(_channelName), (channel, messageValue) =>
                {
                    try
                      {
                        var msg = JsonSerializer.Deserialize<CacheInvalidationMessage>((string?)messageValue);
                        if (msg == null || msg.OriginatorId == _instanceId)
                            return;

                        _logger?.LogInformation("Received cache invalidation: {Action} for {Target}", msg.Action, msg.Target);

                        if (msg.Action == "Remove")
                        {
                            _memoryCache.RemoveAsync(new CacheKey(msg.Target)).GetAwaiter().GetResult();
                        }
                        else if (msg.Action == "RemoveByPrefix")
                        {
                            _memoryCache.RemoveByPrefixAsync(msg.Target).GetAwaiter().GetResult();
                        }
                        else if (msg.Action == "Clear")
                        {
                            _memoryCache.ClearAsync().GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing cache invalidation message");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error subscribing to cache invalidation channel");
            }
        }

        private void PublishInvalidation(string action, string target)
        {
            if (_connectionMultiplexer == null || !_connectionMultiplexer.IsConnected)
                return;

            try
            {
                var subscriber = _connectionMultiplexer.GetSubscriber();
                var msg = new CacheInvalidationMessage
                {
                    Action = action,
                    Target = target,
                    OriginatorId = _instanceId
                };
                subscriber.Publish(RedisChannel.Literal(_channelName), JsonSerializer.Serialize(msg));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error publishing invalidation message: {Action} for {Target}", action, target);
            }
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
                _memoryCache.SetAsync(key, result).GetAwaiter().GetResult();
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
                _memoryCache.SetAsync(key, result).GetAwaiter().GetResult();
            }

            return result;
        }

        public async Task RemoveAsync(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            var key = PrepareKey(cacheKey, cacheKeyParameters);

            await _distributedCache.RemoveAsync(cacheKey, cacheKeyParameters);
            await _memoryCache.RemoveAsync(cacheKey, cacheKeyParameters);

            PublishInvalidation("Remove", key.Key);
        }

        public async Task SetAsync(CacheKey key, object data)
        {
            if ((key?.CacheTime ?? 0) <= 0 || data == null)
                return;

            await _distributedCache.SetAsync(key, data);
            await _memoryCache.SetAsync(key, data);

            PublishInvalidation("Remove", key.Key);
        }

        public async Task RemoveByPrefixAsync(string prefix, params object[] prefixParameters)
        {
            var formattedPrefix = PrepareKeyPrefix(prefix, prefixParameters);

            await _distributedCache.RemoveByPrefixAsync(prefix, prefixParameters);
            await _memoryCache.RemoveByPrefixAsync(prefix, prefixParameters);

            PublishInvalidation("RemoveByPrefix", formattedPrefix);
        }

        public async Task ClearAsync()
        {
            await _distributedCache.ClearAsync();
            await _memoryCache.ClearAsync();

            PublishInvalidation("Clear", string.Empty);
        }

        public bool PerformActionWithLock(string resource, TimeSpan expirationTime, Action action)
        {
            return _distributedCache.PerformActionWithLock(resource, expirationTime, action);
        }

        public async Task<bool> PerformActionWithLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            return await _distributedCache.PerformActionWithLockAsync(resource, expirationTime, action);
        }

        private class CacheInvalidationMessage
        {
            public string Action { get; set; }
            public string Target { get; set; }
            public string OriginatorId { get; set; }
        }
    }
}
