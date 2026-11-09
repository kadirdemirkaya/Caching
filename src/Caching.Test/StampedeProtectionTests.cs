using Base.Caching;
using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Caching.Test
{
    [TestFixture]
    [NonParallelizable]
    public class StampedeProtectionTests
    {
        private const int Callers = 50;

        private static readonly string[] ManagerNames = { "Memory", "DistributedFallback", "Hybrid" };

        private static readonly string[] AsyncOverloads = { "Func<Task<T>>", "Func<CancellationToken, Task<T>>" };

        private static IEnumerable<TestCaseData> AsyncCases()
            => ManagerNames.SelectMany(manager => AsyncOverloads.Select(overload => new TestCaseData(manager, overload)));

        private static IEnumerable<TestCaseData> ManagerCases() => ManagerNames.Select(name => new TestCaseData(name));

        private static BaseCacheConfiguration Settings(bool prevent) => new() { PreventConcurrentAcquire = prevent };

        private static DistributedCacheManager NewDistributed(bool prevent)
        {
            return new DistributedCacheManager(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                new HttpContextAccessor(),
                distributedCacheConfiguration: new BaseDistributedCacheConfiguration { InstanceName = "App:" },
                memoryCache: new MemoryCache(new MemoryCacheOptions()),
                cacheConfiguration: Settings(prevent));
        }

        private static ICacheManager NewManager(string name, bool prevent)
        {
            return name switch
            {
                "Memory" => new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()), Settings(prevent)),
                "DistributedFallback" => NewDistributed(prevent),
                "Hybrid" => new HybridCacheManager(new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()), Settings(prevent)), NewDistributed(false)),
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
        }

        private static CacheKey NewKey() => new($"stampede.{Guid.NewGuid():N}", 10, "stampede.");

        private static int PendingLocks(object manager)
        {
            if (manager is HybridCacheManager)
            {
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                return PendingLocks(typeof(HybridCacheManager).GetField("_memoryCache", flags)!.GetValue(manager)!)
                    + PendingLocks(typeof(HybridCacheManager).GetField("_distributedCache", flags)!.GetValue(manager)!);
            }

            var pool = manager.GetType().GetProperty("AcquireLocks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
            return (int)pool.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pool)!;
        }

        private static Task<string> Get(ICacheManager manager, string overload, CacheKey key, Func<CancellationToken, Task<string>> acquire, CancellationToken cancellationToken = default)
        {
            return overload switch
            {
                "Func<Task<T>>" => manager.GetAsync(key, () => acquire(CancellationToken.None), cancellationToken),
                "Func<CancellationToken, Task<T>>" => manager.GetAsync(key, acquire, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(overload))
            };
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(finished, Is.SameAs(task), "call did not finish");
            return await task;
        }

        [Test]
        public void PreventConcurrentAcquire_IsOffByDefault_Test()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new BaseCacheConfiguration().PreventConcurrentAcquire, Is.False);
                Assert.That(new CacheOptions().Cache.PreventConcurrentAcquire, Is.False);
            });
        }

        [TestCaseSource(nameof(AsyncCases))]
        public async Task Enabled_ConcurrentMisses_RunAcquireOnce_Test(string managerName, string overload)
        {
            var manager = NewManager(managerName, prevent: true);
            var key = NewKey();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<string> Acquire(CancellationToken _)
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "value";
            }

            var tasks = Enumerable.Range(0, Callers).Select(_ => Get(manager, overload, key, Acquire)).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1), "only the first caller acquires while the others wait");
                Assert.That(PendingLocks(manager), Is.EqualTo(1), "one lock entry for the key");
                Assert.That(tasks.Count(task => task.IsCompleted), Is.Zero);
            });

            gate.SetResult();
            var results = await WithTimeout(Task.WhenAll(tasks));

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(results, Has.All.EqualTo("value"));
                Assert.That(PendingLocks(manager), Is.Zero, "lock entries are removed when the work ends");
            });
        }

        [TestCaseSource(nameof(AsyncCases))]
        public async Task Disabled_ConcurrentMisses_RunAcquirePerCall_Test(string managerName, string overload)
        {
            var manager = NewManager(managerName, prevent: false);
            var key = NewKey();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<string> Acquire(CancellationToken _)
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "value";
            }

            var tasks = Enumerable.Range(0, Callers).Select(_ => Get(manager, overload, key, Acquire)).ToArray();

            Assert.That(calls, Is.EqualTo(Callers));

            gate.SetResult();
            var results = await WithTimeout(Task.WhenAll(tasks));

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(Callers));
                Assert.That(results, Has.All.EqualTo("value"));
                Assert.That(PendingLocks(manager), Is.Zero);
            });
        }

        [TestCaseSource(nameof(ManagerCases))]
        public async Task Enabled_SyncAcquireOverload_RunsOnce_Test(string managerName)
        {
            var manager = NewManager(managerName, prevent: true);
            var key = NewKey();
            var calls = 0;

            string Acquire()
            {
                Interlocked.Increment(ref calls);
                Thread.Sleep(200);
                return "value";
            }

            var tasks = Enumerable.Range(0, Callers).Select(_ => Task.Run(() => manager.GetAsync(key, Acquire))).ToArray();
            var results = await WithTimeout(Task.WhenAll(tasks));

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(results, Has.All.EqualTo("value"));
                Assert.That(PendingLocks(manager), Is.Zero);
            });
        }

        [TestCaseSource(nameof(AsyncCases))]
        public async Task Enabled_AcquireThrows_WaitersRetryAndNothingIsCached_Test(string managerName, string overload)
        {
            var manager = NewManager(managerName, prevent: true);
            var key = NewKey();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<string> Acquire(CancellationToken _)
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await gate.Task;
                    throw new InvalidOperationException("acquire failed");
                }

                return "value";
            }

            var failing = Get(manager, overload, key, Acquire);
            var waiter = Get(manager, overload, key, Acquire);

            Assert.That(calls, Is.EqualTo(1));

            gate.SetResult();

            Assert.ThrowsAsync<InvalidOperationException>(() => WithTimeout(failing));
            Assert.That(await WithTimeout(waiter), Is.EqualTo("value"), "the waiting call is not blocked and acquires itself");

            var cachedRead = await WithTimeout(Get(manager, overload, key, Acquire));

            Assert.Multiple(() =>
            {
                Assert.That(cachedRead, Is.EqualTo("value"));
                Assert.That(calls, Is.EqualTo(2), "the failure was not cached; the retried value was");
                Assert.That(PendingLocks(manager), Is.Zero);
            });
        }

        [TestCaseSource(nameof(AsyncCases))]
        public async Task Enabled_AcquireThrows_NextCallRetries_Test(string managerName, string overload)
        {
            var manager = NewManager(managerName, prevent: true);
            var key = NewKey();
            var calls = 0;

            Task<string> Acquire(CancellationToken _)
            {
                return Interlocked.Increment(ref calls) == 1
                    ? Task.FromException<string>(new InvalidOperationException("acquire failed"))
                    : Task.FromResult("value");
            }

            Assert.ThrowsAsync<InvalidOperationException>(() => Get(manager, overload, key, Acquire));
            Assert.That(manager.Get<string>(key), Is.Null, "an exception is not cached");

            var result = await WithTimeout(Get(manager, overload, key, Acquire));

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo("value"));
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(PendingLocks(manager), Is.Zero);
            });
        }

        [TestCaseSource(nameof(ManagerCases))]
        public async Task Enabled_DifferentKeys_DoNotWaitForEachOther_Test(string managerName)
        {
            var manager = NewManager(managerName, prevent: true);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<string> Acquire(CancellationToken _)
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "value";
            }

            var first = manager.GetAsync(NewKey(), Acquire);
            var second = manager.GetAsync(NewKey(), Acquire);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(PendingLocks(manager), Is.EqualTo(2));
            });

            gate.SetResult();
            await WithTimeout(Task.WhenAll(first, second));

            Assert.That(PendingLocks(manager), Is.Zero);
        }

        [TestCaseSource(nameof(ManagerCases))]
        public async Task Enabled_WaiterCancelled_ThrowsAndReleasesItsLockEntry_Test(string managerName)
        {
            var manager = NewManager(managerName, prevent: true);
            var key = NewKey();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<string> Acquire(CancellationToken _)
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "value";
            }

            using var cancellation = new CancellationTokenSource();
            var holder = manager.GetAsync(key, Acquire);
            var waiter = manager.GetAsync(key, Acquire, cancellation.Token);

            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(() => WithTimeout(waiter));
            Assert.That(PendingLocks(manager), Is.EqualTo(1), "only the holder's entry remains");

            gate.SetResult();
            var result = await WithTimeout(holder);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo("value"));
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(PendingLocks(manager), Is.Zero);
            });
        }

        [Test]
        public async Task AddCaching_ConfigurationSetting_ReachesResolvedManager_Test()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:Cache:PreventConcurrentAcquire"] = "true" })
                .Build();
            var services = new ServiceCollection();
            services.AddCaching(configuration);

            using var provider = services.BuildServiceProvider();

            await AssertCoalesces(provider.GetRequiredService<ICacheManager>());
        }

        [Test]
        public async Task AddCaching_CodeFirstSetting_ReachesResolvedManager_Test()
        {
            var services = new ServiceCollection();
            services.AddCaching(options => options.Cache.PreventConcurrentAcquire = true);

            using var provider = services.BuildServiceProvider();

            await AssertCoalesces(provider.GetRequiredService<ICacheManager>());
        }

        [Test]
        public async Task Container_InjectsSettingIntoDistributedAndHybridManagers_Test()
        {
            var services = new ServiceCollection();
            services.AddSingleton(Settings(true));
            services.AddDistributedMemoryCache();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton<MemoryCacheManager>();
            services.AddScoped<DistributedCacheManager>();
            services.AddScoped<HybridCacheManager>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            await AssertCoalesces(scope.ServiceProvider.GetRequiredService<DistributedCacheManager>());
            await AssertCoalesces(scope.ServiceProvider.GetRequiredService<HybridCacheManager>());
        }

        [Test]
        public async Task AddCaching_WithoutSetting_KeepsConcurrentAcquire_Test()
        {
            var services = new ServiceCollection();
            services.AddCaching(_ => { });

            using var provider = services.BuildServiceProvider();
            var manager = provider.GetRequiredService<ICacheManager>();
            var key = NewKey();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<string> Acquire(CancellationToken _)
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "value";
            }

            var tasks = Enumerable.Range(0, 5).Select(_ => manager.GetAsync(key, Acquire)).ToArray();
            gate.SetResult();
            await WithTimeout(Task.WhenAll(tasks));

            Assert.That(calls, Is.EqualTo(5));
        }

        private static async Task AssertCoalesces(ICacheManager manager)
        {
            var key = NewKey();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<string> Acquire(CancellationToken _)
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "value";
            }

            var tasks = Enumerable.Range(0, 5).Select(_ => manager.GetAsync(key, Acquire)).ToArray();
            gate.SetResult();
            await WithTimeout(Task.WhenAll(tasks));

            Assert.That(calls, Is.EqualTo(1), manager.GetType().Name);
        }
    }
}
