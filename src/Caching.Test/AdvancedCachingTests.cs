using Base.Caching;
using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Caching.Test
{
    [TestFixture]
    public class AdvancedCachingTests
    {
        private ServiceProvider? _serviceProvider;

        [TearDown]
        public void Cleanup()
        {
            _serviceProvider?.Dispose();
        }

        [Test]
        public void MemoryCacheManager_AtomicLock_Test()
        {
            var services = new ServiceCollection();
            services.AddMemoryCache();
            services.AddSingleton<MemoryCacheManager>();
            var provider = services.BuildServiceProvider();
            var locker = provider.GetRequiredService<MemoryCacheManager>();

            int successfulLocks = 0;
            var tasks = new List<Task>();
            var lockKey = "test.atomic.lock";

            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    bool acquired = locker.PerformActionWithLock(lockKey, TimeSpan.FromSeconds(5), () =>
                    {
                        Interlocked.Increment(ref successfulLocks);
                        Thread.Sleep(100); // Hold lock briefly
                    });
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Only 1 thread should have successfully acquired the lock and executed the action
            Assert.That(successfulLocks, Is.EqualTo(1));

            // Verify the lock is released and can be acquired again
            bool acquiredAgain = locker.PerformActionWithLock(lockKey, TimeSpan.FromSeconds(5), () => { });
            Assert.That(acquiredAgain, Is.True);
        }

        [Test]
        public async Task MemoryCacheManager_AtomicLockAsync_Test()
        {
            var services = new ServiceCollection();
            services.AddMemoryCache();
            services.AddSingleton<MemoryCacheManager>();
            var provider = services.BuildServiceProvider();
            var locker = provider.GetRequiredService<MemoryCacheManager>();

            int successfulLocks = 0;
            var tasks = new List<Task>();
            var lockKey = "test.atomic.lock.async";

            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    bool acquired = await locker.PerformActionWithLockAsync(lockKey, TimeSpan.FromSeconds(5), async () =>
                    {
                        Interlocked.Increment(ref successfulLocks);
                        await Task.Delay(100); // Hold lock briefly
                    });
                }));
            }

            await Task.WhenAll(tasks);

            // Only 1 thread should have successfully acquired the lock and executed the action
            Assert.That(successfulLocks, Is.EqualTo(1));

            // Verify the lock is released and can be acquired again
            bool acquiredAgain = await locker.PerformActionWithLockAsync(lockKey, TimeSpan.FromSeconds(5), async () => { });
            Assert.That(acquiredAgain, Is.True);
        }

        [Test]
        public async Task DistributedCache_Fallback_WhenRedisIsDown_Test()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();

            var cacheConfig = new CacheConfiguration
            {
                baseCacheConfiguration = new BaseCacheConfiguration
                {
                    DefaultCacheTime = 10,
                    ShortTermCacheTime = 2
                },
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration
                {
                    Enabled = true,
                    HybridCacheEnabled = false,
                    ConnectionString = "127.0.0.1:9999,ssl=False,connectTimeout=500,abortConnect=false",
                    InstanceName = "FallbackTest:"
                }
            };

            services.AddCaching(null, cacheConfig);
            _serviceProvider = services.BuildServiceProvider();

            var cacheManager = _serviceProvider.GetService<ICacheManager>();
            Assert.That(cacheManager, Is.InstanceOf<DistributedCacheManager>());

            var cacheKey = new CacheKey("fallback.test.key", 5);
            var valueToCache = "ResilientCacheData";

            Assert.DoesNotThrowAsync(async () =>
            {
                await cacheManager.SetAsync(cacheKey, valueToCache);
            });

            // Read the data back, it should come from the fallback in-memory cache
            string? cachedValue = null;
            Assert.DoesNotThrowAsync(async () =>
            {
                cachedValue = await cacheManager.GetAsync<string>(cacheKey, () => "default_val");
            });

            Assert.That(cachedValue, Is.EqualTo(valueToCache));
        }

        [Test]
        public async Task HybridCache_Fallback_WhenRedisIsDown_Test()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();

            // Set up configurations to point to a non-existent Redis server with HybridCacheEnabled
            var cacheConfig = new CacheConfiguration
            {
                baseCacheConfiguration = new BaseCacheConfiguration
                {
                    DefaultCacheTime = 10,
                    ShortTermCacheTime = 2
                },
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration
                {
                    Enabled = true,
                    HybridCacheEnabled = true,
                    ConnectionString = "127.0.0.1:9999,ssl=False,connectTimeout=500,abortConnect=false",
                    InstanceName = "HybridFallbackTest:"
                }
            };

            services.AddCaching(null, cacheConfig);
            _serviceProvider = services.BuildServiceProvider();

            var cacheManager = _serviceProvider.GetService<ICacheManager>();
            Assert.That(cacheManager, Is.InstanceOf<HybridCacheManager>());

            var cacheKey = new CacheKey("hybrid.test.key", 5);
            var valueToCache = "HybridCacheData";

            // Act & Assert: It should work smoothly in fallback mode
            Assert.DoesNotThrowAsync(async () =>
            {
                await cacheManager.SetAsync(cacheKey, valueToCache);
            });

            string? cachedValue = null;
            Assert.DoesNotThrowAsync(async () =>
            {
                cachedValue = await cacheManager.GetAsync<string>(cacheKey, () => "default_val");
            });

            Assert.That(cachedValue, Is.EqualTo(valueToCache));
        }

        [Test]
        public async Task Diagnostic_Test()
        {
            var services = new ServiceCollection();
            var cacheConfig = new CacheConfiguration
            {
                baseCacheConfiguration = new BaseCacheConfiguration
                {
                    DefaultCacheTime = 10,
                    ShortTermCacheTime = 2
                },
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration
                {
                    Enabled = true,
                    HybridCacheEnabled = false,
                    ConnectionString = "localhost:6379,ssl=False,connectTimeout=500,abortConnect=false",
                    InstanceName = "Diagnostic:"
                }
            };
            services.AddCaching(null, cacheConfig);
            services.AddHttpContextAccessor();
            var provider = services.BuildServiceProvider();

            var cacheManager = provider.GetRequiredService<ICacheManager>() as DistributedCacheManager;
            Assert.IsNotNull(cacheManager);

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var mc = cacheManager.GetType().GetField("_memoryCache", flags)!.GetValue(cacheManager);
            var cm = cacheManager.GetType().GetField("_connectionMultiplexer", flags)!.GetValue(cacheManager);

            Console.WriteLine($"DIAG: _memoryCache is null: {mc == null}");
            Console.WriteLine($"DIAG: _connectionMultiplexer is null: {cm == null}");

            var isConn = cacheManager.GetType().GetProperty("IsRedisConnected", flags)!.GetValue(cacheManager);
            Console.WriteLine($"DIAG: IsRedisConnected: {isConn}");

            var key = new CacheKey("diag.key");
            await cacheManager.SetAsync(key, "hello");
            var val = cacheManager.Get<string>(key);
            Console.WriteLine($"DIAG: Value from Get: {val}");
        }
    }
}
