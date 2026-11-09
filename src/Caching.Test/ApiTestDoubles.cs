using System.Net;
using System.Reflection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Caching.Test
{
    internal sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));

        public List<(string Method, CancellationToken Token)> Calls { get; } = new();

        public List<DistributedCacheEntryOptions> SetOptions { get; } = new();

        public Action<string, CancellationToken>? OnCall { get; set; }

        private void Record(string method, CancellationToken token)
        {
            Calls.Add((method, token));
            OnCall?.Invoke(method, token);
        }

        public byte[]? Get(string key)
        {
            Record(nameof(Get), CancellationToken.None);
            return _inner.Get(key);
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Record(nameof(GetAsync), token);
            return _inner.GetAsync(key, token);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Record(nameof(Set), CancellationToken.None);
            SetOptions.Add(options);
            _inner.Set(key, value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Record(nameof(SetAsync), token);
            SetOptions.Add(options);
            return _inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public void Remove(string key)
        {
            Record(nameof(Remove), CancellationToken.None);
            _inner.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Record(nameof(RemoveAsync), token);
            return _inner.RemoveAsync(key, token);
        }
    }

    internal sealed class RecordingMemoryCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(new MemoryCacheOptions());

        public List<ICacheEntry> Entries { get; } = new();

        public ICacheEntry CreateEntry(object key)
        {
            var entry = _inner.CreateEntry(key);
            Entries.Add(entry);
            return entry;
        }

        public ICacheEntry LastEntry(string key) => Entries.Last(e => Equals(e.Key, key));

        public void Remove(object key) => _inner.Remove(key);

        public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);

        public void Dispose() => _inner.Dispose();
    }

    internal sealed class ConnectedFakeRedis
    {
        public IConnectionMultiplexer Multiplexer { get; }

        public DistributedClearSafetyTests.FuncProxy Database { get; }

        public Func<object?> OnKeyDelete { get; set; } = () => null;

        public RedisKey[] ServerKeys { get; set; } = Array.Empty<RedisKey>();

        public ConnectedFakeRedis()
        {
            var database = DispatchProxy.Create<IDatabase, DistributedClearSafetyTests.FuncProxy>();
            Database = (DistributedClearSafetyTests.FuncProxy)(object)database;
            Database.Handler = (name, _) => name switch
            {
                "get_Database" => 0,
                "KeyDeleteAsync" => OnKeyDelete() ?? Task.FromResult(0L),
                "LockTakeAsync" => Task.FromResult(true),
                "LockReleaseAsync" => Task.FromResult(true),
                _ => null
            };

            Multiplexer = DispatchProxy.Create<IConnectionMultiplexer, DistributedClearSafetyTests.FuncProxy>();
            ((DistributedClearSafetyTests.FuncProxy)(object)Multiplexer).Handler = (name, _) => name switch
            {
                "get_IsConnected" => true,
                "GetDatabase" => database,
                "GetEndPoints" => new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) },
                "GetServer" => CreateServer(),
                _ => null
            };
        }

        public int DeleteBatchCount => Database.Calls.Count(c => c.Method == "KeyDeleteAsync");

        private IServer CreateServer()
        {
            var server = DispatchProxy.Create<IServer, DistributedClearSafetyTests.FuncProxy>();
            ((DistributedClearSafetyTests.FuncProxy)(object)server).Handler = (name, _) => name switch
            {
                "get_IsReplica" => false,
                "KeysAsync" => ToAsync(ServerKeys),
                _ => null
            };
            return server;
        }

        private static async IAsyncEnumerable<RedisKey> ToAsync(IEnumerable<RedisKey> keys)
        {
            await Task.Yield();
            foreach (var key in keys)
                yield return key;
        }
    }
}
