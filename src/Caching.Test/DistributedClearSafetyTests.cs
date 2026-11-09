using System.Net;
using System.Reflection;
using Base.Caching.Configurations;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Caching.Test
{
    [TestFixture]
    public class DistributedClearSafetyTests
    {
        public class FuncProxy : DispatchProxy
        {
            public List<(string Method, object?[] Args)> Calls { get; } = new();

            public Func<string, object?[], object?> Handler { get; set; } = (_, _) => null;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var name = targetMethod!.Name;
                var arguments = args ?? Array.Empty<object?>();
                Calls.Add((name, arguments));

                var result = Handler(name, arguments);
                if (result != null)
                    return result;

                var returnType = targetMethod.ReturnType;
                return returnType.IsValueType && returnType != typeof(void) ? Activator.CreateInstance(returnType) : null;
            }
        }

        private sealed class FakeRedis
        {
            public IConnectionMultiplexer Multiplexer { get; }

            public FuncProxy Database { get; }

            public Dictionary<EndPoint, FuncProxy> Servers { get; } = new();

            private readonly Dictionary<EndPoint, (bool IsReplica, RedisKey[] Keys)> _servers = new();

            public FakeRedis()
            {
                var database = DispatchProxy.Create<IDatabase, FuncProxy>();
                Database = (FuncProxy)(object)database;
                Database.Handler = (name, _) => name switch
                {
                    "get_Database" => 0,
                    "KeyDeleteAsync" => Task.FromResult(0L),
                    "KeyDelete" => 0L,
                    _ => null
                };

                Multiplexer = DispatchProxy.Create<IConnectionMultiplexer, FuncProxy>();
                ((FuncProxy)(object)Multiplexer).Handler = (name, args) => name switch
                {
                    "get_IsConnected" => true,
                    "GetDatabase" => database,
                    "GetEndPoints" => _servers.Keys.ToArray(),
                    "GetServer" => CreateServer((EndPoint)args[0]!),
                    _ => null
                };
            }

            public void AddServer(int port, bool isReplica, params string[] keys)
            {
                _servers[new IPEndPoint(IPAddress.Loopback, port)] = (isReplica, keys.Select(k => (RedisKey)k).ToArray());
            }

            private IServer CreateServer(EndPoint endpoint)
            {
                var (isReplica, keys) = _servers[endpoint];
                var server = DispatchProxy.Create<IServer, FuncProxy>();
                var recorder = (FuncProxy)(object)server;
                recorder.Handler = (name, _) => name switch
                {
                    "get_IsReplica" => isReplica,
                    "KeysAsync" => ToAsync(keys),
                    "Keys" => keys,
                    _ => null
                };
                Servers[endpoint] = recorder;
                return server;
            }

            public IEnumerable<(string Method, object?[] Args)> ServerCalls => Servers.Values.SelectMany(s => s.Calls);

            public List<string> ScanPatterns => ServerCalls
                .Where(c => c.Method is "Keys" or "KeysAsync")
                .Select(c => ((RedisValue)c.Args[1]!).ToString())
                .ToList();

            public List<RedisKey[]> DeleteBatches => Database.Calls
                .Where(c => c.Method is "KeyDeleteAsync" or "KeyDelete" && c.Args[0] is RedisKey[])
                .Select(c => (RedisKey[])c.Args[0]!)
                .ToList();

            public List<string> DeletedKeys => DeleteBatches.SelectMany(b => b).Select(k => k.ToString()).ToList();
        }

        private sealed class RecordingLogger : ILogger<DistributedCacheManager>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }

        private static async IAsyncEnumerable<RedisKey> ToAsync(IEnumerable<RedisKey> keys)
        {
            await Task.Yield();
            foreach (var key in keys)
                yield return key;
        }

        private static DistributedCacheManager CreateManager(FakeRedis redis, BaseDistributedCacheConfiguration? configuration, IHttpContextAccessor? accessor = null)
        {
            var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            return new DistributedCacheManager(distributedCache, accessor ?? new HttpContextAccessor(), redis.Multiplexer, configuration!);
        }

        [TestCase(null)]
        [TestCase("")]
        public async Task ClearAsync_EmptyInstanceName_DoesNotDeleteKeys_Test(string? instanceName)
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "OtherApp:key", "unprefixed");
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = instanceName! });

            await manager.ClearAsync();

            Assert.That(redis.ScanPatterns, Is.Empty);
            Assert.That(redis.DeletedKeys, Is.Empty);
        }

        [Test]
        public async Task ClearAsync_NullConfiguration_DoesNotDeleteKeys_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "OtherApp:key");
            var manager = CreateManager(redis, null);

            await manager.ClearAsync();

            Assert.That(redis.ScanPatterns, Is.Empty);
            Assert.That(redis.DeletedKeys, Is.Empty);
        }

        [Test]
        public async Task ClearAsync_EmptyInstanceName_LogsWarning_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "OtherApp:key");
            var logger = new RecordingLogger();
            var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            var manager = new DistributedCacheManager(distributedCache, new HttpContextAccessor(), redis.Multiplexer,
                new BaseDistributedCacheConfiguration { InstanceName = string.Empty }, null!, logger);

            await manager.ClearAsync();

            Assert.That(logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("InstanceName is empty")), Is.EqualTo(1));
            Assert.That(redis.DeletedKeys, Is.Empty);
        }

        [Test]
        public async Task ClearAsync_EmptyInstanceNameWithOptIn_DeletesEveryKey_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "OtherApp:key", "unprefixed");
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = string.Empty, AllowClearWithoutInstanceName = true });

            await manager.ClearAsync();

            Assert.That(redis.ScanPatterns, Is.EqualTo(new[] { "*" }));
            Assert.That(redis.DeletedKeys, Is.EquivalentTo(new[] { "OtherApp:key", "unprefixed" }));
        }

        [Test]
        public async Task RemoveByPrefixAsync_EmptyInstanceNameAndPrefixWithOptIn_DeletesEveryKey_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "OtherApp:key");
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { AllowClearWithoutInstanceName = true });

            await manager.RemoveByPrefixAsync(string.Empty);

            Assert.That(redis.ScanPatterns, Is.EqualTo(new[] { "*" }));
            Assert.That(redis.DeletedKeys, Is.EqualTo(new[] { "OtherApp:key" }));
        }

        [Test]
        public async Task ClearAsync_ScansOnlyPrimaryServers_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "App:a", "App:b");
            redis.AddServer(6380, true, "App:replica-only");
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = "App:" });

            await manager.ClearAsync();

            Assert.That(redis.ScanPatterns, Is.EqualTo(new[] { "App:*" }));
            Assert.That(redis.DeletedKeys, Is.EquivalentTo(new[] { "App:a", "App:b" }));
            var replica = redis.Servers.Single(s => ((IPEndPoint)s.Key).Port == 6380).Value;
            Assert.That(replica.Calls.Any(c => c.Method is "Keys" or "KeysAsync"), Is.False);
        }

        [Test]
        public async Task ClearAsync_DeletesInBatches_Test()
        {
            var redis = new FakeRedis();
            var keys = Enumerable.Range(0, 2500).Select(i => $"App:{i}").ToArray();
            redis.AddServer(6379, false, keys);
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = "App:" });

            await manager.ClearAsync();

            Assert.That(redis.DeleteBatches.Select(b => b.Length), Is.EqualTo(new[] { 1000, 1000, 500 }));
            Assert.That(redis.DeletedKeys, Is.EquivalentTo(keys));
        }

        [Test]
        public async Task ClearAsync_EscapesGlobCharactersInInstanceName_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false);
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = @"Tenant*[1]?\:" });

            await manager.ClearAsync();

            Assert.That(redis.ScanPatterns, Is.EqualTo(new[] { @"Tenant\*\[1\]\?\\:*" }));
        }

        [Test]
        public async Task RemoveByPrefixAsync_EscapesGlobCharactersInPrefix_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "App:user[1]*x");
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = "App?" });

            await manager.RemoveByPrefixAsync("user[1]*");

            Assert.That(redis.ScanPatterns, Is.EqualTo(new[] { @"App\?user\[1\]\**" }));
            Assert.That(redis.DeletedKeys, Is.EqualTo(new[] { "App:user[1]*x" }));
        }

        [Test]
        public async Task RemoveByPrefixAsync_ScansOnlyPrimaryServersInBatches_Test()
        {
            var redis = new FakeRedis();
            var keys = Enumerable.Range(0, 1001).Select(i => $"App:user.{i}").ToArray();
            redis.AddServer(6379, false, keys);
            redis.AddServer(6380, true, "App:user.replica");
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = "App:" });

            await manager.RemoveByPrefixAsync("user.");

            Assert.That(redis.ScanPatterns, Is.EqualTo(new[] { "App:user.*" }));
            Assert.That(redis.DeleteBatches.Select(b => b.Length), Is.EqualTo(new[] { 1000, 1 }));
            Assert.That(redis.DeletedKeys, Is.EquivalentTo(keys));
        }

        [Test]
        public async Task RemoveByPrefixAsync_EmptyInstanceNameAndEmptyPrefix_DoesNotDeleteKeys_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "OtherApp:key");
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = string.Empty });

            await manager.RemoveByPrefixAsync(string.Empty);

            Assert.That(redis.ScanPatterns, Is.Empty);
            Assert.That(redis.DeletedKeys, Is.Empty);
        }

        [Test]
        public async Task RemoveByPrefixAsync_EmptyInstanceNameWithPrefix_DeletesMatchingKeys_Test()
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false, "user.1");
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = string.Empty });

            await manager.RemoveByPrefixAsync("user.");

            Assert.That(redis.ScanPatterns, Is.EqualTo(new[] { "user.*" }));
            Assert.That(redis.DeletedKeys, Is.EqualTo(new[] { "user.1" }));
        }

        [TestCase("App:")]
        [TestCase("")]
        public async Task ClearAsync_ConnectedPath_ClearsPerRequestEntries_Test(string instanceName)
        {
            var redis = new FakeRedis();
            redis.AddServer(6379, false);
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var manager = CreateManager(redis, new BaseDistributedCacheConfiguration { InstanceName = instanceName }, accessor);
            var key = new CacheKey("per.request.connected");

            try
            {
                accessor.HttpContext.Items["unrelated.item"] = "kept";
                var first = await manager.GetAsync(key, () => "first");
                var cachedForRequest = accessor.HttpContext.Items.ContainsKey(key.Key);

                await manager.ClearAsync();

                Assert.That(first, Is.EqualTo("first"));
                Assert.That(cachedForRequest, Is.True);
                Assert.That(accessor.HttpContext.Items.ContainsKey(key.Key), Is.False);
                Assert.That(accessor.HttpContext.Items["unrelated.item"], Is.EqualTo("kept"));
            }
            finally
            {
                accessor.HttpContext = null;
            }
        }
    }
}
