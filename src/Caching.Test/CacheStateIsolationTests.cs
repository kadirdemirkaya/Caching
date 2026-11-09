using Base.Caching.Interfaces;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Caching.Test
{
    [TestFixture]
    public class CacheStateIsolationTests
    {
        private static ServiceProvider BuildMemoryProvider()
        {
            var services = new ServiceCollection();
            services.AddMemoryCache();
            services.AddSingleton<MemoryCacheManager>();
            return services.BuildServiceProvider();
        }

        private static ServiceProvider BuildFallbackProvider()
        {
            var services = new ServiceCollection();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
            services.AddScoped<DistributedCacheManager>();
            return services.BuildServiceProvider();
        }

        [Test]
        public async Task MemoryManager_ClearAsync_DoesNotClearOtherServiceProvider_Test()
        {
            using var providerA = BuildMemoryProvider();
            using var providerB = BuildMemoryProvider();
            var managerA = providerA.GetRequiredService<MemoryCacheManager>();
            var managerB = providerB.GetRequiredService<MemoryCacheManager>();
            var key = new CacheKey("isolation.memory.clear", "isolation.memory");

            await managerA.SetAsync(key, "a");
            await managerB.SetAsync(key, "b");

            await managerA.ClearAsync();

            Assert.That(managerA.Get<string>(key), Is.Null);
            Assert.That(managerB.Get<string>(key), Is.EqualTo("b"));
        }

        [Test]
        public async Task MemoryManager_RemoveByPrefixAsync_DoesNotAffectOtherServiceProvider_Test()
        {
            using var providerA = BuildMemoryProvider();
            using var providerB = BuildMemoryProvider();
            var managerA = providerA.GetRequiredService<MemoryCacheManager>();
            var managerB = providerB.GetRequiredService<MemoryCacheManager>();
            var key = new CacheKey("isolation.memory.prefix", "isolation.prefix");

            await managerA.SetAsync(key, "a");
            await managerB.SetAsync(key, "b");

            await managerA.RemoveByPrefixAsync("isolation.prefix");

            Assert.That(managerA.Get<string>(key), Is.Null);
            Assert.That(managerB.Get<string>(key), Is.EqualTo("b"));
        }

        [Test]
        public void MemoryManager_Lock_IsNotSharedWithOtherServiceProvider_Test()
        {
            using var providerA = BuildMemoryProvider();
            using var providerB = BuildMemoryProvider();
            var managerA = providerA.GetRequiredService<MemoryCacheManager>();
            var managerB = providerB.GetRequiredService<MemoryCacheManager>();
            var innerAcquired = false;

            var outerAcquired = managerA.PerformActionWithLock("isolation.lock", TimeSpan.FromMinutes(1), () =>
            {
                innerAcquired = managerB.PerformActionWithLock("isolation.lock", TimeSpan.FromMinutes(1), () => { });
            });

            Assert.That(outerAcquired, Is.True);
            Assert.That(innerAcquired, Is.True);
        }

        [Test]
        public void MemoryManager_Lock_IsSharedBetweenManagersOfSameMemoryCache_Test()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var managerA = new MemoryCacheManager(memoryCache);
            var managerB = new MemoryCacheManager(memoryCache);
            var innerAcquired = true;

            var outerAcquired = managerA.PerformActionWithLock("same.cache.lock", TimeSpan.FromMinutes(1), () =>
            {
                innerAcquired = managerB.PerformActionWithLock("same.cache.lock", TimeSpan.FromMinutes(1), () => { });
            });

            Assert.That(outerAcquired, Is.True);
            Assert.That(innerAcquired, Is.False);
        }

        [Test]
        public async Task MemoryManager_PrefixInvalidation_IsSharedBetweenManagersOfSameMemoryCache_Test()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var managerA = new MemoryCacheManager(memoryCache);
            var managerB = new MemoryCacheManager(memoryCache);
            var key = new CacheKey("same.cache.prefix", "same.cache");

            await managerA.SetAsync(key, "a");
            await managerB.RemoveByPrefixAsync("same.cache");

            Assert.That(managerA.Get<string>(key), Is.Null);
        }

        [Test]
        public async Task DistributedFallback_ClearAsync_DoesNotClearOtherServiceProvider_Test()
        {
            using var providerA = BuildFallbackProvider();
            using var providerB = BuildFallbackProvider();
            using var scopeA = providerA.CreateScope();
            using var scopeB = providerB.CreateScope();
            var managerA = scopeA.ServiceProvider.GetRequiredService<DistributedCacheManager>();
            var managerB = scopeB.ServiceProvider.GetRequiredService<DistributedCacheManager>();
            var key = new CacheKey("isolation.fallback.clear", "isolation.fallback");

            await managerA.SetAsync(key, "a");
            await managerB.SetAsync(key, "b");

            await managerA.ClearAsync();

            Assert.That(managerA.Get<string>(key), Is.Null);
            Assert.That(managerB.Get<string>(key), Is.EqualTo("b"));
        }

        [Test]
        public async Task DistributedFallback_RemoveByPrefixAsync_DoesNotAffectOtherServiceProvider_Test()
        {
            using var providerA = BuildFallbackProvider();
            using var providerB = BuildFallbackProvider();
            using var scopeA = providerA.CreateScope();
            using var scopeB = providerB.CreateScope();
            var managerA = scopeA.ServiceProvider.GetRequiredService<DistributedCacheManager>();
            var managerB = scopeB.ServiceProvider.GetRequiredService<DistributedCacheManager>();
            var key = new CacheKey("isolation.fallback.prefix", "isolation.fallback.p");

            await managerA.SetAsync(key, "a");
            await managerB.SetAsync(key, "b");

            await managerA.RemoveByPrefixAsync("isolation.fallback.p");

            Assert.That(managerA.Get<string>(key), Is.Null);
            Assert.That(managerB.Get<string>(key), Is.EqualTo("b"));
        }

        [Test]
        public void DistributedFallback_Lock_IsNotSharedWithOtherServiceProvider_Test()
        {
            using var providerA = BuildFallbackProvider();
            using var providerB = BuildFallbackProvider();
            using var scopeA = providerA.CreateScope();
            using var scopeB = providerB.CreateScope();
            var managerA = scopeA.ServiceProvider.GetRequiredService<DistributedCacheManager>();
            var managerB = scopeB.ServiceProvider.GetRequiredService<DistributedCacheManager>();
            var innerAcquired = false;

            var outerAcquired = managerA.PerformActionWithLock("isolation.fallback.lock", TimeSpan.FromMinutes(1), () =>
            {
                innerAcquired = managerB.PerformActionWithLock("isolation.fallback.lock", TimeSpan.FromMinutes(1), () => { });
            });

            Assert.That(outerAcquired, Is.True);
            Assert.That(innerAcquired, Is.True);
        }

        [Test]
        public async Task DistributedFallback_StateIsSharedAcrossScopesOfSameServiceProvider_Test()
        {
            using var provider = BuildFallbackProvider();
            var key = new CacheKey("shared.fallback.key", "shared.fallback");
            var otherKey = new CacheKey("shared.fallback.other", "shared.other");

            using (var scope = provider.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<DistributedCacheManager>();
                await manager.SetAsync(key, "value");
                await manager.SetAsync(otherKey, "other");
            }

            using (var scope = provider.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<DistributedCacheManager>();
                Assert.That(manager.Get<string>(key), Is.EqualTo("value"));
                await manager.RemoveByPrefixAsync("shared.fallback");
            }

            using (var scope = provider.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<DistributedCacheManager>();
                Assert.That(manager.Get<string>(key), Is.Null);
                Assert.That(manager.Get<string>(otherKey), Is.EqualTo("other"));
                await manager.ClearAsync();
            }

            using (var scope = provider.CreateScope())
            {
                var manager = scope.ServiceProvider.GetRequiredService<DistributedCacheManager>();
                Assert.That(manager.Get<string>(otherKey), Is.Null);

                var lockerInner = true;
                using var innerScope = provider.CreateScope();
                var innerManager = innerScope.ServiceProvider.GetRequiredService<DistributedCacheManager>();
                var outer = manager.PerformActionWithLock("shared.fallback.lock", TimeSpan.FromMinutes(1), () =>
                {
                    lockerInner = innerManager.PerformActionWithLock("shared.fallback.lock", TimeSpan.FromMinutes(1), () => { });
                });

                Assert.That(outer, Is.True);
                Assert.That(lockerInner, Is.False);
            }
        }

        [Test]
        public async Task DistributedManager_ClearAsync_ClearsPerRequestEntries_Test()
        {
            using var provider = BuildFallbackProvider();
            using var scope = provider.CreateScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previousContext = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext();

            try
            {
                var manager = scope.ServiceProvider.GetRequiredService<DistributedCacheManager>();
                var key = new CacheKey("per.request.clear");
                accessor.HttpContext.Items["unrelated.item"] = "kept";

                var first = await manager.GetAsync(key, () => "first");
                await manager.ClearAsync();
                var second = await manager.GetAsync(key, () => "second");

                Assert.That(first, Is.EqualTo("first"));
                Assert.That(second, Is.EqualTo("second"));
                Assert.That(accessor.HttpContext.Items["unrelated.item"], Is.EqualTo("kept"));
            }
            finally
            {
                accessor.HttpContext = previousContext;
            }
        }

        [Test]
        public void MemoryManager_ConcurrentSetClearAndRemoveByPrefix_DoesNotThrow_Test()
        {
            using var provider = BuildMemoryProvider();
            var manager = provider.GetRequiredService<MemoryCacheManager>();

            Assert.DoesNotThrowAsync(() => RunConcurrently(manager));
        }

        [Test]
        public void DistributedFallback_ConcurrentSetClearAndRemoveByPrefix_DoesNotThrow_Test()
        {
            using var provider = BuildFallbackProvider();
            using var scope = provider.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<DistributedCacheManager>();

            Assert.DoesNotThrowAsync(() => RunConcurrently(manager));
        }

        private static async Task RunConcurrently(ICacheManager manager)
        {
            const int iterations = 2000;
            var writers = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
            {
                for (var i = 0; i < iterations; i++)
                    await manager.SetAsync(new CacheKey($"concurrent.{w}.{i % 50}", "concurrent", $"concurrent.{i % 5}"), i);
            }));
            var clearers = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    await manager.ClearAsync();
                    await manager.RemoveByPrefixAsync($"concurrent.{i % 5}");
                }
            }));

            await Task.WhenAll(writers.Concat(clearers));
        }
    }
}
