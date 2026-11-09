using Base.Caching;
using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Caching.Test
{
    [TestFixture]
    [NonParallelizable]
    public class AddCachingRegistrationTests
    {
        private int _savedCacheTime;
        private int _savedShortCacheTime;

        [SetUp]
        public void SetUp()
        {
            _savedCacheTime = CachingDefaults.CacheTime;
            _savedShortCacheTime = CachingDefaults.ShortCacheTime;
            CachingDefaults.CacheTime = 120;
            CachingDefaults.ShortCacheTime = 1;
        }

        [TearDown]
        public void TearDown()
        {
            CachingDefaults.CacheTime = _savedCacheTime;
            CachingDefaults.ShortCacheTime = _savedShortCacheTime;
        }

        private static IConfiguration Configuration(Dictionary<string, string?> values)
            => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        private static ServiceDescriptor Single<TService>(IServiceCollection services)
            => services.Single(d => d.ServiceType == typeof(TService));

        private static List<(Type ServiceType, ServiceLifetime Lifetime)> Registrations(IServiceCollection services)
            => services.Select(d => (d.ServiceType, d.Lifetime)).OrderBy(r => r.ServiceType.FullName).ThenBy(r => r.Lifetime).ToList();

        [Test]
        public void Configuration_WithoutDistributedSection_RegistersMemoryManager_Test()
        {
            var services = new ServiceCollection();

            services.AddCaching(Configuration(new Dictionary<string, string?> { ["Caching:Cache:DefaultCacheTime"] = "45" }));
            using var provider = services.BuildServiceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<ICacheManager>(), Is.InstanceOf<MemoryCacheManager>());
                Assert.That(provider.GetRequiredService<ICacheLocker>(), Is.InstanceOf<MemoryCacheManager>());
                Assert.That(provider.GetRequiredService<BaseDistributedCacheConfiguration>().Enabled, Is.False);
                Assert.That(services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer)), Is.False);
                Assert.That(CachingDefaults.CacheTime, Is.EqualTo(45));
            });
        }

        [Test]
        public void Configuration_WithoutCacheSection_ThrowsInvalidOperationNamingTheSection_Test()
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<InvalidOperationException>(() => services.AddCaching(Configuration(new Dictionary<string, string?>
            {
                ["Caching:DistributedCache:Enabled"] = "false"
            })));

            Assert.That(exception!.Message, Does.Contain("'Caching:Cache'"));
            Assert.That(services, Is.Empty);
        }

        [Test]
        public void Configuration_EnabledWithoutConnectionString_ThrowsInvalidOperation_Test()
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<InvalidOperationException>(() => services.AddCaching(Configuration(new Dictionary<string, string?>
            {
                ["Caching:Cache:DefaultCacheTime"] = "45",
                ["Caching:DistributedCache:Enabled"] = "true"
            })));

            Assert.That(exception!.Message, Does.Contain("Caching:DistributedCache:ConnectionString"));
            Assert.That(services, Is.Empty);
        }

        [Test]
        public void Configuration_Null_ThrowsArgumentNull_Test()
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<ArgumentNullException>(() => services.AddCaching((IConfiguration)null!));

            Assert.That(exception!.ParamName, Is.EqualTo("configuration"));
        }

        [Test]
        public void CacheConfiguration_WithoutDistributedSettings_RegistersMemoryManager_Test()
        {
            var services = new ServiceCollection();

            services.AddCaching(null, new CacheConfiguration { baseCacheConfiguration = new BaseCacheConfiguration() });
            using var provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<ICacheManager>(), Is.InstanceOf<MemoryCacheManager>());
        }

        [Test]
        public void CacheConfiguration_WithoutCacheSettings_ThrowsArgumentException_Test()
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<ArgumentException>(() => services.AddCaching(null, new CacheConfiguration
            {
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration()
            }));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.ParamName, Is.EqualTo("cacheConfiguration"));
                Assert.That(exception.Message, Does.Contain("baseCacheConfiguration"));
            });
        }

        [Test]
        public void CacheConfiguration_EnabledWithoutConnectionString_ThrowsArgumentException_Test()
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<ArgumentException>(() => services.AddCaching(null, new CacheConfiguration
            {
                baseCacheConfiguration = new BaseCacheConfiguration(),
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration { Enabled = true, ConnectionString = " " }
            }));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.ParamName, Is.EqualTo("cacheConfiguration"));
                Assert.That(exception.Message, Does.Contain("ConnectionString"));
            });
        }

        [Test]
        public void CacheConfiguration_Null_ThrowsArgumentNull_Test()
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<ArgumentNullException>(() => services.AddCaching(null, null!));

            Assert.That(exception!.ParamName, Is.EqualTo("cacheConfiguration"));
        }

        [Test]
        public void Options_Empty_RegistersMemoryManagerWithBuiltInTimes_Test()
        {
            var services = new ServiceCollection();

            services.AddCaching(_ => { });
            using var provider = services.BuildServiceProvider();
            var manager = provider.GetRequiredService<ICacheManager>();

            Assert.Multiple(() =>
            {
                Assert.That(manager, Is.InstanceOf<MemoryCacheManager>());
                Assert.That(manager.PrepareKeyForDefaultCache(new CacheKey("options.default")).CacheTime, Is.EqualTo(120));
                Assert.That(manager.PrepareKeyForShortTermCache(new CacheKey("options.short")).CacheTime, Is.EqualTo(1));
            });
        }

        [Test]
        public void Options_CacheTimes_AreAppliedAndRegistered_Test()
        {
            var services = new ServiceCollection();
            CacheOptions? configured = null;

            services.AddCaching(options =>
            {
                configured = options;
                options.Cache.DefaultCacheTime = 45;
                options.Cache.ShortTermCacheTime = 4;
            });
            using var provider = services.BuildServiceProvider();
            var manager = provider.GetRequiredService<ICacheManager>();

            Assert.Multiple(() =>
            {
                Assert.That(manager.PrepareKeyForDefaultCache(new CacheKey("options.default")).CacheTime, Is.EqualTo(45));
                Assert.That(manager.PrepareKeyForShortTermCache(new CacheKey("options.short")).CacheTime, Is.EqualTo(4));
                Assert.That(provider.GetRequiredService<BaseCacheConfiguration>(), Is.SameAs(configured!.Cache));
                Assert.That(provider.GetRequiredService<BaseDistributedCacheConfiguration>(), Is.SameAs(configured.DistributedCache));
            });
        }

        [Test]
        public void Options_DistributedEnabled_RegistersDistributedManagerWithoutConnecting_Test()
        {
            var services = new ServiceCollection();

            services.AddCaching(options =>
            {
                options.DistributedCache.Enabled = true;
                options.DistributedCache.ConnectionString = "127.0.0.1:9999,abortConnect=false";
                options.DistributedCache.InstanceName = "App:";
            });

            Assert.Multiple(() =>
            {
                Assert.That(Single<IConnectionMultiplexer>(services).Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
                Assert.That(Single<ICacheManager>(services).Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
                Assert.That(Single<ICacheManager>(services).ImplementationType, Is.Null, "resolved through DistributedCacheManager");
            });
        }

        [Test]
        public void Options_HybridEnabled_RegistersHybridManager_Test()
        {
            var services = new ServiceCollection();

            services.AddCaching(options =>
            {
                options.DistributedCache.Enabled = true;
                options.DistributedCache.HybridCacheEnabled = true;
                options.DistributedCache.ConnectionString = "127.0.0.1:9999,abortConnect=false";
            });

            Assert.Multiple(() =>
            {
                Assert.That(Single<ICacheManager>(services).ImplementationType, Is.EqualTo(typeof(HybridCacheManager)));
                Assert.That(Single<ICacheLocker>(services).ImplementationType, Is.EqualTo(typeof(HybridCacheManager)));
            });
        }

        [Test]
        public void Options_EnabledWithoutConnectionString_ThrowsArgumentException_Test()
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<ArgumentException>(() => services.AddCaching(options => options.DistributedCache.Enabled = true));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.ParamName, Is.EqualTo("configure"));
                Assert.That(exception.Message, Does.Contain("ConnectionString"));
                Assert.That(services, Is.Empty);
            });
        }

        [Test]
        public void Options_NullSections_ThrowArgumentException_Test()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new ServiceCollection().AddCaching(options => options.Cache = null!),
                    Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("configure"));
                Assert.That(() => new ServiceCollection().AddCaching(options => options.DistributedCache = null!),
                    Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("configure"));
            });
        }

        [Test]
        public void Options_NullAction_ThrowsArgumentNull_Test()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddCaching((Action<CacheOptions>)null!));

            Assert.That(exception!.ParamName, Is.EqualTo("configure"));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AllOverloads_RegisterTheSameServices_Test(bool enabled, bool hybrid)
        {
            const string connectionString = "127.0.0.1:9999,abortConnect=false";

            var fromConfiguration = new ServiceCollection();
            fromConfiguration.AddCaching(Configuration(new Dictionary<string, string?>
            {
                ["Caching:Cache:DefaultCacheTime"] = "60",
                ["Caching:DistributedCache:Enabled"] = enabled.ToString(),
                ["Caching:DistributedCache:HybridCacheEnabled"] = hybrid.ToString(),
                ["Caching:DistributedCache:ConnectionString"] = connectionString
            }));

            var fromObject = new ServiceCollection();
            fromObject.AddCaching(null, new CacheConfiguration
            {
                baseCacheConfiguration = new BaseCacheConfiguration { DefaultCacheTime = 60 },
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration
                {
                    Enabled = enabled,
                    HybridCacheEnabled = hybrid,
                    ConnectionString = connectionString
                }
            });

            var fromOptions = new ServiceCollection();
            fromOptions.AddCaching(options =>
            {
                options.Cache.DefaultCacheTime = 60;
                options.DistributedCache.Enabled = enabled;
                options.DistributedCache.HybridCacheEnabled = hybrid;
                options.DistributedCache.ConnectionString = connectionString;
            });

            Assert.Multiple(() =>
            {
                Assert.That(Registrations(fromObject), Is.EqualTo(Registrations(fromConfiguration)));
                Assert.That(Registrations(fromOptions), Is.EqualTo(Registrations(fromConfiguration)));
            });
        }
    }
}
