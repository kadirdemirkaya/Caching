using Base.Caching;
using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Caching.Test
{
    [TestFixture]
    [Category("Integration")]
    [Explicit("Requires Redis on localhost:6379")]
    public class RedisIntegrationTests
    {
        private ServiceProvider _serviceProvider;
        private ICacheManager _cache;
        private ICacheLocker _locker;
        private ConnectionMultiplexer _multiplexer;

        [OneTimeSetUp]
        public void Setup()
        {
            _multiplexer = (ConnectionMultiplexer)ConnectionMultiplexer.Connect("localhost:6379,allowAdmin=true");

            var services = new ServiceCollection();
            services.AddCaching(null, new CacheConfiguration
            {
                baseCacheConfiguration = new BaseCacheConfiguration
                {
                    DefaultCacheTime = 10,
                    ShortTermCacheTime = 1
                },
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration
                {
                    Enabled = true,
                    HybridCacheEnabled = false,
                    ConnectionString = "localhost:6379,allowAdmin=true",
                    InstanceName = "IntegrationTest:"
                }
            });
            services.AddHttpContextAccessor();
            services.AddLogging();
            _serviceProvider = services.BuildServiceProvider();

            _cache = _serviceProvider.GetRequiredService<ICacheManager>();
            _locker = _serviceProvider.GetRequiredService<ICacheLocker>();

            var diMux = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
            var retries = 20;
            while (retries-- > 0 && !diMux.IsConnected)
                Thread.Sleep(100);

            // Clean before tests
            var server = _multiplexer.GetServer(_multiplexer.GetEndPoints()[0]);
            foreach (var key in server.Keys(pattern: "IntegrationTest:*"))
                _multiplexer.GetDatabase().KeyDelete(key);
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            var server = _multiplexer.GetServer(_multiplexer.GetEndPoints()[0]);
            foreach (var key in server.Keys(pattern: "IntegrationTest:*"))
                _multiplexer.GetDatabase().KeyDelete(key);

            _serviceProvider?.Dispose();
            _multiplexer?.Dispose();
        }

        [Test]
        public async Task Set_And_Get_String()
        {
            var key = new CacheKey("string.key", 5);
            await _cache.SetAsync(key, "hello redis");

            var result = _cache.Get<string>(key);
            Assert.That(result, Is.EqualTo("hello redis"));
        }

        [Test]
        public async Task Set_And_Get_Object()
        {
            var key = new CacheKey("object.key", 5);
            var user = new User { Name = "Test", Age = 25, Role = "Admin" };
            await _cache.SetAsync(key, user);

            var result = _cache.Get<User>(key);
            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result.Name, Is.EqualTo("Test"));
                Assert.That(result.Age, Is.EqualTo(25));
                Assert.That(result.Role, Is.EqualTo("Admin"));
            });
        }

        [Test]
        public async Task Get_With_Acquire_Caches_Result()
        {
            var key = new CacheKey("acquire.key", 5);
            int callCount = 0;

            var result1 = await _cache.GetAsync(key, () =>
            {
                Interlocked.Increment(ref callCount);
                return "from_factory";
            });

            var result2 = await _cache.GetAsync(key, () =>
            {
                Interlocked.Increment(ref callCount);
                return "should_not_run";
            });

            Assert.Multiple(() =>
            {
                Assert.That(result1, Is.EqualTo("from_factory"));
                Assert.That(result2, Is.EqualTo("from_factory"));
                Assert.That(callCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task Remove_Clears_Cache()
        {
            var key = new CacheKey("remove.key", 5);
            await _cache.SetAsync(key, "data");
            Assert.That(_cache.Get<string>(key), Is.Not.Null);

            await _cache.RemoveAsync(key);
            Assert.That(_cache.Get<string>(key), Is.Null);
        }

        [Test]
        public async Task RemoveByPrefix_Clears_Matching_Keys()
        {
            var key1 = new CacheKey("testgroup.{0}", "testgroup", "item1");
            var key2 = new CacheKey("testgroup.{0}", "testgroup", "item2");
            var prepared1 = _cache.PrepareKey(key1, "item1");
            var prepared2 = _cache.PrepareKey(key2, "item2");

            await _cache.SetAsync(prepared1, "data1");
            await _cache.SetAsync(prepared2, "data2");

            await _cache.RemoveByPrefixAsync("testgroup");

            Assert.Multiple(() =>
            {
                Assert.That(_cache.Get<string>(prepared1), Is.Null);
                Assert.That(_cache.Get<string>(prepared2), Is.Null);
            });
        }

        [Test]
        public async Task Clear_Removes_All_Keys()
        {
            await _cache.SetAsync(new CacheKey("clear1", 5), "a");
            await _cache.SetAsync(new CacheKey("clear2", 5), "b");

            await _cache.ClearAsync();

            Assert.Multiple(() =>
            {
                Assert.That(_cache.Get<string>(new CacheKey("clear1", 5)), Is.Null);
                Assert.That(_cache.Get<string>(new CacheKey("clear2", 5)), Is.Null);
            });
        }

        [Test]
        public void DistributedLock_Acquire_And_Release()
        {
            bool acquired = _locker.PerformActionWithLock("test.lock", TimeSpan.FromSeconds(10), () =>
            {
                Thread.Sleep(100);
            });
            Assert.That(acquired, Is.True);
        }

        [Test]
        public void DistributedLock_Second_Attempt_Fails()
        {
            var acquired = new AutoResetEvent(false);
            var holdLock = new AutoResetEvent(false);
            bool second = false;

            var bg = Task.Run(() =>
            {
                _locker.PerformActionWithLock("concurrent.lock", TimeSpan.FromSeconds(10), () =>
                {
                    acquired.Set();
                    holdLock.WaitOne(TimeSpan.FromSeconds(5));
                });
            });

            Assert.That(acquired.WaitOne(TimeSpan.FromSeconds(5)), Is.True, "first lock should be acquired");

            second = _locker.PerformActionWithLock("concurrent.lock", TimeSpan.FromSeconds(2), () => { });
            Assert.That(second, Is.False, "second lock should fail while first is held");

            holdLock.Set();
            bg.Wait(TimeSpan.FromSeconds(5));
        }

        [Test]
        public async Task DistributedLock_Acquire_And_Release_Async()
        {
            bool acquired = await _locker.PerformActionWithLockAsync("async.lock", TimeSpan.FromSeconds(10), async () =>
            {
                await Task.Delay(100);
            });
            Assert.That(acquired, Is.True);
        }

        [Test]
        public async Task HybridCache_Set_And_Get()
        {
            var services = new ServiceCollection();
            services.AddCaching(null, new CacheConfiguration
            {
                baseCacheConfiguration = new BaseCacheConfiguration
                {
                    DefaultCacheTime = 10,
                    ShortTermCacheTime = 1
                },
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration
                {
                    Enabled = true,
                    HybridCacheEnabled = true,
                    ConnectionString = "localhost:6379,allowAdmin=true",
                    InstanceName = "HybridIntegration:"
                }
            });
            services.AddHttpContextAccessor();
            services.AddLogging();
            await using var provider = services.BuildServiceProvider();

            var hybridCache = provider.GetRequiredService<ICacheManager>();
            var key = new CacheKey("hybrid.test", 5);
            await hybridCache.SetAsync(key, "hybrid_value");

            var result = hybridCache.Get<string>(key);
            Assert.That(result, Is.EqualTo("hybrid_value"));
        }

        [Test]
        public async Task InstanceName_Scopes_Keys()
        {
            // Use a separate connection with different InstanceName
            var services = new ServiceCollection();
            services.AddCaching(null, new CacheConfiguration
            {
                baseCacheConfiguration = new BaseCacheConfiguration { DefaultCacheTime = 10, ShortTermCacheTime = 1 },
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration
                {
                    Enabled = true,
                    ConnectionString = "localhost:6379,allowAdmin=true",
                    InstanceName = "AppA:"
                }
            });
            services.AddHttpContextAccessor();
            services.AddLogging();
            await using var provider = services.BuildServiceProvider();
            var cacheA = provider.GetRequiredService<ICacheManager>();

            var key = new CacheKey("scoped.key", 5);
            await cacheA.SetAsync(key, "value_a");

            // Query Redis directly: should be "AppA:scoped.key"
            var db = _multiplexer.GetDatabase();
            var exists = db.KeyExists("AppA:scoped.key");
            Assert.That(exists, Is.True);
        }

        public class User
        {
            public string? Name { get; set; }
            public int Age { get; set; }
            public string? Role { get; set; }
        }
    }
}
