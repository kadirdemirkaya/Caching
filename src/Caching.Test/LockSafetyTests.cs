using System.Net;
using System.Reflection;
using Base.Caching.Configurations;
using Base.Caching.Managers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Caching.Test
{
    [TestFixture]
    public class LockSafetyTests
    {
        private sealed class FakeLockRedis
        {
            private readonly object _sync = new();

            private readonly Dictionary<string, string> _store = new();

            private readonly List<RedisKey> _serverKeys = new();

            public IConnectionMultiplexer Multiplexer { get; }

            public DistributedClearSafetyTests.FuncProxy Database { get; }

            public bool IsConnected { get; set; } = true;

            public bool ThrowOnRelease { get; set; }

            public FakeLockRedis()
            {
                var database = DispatchProxy.Create<IDatabase, DistributedClearSafetyTests.FuncProxy>();
                Database = (DistributedClearSafetyTests.FuncProxy)(object)database;
                Database.Handler = HandleDatabase;

                Multiplexer = DispatchProxy.Create<IConnectionMultiplexer, DistributedClearSafetyTests.FuncProxy>();
                ((DistributedClearSafetyTests.FuncProxy)(object)Multiplexer).Handler = (name, args) => name switch
                {
                    "get_IsConnected" => IsConnected,
                    "GetDatabase" => database,
                    "GetEndPoints" => new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) },
                    "GetServer" => CreateServer(),
                    _ => null
                };
            }

            public void AddServerKeys(params string[] keys) => _serverKeys.AddRange(keys.Select(k => (RedisKey)k));

            public void Put(string key, string value)
            {
                lock (_sync)
                    _store[key] = value;
            }

            public void Expire(string key)
            {
                lock (_sync)
                    _store.Remove(key);
            }

            public string? ValueOf(string key)
            {
                lock (_sync)
                    return _store.TryGetValue(key, out var value) ? value : null;
            }

            public IReadOnlyCollection<string> Keys
            {
                get
                {
                    lock (_sync)
                        return _store.Keys.ToList();
                }
            }

            public List<string> DeletedServerKeys => Database.Calls
                .Where(c => c.Method is "KeyDeleteAsync" or "KeyDelete" && c.Args[0] is RedisKey[])
                .SelectMany(c => (RedisKey[])c.Args[0]!)
                .Select(k => k.ToString())
                .ToList();

            private IServer CreateServer()
            {
                var server = DispatchProxy.Create<IServer, DistributedClearSafetyTests.FuncProxy>();
                ((DistributedClearSafetyTests.FuncProxy)(object)server).Handler = (name, _) => name switch
                {
                    "get_IsReplica" => false,
                    "KeysAsync" => ToAsync(_serverKeys.ToArray()),
                    "Keys" => _serverKeys.ToArray(),
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

            private object? HandleDatabase(string name, object?[] args)
            {
                return name switch
                {
                    "get_Database" => 0,
                    "LockTake" => TakeIfAbsent(args),
                    "LockTakeAsync" => Task.FromResult(TakeIfAbsent(args)),
                    "LockRelease" => ReleaseIfOwner(args),
                    "LockReleaseAsync" => Task.FromResult(ReleaseIfOwner(args)),
                    "StringSet" => StringSet(args),
                    "StringSetAsync" => Task.FromResult(StringSet(args)),
                    "KeyDelete" when args[0] is RedisKey => DeleteSingle(args),
                    "KeyDeleteAsync" when args[0] is RedisKey => Task.FromResult(DeleteSingle(args)),
                    "KeyDelete" => 0L,
                    "KeyDeleteAsync" => Task.FromResult(0L),
                    _ => null
                };
            }

            private bool TakeIfAbsent(object?[] args)
            {
                var key = ((RedisKey)args[0]!).ToString();
                var value = ((RedisValue)args[1]!).ToString();
                lock (_sync)
                    return _store.TryAdd(key, value);
            }

            private bool ReleaseIfOwner(object?[] args)
            {
                if (ThrowOnRelease)
                    throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "connection lost");

                var key = ((RedisKey)args[0]!).ToString();
                var value = ((RedisValue)args[1]!).ToString();
                lock (_sync)
                {
                    if (_store.TryGetValue(key, out var current) && current == value)
                        return _store.Remove(key);
                    return false;
                }
            }

            private bool StringSet(object?[] args)
            {
                var key = ((RedisKey)args[0]!).ToString();
                var value = ((RedisValue)args[1]!).ToString();
                var onlyIfAbsent = args.OfType<When>().Any(w => w == When.NotExists);
                lock (_sync)
                {
                    if (onlyIfAbsent)
                        return _store.TryAdd(key, value);
                    _store[key] = value;
                    return true;
                }
            }

            private bool DeleteSingle(object?[] args)
            {
                if (ThrowOnRelease)
                    throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "connection lost");

                var key = ((RedisKey)args[0]!).ToString();
                lock (_sync)
                    return _store.Remove(key);
            }
        }

        private static DistributedCacheManager CreateManager(FakeLockRedis redis, string instanceName)
        {
            var distributedCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            return new DistributedCacheManager(distributedCache, new HttpContextAccessor(), redis.Multiplexer,
                new BaseDistributedCacheConfiguration { InstanceName = instanceName }, new MemoryCache(new MemoryCacheOptions()));
        }

        private static Task<bool> RunWithLock(DistributedCacheManager manager, bool async, string resource, TimeSpan expiry, Action action)
        {
            if (!async)
                return Task.FromResult(manager.PerformActionWithLock(resource, expiry, action));

            return manager.PerformActionWithLockAsync(resource, expiry, async () =>
            {
                await Task.Yield();
                action();
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Distributed_ExpiredLockTakenByOtherOwner_ReleaseKeepsOtherOwnersLock_Test(bool async)
        {
            var redis = new FakeLockRedis();
            var manager = CreateManager(redis, "App1:");

            var acquired = await RunWithLock(manager, async, "order.1", TimeSpan.FromSeconds(30), () =>
            {
                var heldKey = redis.Keys.Single();
                redis.Expire(heldKey);
                redis.Put(heldKey, "other-owner");
            });

            Assert.That(acquired, Is.True);
            Assert.That(redis.Keys, Has.Count.EqualTo(1));
            Assert.That(redis.ValueOf(redis.Keys.Single()), Is.EqualTo("other-owner"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Distributed_LockKey_IsPrefixedWithInstanceName_Test(bool async)
        {
            var redis = new FakeLockRedis();
            var manager = CreateManager(redis, "App1:");
            var heldKeys = new List<string>();

            var acquired = await RunWithLock(manager, async, "order.1", TimeSpan.FromSeconds(30), () => heldKeys.AddRange(redis.Keys));

            Assert.That(acquired, Is.True);
            Assert.That(heldKeys, Is.EqualTo(new[] { "App1:lock:order.1" }));
            Assert.That(redis.Keys, Is.Empty);
        }

        [Test]
        public void Distributed_SameResourceInDifferentInstances_DoNotBlockEachOther_Test()
        {
            var redis = new FakeLockRedis();
            var first = CreateManager(redis, "App1:");
            var second = CreateManager(redis, "App2:");
            var innerAcquired = false;

            var outerAcquired = first.PerformActionWithLock("order.1", TimeSpan.FromSeconds(30), () =>
            {
                innerAcquired = second.PerformActionWithLock("order.1", TimeSpan.FromSeconds(30), () => { });
            });

            Assert.That(outerAcquired, Is.True);
            Assert.That(innerAcquired, Is.True);
        }

        [Test]
        public void Distributed_SameResourceInSameInstance_IsBlocked_Test()
        {
            var redis = new FakeLockRedis();
            var first = CreateManager(redis, "App1:");
            var second = CreateManager(redis, "App1:");
            var innerAcquired = true;

            var outerAcquired = first.PerformActionWithLock("order.1", TimeSpan.FromSeconds(30), () =>
            {
                innerAcquired = second.PerformActionWithLock("order.1", TimeSpan.FromSeconds(30), () => { });
            });

            Assert.That(outerAcquired, Is.True);
            Assert.That(innerAcquired, Is.False);
            Assert.That(redis.Keys, Is.Empty);
        }

        [Test]
        public async Task Distributed_LockValue_IsUniqueTokenPerAcquisition_Test()
        {
            var redis = new FakeLockRedis();
            var manager = CreateManager(redis, "App1:");
            var tokens = new List<string?>();

            manager.PerformActionWithLock("order.1", TimeSpan.FromSeconds(30), () => tokens.Add(redis.ValueOf(redis.Keys.Single())));
            await manager.PerformActionWithLockAsync("order.1", TimeSpan.FromSeconds(30), () =>
            {
                tokens.Add(redis.ValueOf(redis.Keys.Single()));
                return Task.CompletedTask;
            });

            Assert.That(tokens, Has.Count.EqualTo(2));
            Assert.That(tokens, Has.None.Null.And.None.EqualTo("order.1"));
            Assert.That(tokens[0], Is.Not.EqualTo(tokens[1]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Distributed_AcquiredInRedis_DisconnectedBeforeRelease_ReleasesInRedis_Test(bool async)
        {
            var redis = new FakeLockRedis();
            var manager = CreateManager(redis, "App1:");

            var acquired = await RunWithLock(manager, async, "order.1", TimeSpan.FromSeconds(30), () => redis.IsConnected = false);

            Assert.That(acquired, Is.True);
            Assert.That(redis.Keys, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Distributed_AcquiredLocally_ReconnectedBeforeRelease_ReleasesLocally_Test(bool async)
        {
            var redis = new FakeLockRedis { IsConnected = false };
            var manager = CreateManager(redis, "App1:");

            var acquired = await RunWithLock(manager, async, "order.1", TimeSpan.FromMinutes(1), () =>
            {
                redis.IsConnected = true;
                redis.Put("order.1", "other-process");
                redis.Put("App1:lock:order.1", "other-process");
            });

            Assert.That(acquired, Is.True);
            Assert.That(redis.ValueOf("order.1"), Is.EqualTo("other-process"));
            Assert.That(redis.ValueOf("App1:lock:order.1"), Is.EqualTo("other-process"));

            redis.IsConnected = false;
            var acquiredAgain = await RunWithLock(manager, async, "order.1", TimeSpan.FromMinutes(1), () => { });
            Assert.That(acquiredAgain, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Distributed_ReleaseFailsInRedis_DoesNotThrow_Test(bool async)
        {
            var redis = new FakeLockRedis();
            var manager = CreateManager(redis, "App1:");

            var acquired = await RunWithLock(manager, async, "order.1", TimeSpan.FromSeconds(30), () => redis.ThrowOnRelease = true);

            Assert.That(acquired, Is.True);
        }

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(true, false)]
        public async Task Distributed_ActionThrows_LockIsReleased_Test(bool async, bool connected)
        {
            var redis = new FakeLockRedis { IsConnected = connected };
            var manager = CreateManager(redis, "App1:");

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await RunWithLock(manager, async, "order.1", TimeSpan.FromMinutes(1), () => throw new InvalidOperationException("boom")));

            Assert.That(redis.Keys, Is.Empty);
            var acquiredAgain = await RunWithLock(manager, async, "order.1", TimeSpan.FromMinutes(1), () => { });
            Assert.That(acquiredAgain, Is.True);
        }

        [Test]
        public async Task Distributed_ClearAsync_DoesNotDeleteHeldLocks_Test()
        {
            var redis = new FakeLockRedis();
            redis.AddServerKeys("App1:lock:order.1", "App1:product.1", "App1:locker.settings");
            var manager = CreateManager(redis, "App1:");

            await manager.ClearAsync();
            await manager.RemoveByPrefixAsync("lock");

            Assert.That(redis.DeletedServerKeys, Has.None.EqualTo("App1:lock:order.1"));
            Assert.That(redis.DeletedServerKeys, Does.Contain("App1:product.1").And.Contain("App1:locker.settings"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Memory_ActionThrows_LockIsReleased_Test(bool async)
        {
            var manager = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));

            if (async)
                Assert.ThrowsAsync<InvalidOperationException>(() =>
                    manager.PerformActionWithLockAsync("order.1", TimeSpan.FromMinutes(1), () => throw new InvalidOperationException("boom")));
            else
                Assert.Throws<InvalidOperationException>(() =>
                    manager.PerformActionWithLock("order.1", TimeSpan.FromMinutes(1), () => throw new InvalidOperationException("boom")));

            var acquiredAgain = async
                ? await manager.PerformActionWithLockAsync("order.1", TimeSpan.FromMinutes(1), () => Task.CompletedTask)
                : manager.PerformActionWithLock("order.1", TimeSpan.FromMinutes(1), () => { });
            Assert.That(acquiredAgain, Is.True);
        }

        [Test]
        public async Task Memory_ExpiredLockTakenByOtherOwner_ReleaseKeepsOtherOwnersLock_Test()
        {
            var manager = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            await AssertExpiredTakeoverKeepsSecondOwner(manager);
        }

        [Test]
        public async Task DistributedFallback_ExpiredLockTakenByOtherOwner_ReleaseKeepsOtherOwnersLock_Test()
        {
            var redis = new FakeLockRedis { IsConnected = false };
            var manager = CreateManager(redis, "App1:");
            await AssertExpiredTakeoverKeepsSecondOwner(manager);
        }

        private static async Task AssertExpiredTakeoverKeepsSecondOwner(Base.Caching.Interfaces.ICacheLocker locker)
        {
            using var secondInside = new SemaphoreSlim(0);
            using var releaseSecond = new SemaphoreSlim(0);
            Task<bool>? second = null;

            var first = await locker.PerformActionWithLockAsync("order.1", TimeSpan.FromMilliseconds(1), async () =>
            {
                await Task.Delay(50);
                second = locker.PerformActionWithLockAsync("order.1", TimeSpan.FromMinutes(1), async () =>
                {
                    secondInside.Release();
                    await releaseSecond.WaitAsync();
                });
                await secondInside.WaitAsync();
            });

            var third = await locker.PerformActionWithLockAsync("order.1", TimeSpan.FromMinutes(1), () => Task.CompletedTask);
            releaseSecond.Release();

            Assert.That(first, Is.True);
            Assert.That(second, Is.Not.Null);
            Assert.That(await second!, Is.True);
            Assert.That(third, Is.False);
        }
    }
}
