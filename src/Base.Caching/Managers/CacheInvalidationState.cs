using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Base.Caching.Managers
{
    internal sealed class CacheInvalidationState
    {
        private static readonly ConditionalWeakTable<IMemoryCache, CacheInvalidationState> _memoryManagerStates = new();
        private static readonly ConditionalWeakTable<IMemoryCache, CacheInvalidationState> _distributedFallbackStates = new();
        private static readonly CacheInvalidationState _unboundMemoryManagerState = new();
        private static readonly CacheInvalidationState _unboundDistributedFallbackState = new();

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _prefixes = new();
        private CancellationTokenSource _clearTokenSource = new();

        public ConcurrentDictionary<string, DateTime> Locks { get; } = new();

        public static CacheInvalidationState ForMemoryManager(IMemoryCache? memoryCache)
        {
            return memoryCache == null
                ? _unboundMemoryManagerState
                : _memoryManagerStates.GetValue(memoryCache, static _ => new CacheInvalidationState());
        }

        public static CacheInvalidationState ForDistributedFallback(IMemoryCache? memoryCache)
        {
            return memoryCache == null
                ? _unboundDistributedFallbackState
                : _distributedFallbackStates.GetValue(memoryCache, static _ => new CacheInvalidationState());
        }

        public MemoryCacheEntryOptions AddExpirationTokens(MemoryCacheEntryOptions options, IEnumerable<string> prefixes)
        {
            options.AddExpirationToken(new CancellationChangeToken(Volatile.Read(ref _clearTokenSource).Token));

            foreach (var prefix in prefixes)
            {
                var tokenSource = _prefixes.GetOrAdd(prefix, static _ => new CancellationTokenSource());
                options.AddExpirationToken(new CancellationChangeToken(tokenSource.Token));
            }

            return options;
        }

        public void RemoveByPrefix(string prefix)
        {
            if (_prefixes.TryRemove(prefix, out var tokenSource))
                tokenSource.Cancel();
        }

        public void Clear()
        {
            var previous = Interlocked.Exchange(ref _clearTokenSource, new CancellationTokenSource());
            previous.Cancel();

            foreach (var prefix in _prefixes.Keys)
            {
                if (_prefixes.TryRemove(prefix, out var tokenSource))
                    tokenSource.Cancel();
            }
        }
    }
}
