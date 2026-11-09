using Base.Caching;
using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Caching.Test
{
    [TestFixture]
    [NonParallelizable]
    public class ConfiguredCacheTimeTests
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

        private static ServiceProvider BuildProvider(BaseCacheConfiguration cacheConfiguration)
        {
            var services = new ServiceCollection();
            services.AddCaching(null!, new CacheConfiguration
            {
                baseCacheConfiguration = cacheConfiguration,
                baseDistributedCacheConfiguration = new BaseDistributedCacheConfiguration { Enabled = false }
            });
            return services.BuildServiceProvider();
        }

        private static ServiceProvider BuildProviderFromConfiguration(Dictionary<string, string?> values)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            services.AddCaching(configuration);
            return services.BuildServiceProvider();
        }

        [Test]
        public void ConfiguredTimes_AreUsedByPrepareKeyForDefaultAndShortTermCache_Test()
        {
            using var provider = BuildProvider(new BaseCacheConfiguration { DefaultCacheTime = 60, ShortTermCacheTime = 3 });
            var manager = provider.GetRequiredService<ICacheManager>();

            var defaultKey = manager.PrepareKeyForDefaultCache(new CacheKey("configured.default"));
            var shortKey = manager.PrepareKeyForShortTermCache(new CacheKey("configured.short"));

            Assert.Multiple(() =>
            {
                Assert.That(defaultKey.CacheTime, Is.EqualTo(60));
                Assert.That(shortKey.CacheTime, Is.EqualTo(3));
            });
        }

        [Test]
        public void ConfiguredTimes_FromConfigurationSection_AreUsed_Test()
        {
            using var provider = BuildProviderFromConfiguration(new Dictionary<string, string?>
            {
                ["Caching:Cache:DefaultCacheTime"] = "60",
                ["Caching:Cache:ShortTermCacheTime"] = "3",
                ["Caching:DistributedCache:Enabled"] = "false"
            });
            var manager = provider.GetRequiredService<ICacheManager>();

            Assert.Multiple(() =>
            {
                Assert.That(manager.PrepareKeyForDefaultCache(new CacheKey("section.default")).CacheTime, Is.EqualTo(60));
                Assert.That(manager.PrepareKeyForShortTermCache(new CacheKey("section.short")).CacheTime, Is.EqualTo(3));
                Assert.That(new CacheKey("section.plain").CacheTime, Is.EqualTo(60));
            });
        }

        [Test]
        public void ConfiguredDefaultTime_AppliesToKeyWithoutExplicitTime_Test()
        {
            var keyCreatedBeforeRegistration = new CacheKey("configured.early", "configured");

            using var provider = BuildProvider(new BaseCacheConfiguration { DefaultCacheTime = 60, ShortTermCacheTime = 3 });
            var manager = provider.GetRequiredService<ICacheManager>();

            Assert.Multiple(() =>
            {
                Assert.That(new CacheKey("configured.plain").CacheTime, Is.EqualTo(60));
                Assert.That(CacheKey.Create("configured.created").CacheTime, Is.EqualTo(60));
                Assert.That(keyCreatedBeforeRegistration.CacheTime, Is.EqualTo(60));
                Assert.That(manager.PrepareKey(keyCreatedBeforeRegistration).CacheTime, Is.EqualTo(60));
            });
        }

        [Test]
        public void MissingOrZeroTimes_KeepBuiltInDefaults_Test()
        {
            using var provider = BuildProvider(new BaseCacheConfiguration());
            var manager = provider.GetRequiredService<ICacheManager>();

            Assert.Multiple(() =>
            {
                Assert.That(manager.PrepareKeyForDefaultCache(new CacheKey("unconfigured.default")).CacheTime, Is.EqualTo(120));
                Assert.That(manager.PrepareKeyForShortTermCache(new CacheKey("unconfigured.short")).CacheTime, Is.EqualTo(1));
                Assert.That(new CacheKey("unconfigured.plain").CacheTime, Is.EqualTo(120));
            });
        }

        [Test]
        public void MissingTimesInConfigurationSection_KeepBuiltInDefaults_Test()
        {
            using var provider = BuildProviderFromConfiguration(new Dictionary<string, string?>
            {
                ["Caching:Cache:Unused"] = "x",
                ["Caching:DistributedCache:Enabled"] = "false"
            });
            var manager = provider.GetRequiredService<ICacheManager>();

            Assert.Multiple(() =>
            {
                Assert.That(manager.PrepareKeyForDefaultCache(new CacheKey("section.missing.default")).CacheTime, Is.EqualTo(120));
                Assert.That(manager.PrepareKeyForShortTermCache(new CacheKey("section.missing.short")).CacheTime, Is.EqualTo(1));
            });
        }

        [Test]
        public void OnlyPositiveConfiguredTime_IsApplied_Test()
        {
            using var provider = BuildProvider(new BaseCacheConfiguration { DefaultCacheTime = 0, ShortTermCacheTime = 5 });
            var manager = provider.GetRequiredService<ICacheManager>();

            Assert.Multiple(() =>
            {
                Assert.That(manager.PrepareKeyForDefaultCache(new CacheKey("partial.default")).CacheTime, Is.EqualTo(120));
                Assert.That(manager.PrepareKeyForShortTermCache(new CacheKey("partial.short")).CacheTime, Is.EqualTo(5));
            });
        }

        [Test]
        public void ExplicitExpireMinute_AlwaysWins_Test()
        {
            using var provider = BuildProvider(new BaseCacheConfiguration { DefaultCacheTime = 60, ShortTermCacheTime = 3 });
            var manager = provider.GetRequiredService<ICacheManager>();
            var keyService = provider.GetRequiredService<MemoryCacheManager>();
            var explicitKey = new CacheKey("explicit.key{0}", 7, "explicit");

            Assert.Multiple(() =>
            {
                Assert.That(explicitKey.CacheTime, Is.EqualTo(7));
                Assert.That(CacheKey.Create("explicit.created", 9).CacheTime, Is.EqualTo(9));
                Assert.That(manager.PrepareKey(explicitKey, "1").CacheTime, Is.EqualTo(7));
                Assert.That(keyService.PrepareKey(new CacheKey("explicit.override"), 15).CacheTime, Is.EqualTo(15));
            });
        }
    }
}
