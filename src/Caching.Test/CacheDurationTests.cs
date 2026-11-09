using Base.Caching;
using Base.Caching.Configurations;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Caching.Test
{
    [TestFixture]
    [NonParallelizable]
    public class CacheDurationTests
    {
        private int _savedCacheTime;
        private int _savedShortCacheTime;

        [SetUp]
        public void SetUp()
        {
            _savedCacheTime = CachingDefaults.CacheTime;
            _savedShortCacheTime = CachingDefaults.ShortCacheTime;
        }

        [TearDown]
        public void TearDown()
        {
            CachingDefaults.CacheTime = _savedCacheTime;
            CachingDefaults.ShortCacheTime = _savedShortCacheTime;
        }

        [TestCase(30, 1)]
        [TestCase(90, 2)]
        [TestCase(300, 5)]
        [TestCase(0, 0)]
        [TestCase(-30, 0)]
        [TestCase(-90, -1)]
        public void TimeSpanConstructor_SetsDuration_AndCacheTimeRoundsUp_Test(int seconds, int expectedMinutes)
        {
            var key = new CacheKey("duration.ctor", TimeSpan.FromSeconds(seconds), "duration");

            Assert.Multiple(() =>
            {
                Assert.That(key.CacheDuration, Is.EqualTo(TimeSpan.FromSeconds(seconds)));
                Assert.That(key.CacheTime, Is.EqualTo(expectedMinutes));
                Assert.That(key.Prefixes, Is.EqualTo(new[] { "duration" }));
            });
        }

        [Test]
        public void TimeSpanCreate_SetsDurationAndPrefixes_Test()
        {
            var key = CacheKey.Create("duration.create", TimeSpan.FromMinutes(90), "a", "", "b");

            Assert.Multiple(() =>
            {
                Assert.That(key.CacheDuration, Is.EqualTo(TimeSpan.FromMinutes(90)));
                Assert.That(key.CacheTime, Is.EqualTo(90));
                Assert.That(key.Prefixes, Is.EqualTo(new[] { "a", "b" }));
                Assert.That(key.SlidingExpiration, Is.Null);
            });
        }

        [Test]
        public void ExtremeDurations_ClampCacheTime_Test()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new CacheKey("max", TimeSpan.MaxValue).CacheTime, Is.EqualTo(int.MaxValue));
                Assert.That(new CacheKey("min", TimeSpan.MinValue).CacheTime, Is.EqualTo(int.MinValue));
            });
        }

        [TestCase(5)]
        [TestCase(0)]
        [TestCase(-3)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void MinuteApi_RoundTripsAndSetsDuration_Test(int minutes)
        {
            var fromConstructor = new CacheKey("minutes.ctor", minutes);
            var fromSetter = new CacheKey("minutes.setter") { CacheTime = minutes };

            Assert.Multiple(() =>
            {
                Assert.That(fromConstructor.CacheTime, Is.EqualTo(minutes));
                Assert.That(fromConstructor.CacheDuration, Is.EqualTo(TimeSpan.FromMinutes(minutes)));
                Assert.That(fromSetter.CacheTime, Is.EqualTo(minutes));
                Assert.That(fromSetter.CacheDuration, Is.EqualTo(TimeSpan.FromMinutes(minutes)));
            });
        }

        [Test]
        public void KeyWithoutExplicitTime_DurationFollowsDefault_Test()
        {
            var key = new CacheKey("duration.default");

            CachingDefaults.CacheTime = 33;

            Assert.Multiple(() =>
            {
                Assert.That(key.CacheTime, Is.EqualTo(33));
                Assert.That(key.CacheDuration, Is.EqualTo(TimeSpan.FromMinutes(33)));
            });
        }

        [Test]
        public void PrepareKey_KeepsDurationAndSlidingExpiration_Test()
        {
            var manager = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            var key = new CacheKey("duration.user.{0}", TimeSpan.FromSeconds(45), "duration.user.{0}")
            {
                SlidingExpiration = TimeSpan.FromSeconds(10)
            };

            var prepared = manager.PrepareKey(key, "7");
            var overridden = manager.PrepareKey(key, 15, "7");

            Assert.Multiple(() =>
            {
                Assert.That(prepared.Key, Is.EqualTo("duration.user.7"));
                Assert.That(prepared.CacheDuration, Is.EqualTo(TimeSpan.FromSeconds(45)));
                Assert.That(prepared.SlidingExpiration, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(overridden.CacheDuration, Is.EqualTo(TimeSpan.FromMinutes(15)));
                Assert.That(overridden.SlidingExpiration, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(key.Key, Is.EqualTo("duration.user.{0}"));
            });
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void SlidingExpiration_RejectsNonPositive_Test(int seconds)
        {
            var key = new CacheKey("sliding.invalid");

            Assert.Throws<ArgumentOutOfRangeException>(() => key.SlidingExpiration = TimeSpan.FromSeconds(seconds));
            Assert.That(key.SlidingExpiration, Is.Null);
        }

        [Test]
        public void SlidingExpiration_AcceptsNull_Test()
        {
            var key = new CacheKey("sliding.null") { SlidingExpiration = TimeSpan.FromSeconds(5) };

            key.SlidingExpiration = null;

            Assert.That(key.SlidingExpiration, Is.Null);
        }

        private static CacheKey SubMinuteSlidingKey(string name) => new(name, TimeSpan.FromSeconds(30)) { SlidingExpiration = TimeSpan.FromSeconds(5) };

        [Test]
        public async Task Memory_SetAsync_UsesDurationAndSlidingExpiration_Test()
        {
            var memory = new RecordingMemoryCache();
            var manager = new MemoryCacheManager(memory);

            await manager.SetAsync(SubMinuteSlidingKey("memory.set"), "v");
            await manager.GetAsync(SubMinuteSlidingKey("memory.getorcreate"), () => "v");
            await manager.SetAsync(new CacheKey("memory.minutes", 5), "v");

            Assert.Multiple(() =>
            {
                foreach (var name in new[] { "memory.set", "memory.getorcreate" })
                {
                    Assert.That(memory.LastEntry(name).AbsoluteExpirationRelativeToNow, Is.EqualTo(TimeSpan.FromSeconds(30)), name);
                    Assert.That(memory.LastEntry(name).SlidingExpiration, Is.EqualTo(TimeSpan.FromSeconds(5)), name);
                }

                Assert.That(memory.LastEntry("memory.minutes").AbsoluteExpirationRelativeToNow, Is.EqualTo(TimeSpan.FromMinutes(5)));
                Assert.That(memory.LastEntry("memory.minutes").SlidingExpiration, Is.Null);
            });
        }

        [Test]
        public async Task DistributedFallback_SetAsync_UsesDurationAndSlidingExpiration_Test()
        {
            var memory = new RecordingMemoryCache();
            var manager = new DistributedCacheManager(new RecordingDistributedCache(), new HttpContextAccessor(), memoryCache: memory);

            await manager.SetAsync(SubMinuteSlidingKey("fallback.set"), "v");

            Assert.Multiple(() =>
            {
                Assert.That(memory.LastEntry("fallback.set").AbsoluteExpirationRelativeToNow, Is.EqualTo(TimeSpan.FromSeconds(30)));
                Assert.That(memory.LastEntry("fallback.set").SlidingExpiration, Is.EqualTo(TimeSpan.FromSeconds(5)));
            });
        }

        [Test]
        public async Task DistributedConnected_SetAsync_PassesDurationAndSlidingExpirationToStore_Test()
        {
            var store = new RecordingDistributedCache();
            var manager = new DistributedCacheManager(store, new HttpContextAccessor(), new ConnectedFakeRedis().Multiplexer,
                new BaseDistributedCacheConfiguration { InstanceName = "App:" });

            await manager.SetAsync(SubMinuteSlidingKey("connected.set"), "v");
            await manager.SetAsync(new CacheKey("connected.minutes", 5), "v");

            Assert.Multiple(() =>
            {
                Assert.That(store.SetOptions[0].AbsoluteExpirationRelativeToNow, Is.EqualTo(TimeSpan.FromSeconds(30)));
                Assert.That(store.SetOptions[0].SlidingExpiration, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(store.SetOptions[1].AbsoluteExpirationRelativeToNow, Is.EqualTo(TimeSpan.FromMinutes(5)));
                Assert.That(store.SetOptions[1].SlidingExpiration, Is.Null);
            });
        }

        [Test]
        public async Task Memory_SubMinuteDuration_IsCachedAndExpires_Test()
        {
            var manager = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            var key = new CacheKey("memory.short", TimeSpan.FromMilliseconds(300));

            await manager.SetAsync(key, "v");
            var immediately = manager.Get<string>(key);
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            var later = manager.Get<string>(key);

            Assert.Multiple(() =>
            {
                Assert.That(immediately, Is.EqualTo("v"));
                Assert.That(later, Is.Null);
            });
        }

        [Test]
        public async Task ZeroDuration_DisablesCaching_Test()
        {
            var manager = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            var key = new CacheKey("memory.zero", TimeSpan.Zero);
            var calls = 0;

            await manager.GetAsync(key, () => Task.FromResult(++calls));
            await manager.GetAsync(key, () => Task.FromResult(++calls));

            Assert.That(calls, Is.EqualTo(2));
        }
    }
}
