using Base.Caching.Interfaces;
using Base.Caching.Key;
using Base.Caching.Managers;
using Base.Caching.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Caching.Test
{
    [TestFixture]
    public class CancellationTokenTests
    {
        private static readonly string[] ManagerNames = { "Memory", "DistributedFallback", "Hybrid" };

        private static readonly string[] OperationNames =
        {
            "GetAsync(Func<Task<T>>)", "GetAsync(Func<CancellationToken, Task<T>>)", "GetAsync(Func<T>)", "SetAsync",
            "RemoveAsync", "RemoveByPrefixAsync", "ClearAsync", "PerformActionWithLockAsync(Func<Task>)",
            "PerformActionWithLockAsync(Func<CancellationToken, Task>)"
        };

        private static IEnumerable<TestCaseData> ManagerCases() => ManagerNames.Select(name => new TestCaseData(name));

        private static IEnumerable<TestCaseData> CancelledOperationCases()
            => ManagerNames.SelectMany(manager => OperationNames.Select(operation => new TestCaseData(manager, operation)));

        private static DistributedCacheManager NewFallbackManager(IDistributedCache? distributedCache = null, IConnectionMultiplexer? multiplexer = null)
        {
            return new DistributedCacheManager(
                distributedCache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                new HttpContextAccessor(),
                multiplexer,
                new BaseDistributedCacheConfiguration { InstanceName = "App:" },
                new MemoryCache(new MemoryCacheOptions()));
        }

        private static ICacheManager NewManager(string name)
        {
            return name switch
            {
                "Memory" => new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions())),
                "DistributedFallback" => NewFallbackManager(),
                "Hybrid" => new HybridCacheManager(new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions())), NewFallbackManager()),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }

        private static CacheKey NewKey() => new($"ct.{Guid.NewGuid():N}", 10, "ct.");

        private static CancellationToken CancelledToken() => new(canceled: true);

        [TestCaseSource(nameof(CancelledOperationCases))]
        public async Task CancelledToken_ThrowsAndRunsNothing_Test(string managerName, string operation)
        {
            var manager = NewManager(managerName);
            var locker = (ICacheLocker)manager;
            var key = NewKey();
            var invoked = false;
            var token = CancelledToken();

            Func<Task> call = operation switch
            {
                "GetAsync(Func<Task<T>>)" => () => manager.GetAsync(key, () => { invoked = true; return Task.FromResult("v"); }, token),
                "GetAsync(Func<CancellationToken, Task<T>>)" => () => manager.GetAsync(key, _ => { invoked = true; return Task.FromResult("v"); }, token),
                "GetAsync(Func<T>)" => () => manager.GetAsync(key, () => { invoked = true; return "v"; }, token),
                "SetAsync" => () => manager.SetAsync(key, "v", token),
                "RemoveAsync" => () => manager.RemoveAsync(key, token),
                "RemoveByPrefixAsync" => () => manager.RemoveByPrefixAsync("ct.", token),
                "ClearAsync" => () => manager.ClearAsync(token),
                "PerformActionWithLockAsync(Func<Task>)" => () => locker.PerformActionWithLockAsync(key.Key, TimeSpan.FromSeconds(5), () => { invoked = true; return Task.CompletedTask; }, token),
                "PerformActionWithLockAsync(Func<CancellationToken, Task>)" => () => locker.PerformActionWithLockAsync(key.Key, TimeSpan.FromSeconds(5), _ => { invoked = true; return Task.CompletedTask; }, token),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };

            Assert.CatchAsync<OperationCanceledException>(() => call());

            Assert.Multiple(() =>
            {
                Assert.That(invoked, Is.False, "acquire/action must not run");
                Assert.That(manager.Get<string>(key), Is.Null, "nothing may be cached");
            });

            Assert.That(await locker.PerformActionWithLockAsync(key.Key, TimeSpan.FromSeconds(5), () => Task.CompletedTask), Is.True, "no lock may be left behind");
        }

        [TestCaseSource(nameof(ManagerCases))]
        public async Task GetAsync_TokenAcquire_ReceivesTokenAndCachesValue_Test(string managerName)
        {
            var manager = NewManager(managerName);
            var key = NewKey();
            using var source = new CancellationTokenSource();
            CancellationToken received = default;

            var first = await manager.GetAsync(key, ct => { received = ct; return Task.FromResult("loaded"); }, source.Token);
            var second = await manager.GetAsync(key, _ => Task.FromResult("reloaded"), source.Token);

            Assert.Multiple(() =>
            {
                Assert.That(received, Is.EqualTo(source.Token));
                Assert.That(first, Is.EqualTo("loaded"));
                Assert.That(second, Is.EqualTo("loaded"));
            });
        }

        [TestCaseSource(nameof(ManagerCases))]
        public async Task GetAsync_WithoutToken_KeepsWorking_Test(string managerName)
        {
            var manager = NewManager(managerName);
            var key = NewKey();

            var first = await manager.GetAsync(key, () => Task.FromResult("loaded"));
            var second = await manager.GetAsync(key, () => "reloaded");
            await manager.RemoveAsync(key);
            var third = await manager.GetAsync(key, _ => Task.FromResult("after-remove"));

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo("loaded"));
                Assert.That(second, Is.EqualTo("loaded"));
                Assert.That(third, Is.EqualTo("after-remove"));
            });
        }

        [TestCaseSource(nameof(ManagerCases))]
        public async Task PerformActionWithLockAsync_TokenAction_ReceivesTokenAndReleasesLock_Test(string managerName)
        {
            var locker = (ICacheLocker)NewManager(managerName);
            var resource = $"lock.{Guid.NewGuid():N}";
            using var source = new CancellationTokenSource();
            CancellationToken received = default;

            var ran = await locker.PerformActionWithLockAsync(resource, TimeSpan.FromSeconds(30), ct => { received = ct; return Task.CompletedTask; }, source.Token);
            var again = await locker.PerformActionWithLockAsync(resource, TimeSpan.FromSeconds(30), () => Task.CompletedTask);

            Assert.Multiple(() =>
            {
                Assert.That(ran, Is.True);
                Assert.That(received, Is.EqualTo(source.Token));
                Assert.That(again, Is.True);
            });
        }

        [TestCaseSource(nameof(ManagerCases))]
        public async Task PerformActionWithLockAsync_TokenCancelledDuringAction_ReleasesLock_Test(string managerName)
        {
            var locker = (ICacheLocker)NewManager(managerName);
            var resource = $"lock.{Guid.NewGuid():N}";
            using var source = new CancellationTokenSource();

            Assert.CatchAsync<OperationCanceledException>(() => locker.PerformActionWithLockAsync(resource, TimeSpan.FromSeconds(30), ct =>
            {
                source.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, source.Token));

            Assert.That(await locker.PerformActionWithLockAsync(resource, TimeSpan.FromSeconds(30), () => Task.CompletedTask), Is.True);
        }

        [Test]
        public async Task Distributed_Connected_PassesTokenToDistributedStore_Test()
        {
            var store = new RecordingDistributedCache();
            var manager = NewFallbackManager(store, new ConnectedFakeRedis().Multiplexer);
            var key = NewKey();
            using var source = new CancellationTokenSource();

            await manager.GetAsync(key, _ => Task.FromResult("loaded"), source.Token);
            await manager.RemoveAsync(key, source.Token);
            await manager.SetAsync(key, "set", source.Token);

            Assert.Multiple(() =>
            {
                Assert.That(store.Calls.Select(c => c.Method), Is.EqualTo(new[] { "GetAsync", "SetAsync", "RemoveAsync", "SetAsync" }));
                Assert.That(store.Calls.Select(c => c.Token), Is.All.EqualTo(source.Token));
            });
        }

        [Test]
        public async Task Hybrid_Connected_PassesTokenToDistributedStore_Test()
        {
            var store = new RecordingDistributedCache();
            var manager = new HybridCacheManager(
                new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions())),
                NewFallbackManager(store, new ConnectedFakeRedis().Multiplexer));
            var key = NewKey();
            using var source = new CancellationTokenSource();

            await manager.GetAsync(key, () => Task.FromResult("loaded"), source.Token);
            await manager.RemoveAsync(key, source.Token);

            Assert.Multiple(() =>
            {
                Assert.That(store.Calls.Select(c => c.Method), Is.EqualTo(new[] { "GetAsync", "SetAsync", "RemoveAsync" }));
                Assert.That(store.Calls.Select(c => c.Token), Is.All.EqualTo(source.Token));
            });
        }

        [Test]
        public void Distributed_Connected_CancellationDuringStoreCall_IsNotTreatedAsConnectionFailure_Test()
        {
            var store = new RecordingDistributedCache();
            using var source = new CancellationTokenSource();
            store.OnCall = (_, token) =>
            {
                source.Cancel();
                token.ThrowIfCancellationRequested();
            };
            var manager = NewFallbackManager(store, new ConnectedFakeRedis().Multiplexer);
            var acquired = false;

            Assert.CatchAsync<OperationCanceledException>(() =>
                manager.GetAsync(NewKey(), _ => { acquired = true; return Task.FromResult("fallback"); }, source.Token));

            Assert.That(acquired, Is.False);
        }

        [Test]
        public async Task Distributed_Connected_StoreFailureWithoutCancellation_StillFallsBack_Test()
        {
            var store = new RecordingDistributedCache
            {
                OnCall = (_, _) => throw new OperationCanceledException("store timeout")
            };
            var manager = NewFallbackManager(store, new ConnectedFakeRedis().Multiplexer);
            var key = NewKey();
            using var source = new CancellationTokenSource();

            var first = await manager.GetAsync(key, _ => Task.FromResult("loaded"), source.Token);
            var cached = manager.Get<string>(key);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo("loaded"));
                Assert.That(cached, Is.EqualTo("loaded"), "value is written to the in-memory fallback");
            });
        }

        [Test]
        public void Distributed_Connected_ClearAsync_CancelledBetweenBatches_StopsDeleting_Test()
        {
            var redis = new ConnectedFakeRedis
            {
                ServerKeys = Enumerable.Range(0, 2500).Select(i => (RedisKey)$"App:item.{i}").ToArray()
            };
            using var source = new CancellationTokenSource();
            redis.OnKeyDelete = () =>
            {
                source.Cancel();
                return null;
            };
            var manager = NewFallbackManager(multiplexer: redis.Multiplexer);

            Assert.CatchAsync<OperationCanceledException>(() => manager.ClearAsync(source.Token));

            Assert.That(redis.DeleteBatchCount, Is.EqualTo(1));
        }

        [Test]
        public void Distributed_Connected_RemoveByPrefixAsync_CancelledBetweenBatches_StopsDeleting_Test()
        {
            var redis = new ConnectedFakeRedis
            {
                ServerKeys = Enumerable.Range(0, 1500).Select(i => (RedisKey)$"App:user.{i}").ToArray()
            };
            using var source = new CancellationTokenSource();
            redis.OnKeyDelete = () =>
            {
                source.Cancel();
                return null;
            };
            var manager = NewFallbackManager(multiplexer: redis.Multiplexer);

            Assert.CatchAsync<OperationCanceledException>(() => manager.RemoveByPrefixAsync("user.", source.Token));

            Assert.That(redis.DeleteBatchCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Distributed_Connected_ClearAsync_WithoutCancellation_DeletesAllBatches_Test()
        {
            var redis = new ConnectedFakeRedis
            {
                ServerKeys = Enumerable.Range(0, 2500).Select(i => (RedisKey)$"App:item.{i}").ToArray()
            };
            var manager = NewFallbackManager(multiplexer: redis.Multiplexer);
            using var source = new CancellationTokenSource();

            await manager.ClearAsync(source.Token);

            Assert.That(redis.DeleteBatchCount, Is.EqualTo(3));
        }
    }
}
