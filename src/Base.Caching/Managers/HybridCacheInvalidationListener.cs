using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Base.Caching.Managers
{
    internal sealed class HybridCacheInvalidationListener
    {
        internal const string RemoveAction = "Remove";
        internal const string RemoveByPrefixAction = "RemoveByPrefix";
        internal const string ClearAction = "Clear";

        private static readonly ConditionalWeakTable<IConnectionMultiplexer, ConditionalWeakTable<MemoryCacheManager, ConcurrentDictionary<string, HybridCacheInvalidationListener>>> _listeners = new();

        private readonly MemoryCacheManager _memoryCache;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ILogger? _logger;
        private readonly object _subscribeLock = new();
        private bool _subscribed;

        private HybridCacheInvalidationListener(MemoryCacheManager memoryCache, IConnectionMultiplexer connectionMultiplexer, string channelName, ILogger? logger)
        {
            _memoryCache = memoryCache;
            _connectionMultiplexer = connectionMultiplexer;
            ChannelName = channelName;
            _logger = logger;
        }

        public string InstanceId { get; } = Guid.NewGuid().ToString();

        public string ChannelName { get; }

        public static HybridCacheInvalidationListener GetOrCreate(MemoryCacheManager memoryCache, IConnectionMultiplexer connectionMultiplexer, string channelName, ILogger? logger)
        {
            var byMemoryCache = _listeners.GetValue(connectionMultiplexer, static _ => new ConditionalWeakTable<MemoryCacheManager, ConcurrentDictionary<string, HybridCacheInvalidationListener>>());
            var byChannel = byMemoryCache.GetValue(memoryCache, static _ => new ConcurrentDictionary<string, HybridCacheInvalidationListener>());

            var listener = byChannel.GetOrAdd(channelName, name => new HybridCacheInvalidationListener(memoryCache, connectionMultiplexer, name, logger));
            listener.EnsureSubscribed();

            return listener;
        }

        public void EnsureSubscribed()
        {
            if (Volatile.Read(ref _subscribed))
                return;

            lock (_subscribeLock)
            {
                if (_subscribed || !_connectionMultiplexer.IsConnected)
                    return;

                try
                {
                    _connectionMultiplexer.GetSubscriber().Subscribe(RedisChannel.Literal(ChannelName), (_, messageValue) => Handle((string?)messageValue));
                    Volatile.Write(ref _subscribed, true);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error subscribing to cache invalidation channel");
                }
            }
        }

        public void Publish(string action, string target)
        {
            if (!_connectionMultiplexer.IsConnected)
                return;

            try
            {
                var msg = new CacheInvalidationMessage
                {
                    Action = action,
                    Target = target,
                    OriginatorId = InstanceId
                };
                _connectionMultiplexer.GetSubscriber().Publish(RedisChannel.Literal(ChannelName), JsonSerializer.Serialize(msg));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error publishing invalidation message: {Action} for {Target}", action, target);
            }
        }

        private void Handle(string? payload)
        {
            try
            {
                if (payload == null)
                    return;

                var msg = JsonSerializer.Deserialize<CacheInvalidationMessage>(payload);
                if (msg == null || msg.OriginatorId == InstanceId)
                    return;

                _logger?.LogInformation("Received cache invalidation: {Action} for {Target}", msg.Action, msg.Target);

                switch (msg.Action)
                {
                    case RemoveAction:
                        _memoryCache.RemovePreparedKey(msg.Target);
                        break;
                    case RemoveByPrefixAction:
                        _memoryCache.RemovePreparedPrefix(msg.Target);
                        break;
                    case ClearAction:
                        _memoryCache.ClearAll();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing cache invalidation message");
            }
        }

        private sealed class CacheInvalidationMessage
        {
            public string Action { get; set; } = string.Empty;
            public string Target { get; set; } = string.Empty;
            public string OriginatorId { get; set; } = string.Empty;
        }
    }
}
