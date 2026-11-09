using Base.Caching;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Caching.Test
{
    [TestFixture]
    public class ConsistencyTests
    {
        private HttpContext? _previousContext;

        [SetUp]
        public void SetUp()
        {
            _previousContext = new HttpContextAccessor().HttpContext;
        }

        [TearDown]
        public void TearDown()
        {
            new HttpContextAccessor().HttpContext = _previousContext;
        }

        private static HttpContextAccessor NewAccessor() => new() { HttpContext = new DefaultHttpContext() };

        private static DistributedCacheManager NewFallbackManager(IHttpContextAccessor accessor, IMemoryCache? memoryCache = null)
        {
            return new DistributedCacheManager(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                accessor,
                memoryCache: memoryCache ?? new MemoryCache(new MemoryCacheOptions()));
        }

        private static HybridCacheManager NewHybridManager(out DistributedCacheManager distributed)
        {
            var memory = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            distributed = NewFallbackManager(new HttpContextAccessor(), new MemoryCache(new MemoryCacheOptions()));
            return new HybridCacheManager(memory, distributed);
        }

        [Test]
        public async Task Distributed_GetAsyncTaskAcquire_ZeroCacheTime_IgnoresPerRequestEntry_Test()
        {
            var manager = NewFallbackManager(NewAccessor());
            await manager.SetAsync(new CacheKey("ttl.zero", 10), "cached");

            var result = await manager.GetAsync(new CacheKey("ttl.zero", 0), () => Task.FromResult("fresh"));

            Assert.That(result, Is.EqualTo("fresh"));
        }

        [Test]
        public async Task Distributed_GetAsyncFuncAcquire_ZeroCacheTime_IgnoresPerRequestEntry_Test()
        {
            var manager = NewFallbackManager(NewAccessor());
            await manager.SetAsync(new CacheKey("ttl.zero", 10), "cached");

            var result = await manager.GetAsync(new CacheKey("ttl.zero", 0), () => "fresh");

            Assert.That(result, Is.EqualTo("fresh"));
        }

        [Test]
        public async Task Distributed_GetWithAcquire_ZeroCacheTime_IgnoresPerRequestEntry_Test()
        {
            var manager = NewFallbackManager(NewAccessor());
            await manager.SetAsync(new CacheKey("ttl.zero", 10), "cached");

            var result = manager.Get(new CacheKey("ttl.zero", 0), () => "fresh");

            Assert.That(result, Is.EqualTo("fresh"));
        }

        [Test]
        public async Task Distributed_GetWithoutAcquire_ZeroCacheTime_ReturnsDefaultLikeMemoryManager_Test()
        {
            var distributed = NewFallbackManager(NewAccessor());
            var memory = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            await distributed.SetAsync(new CacheKey("ttl.zero.get", 10), "cached");
            await memory.SetAsync(new CacheKey("ttl.zero.get", 10), "cached");

            Assert.Multiple(() =>
            {
                Assert.That(distributed.Get<string>(new CacheKey("ttl.zero.get", 0)), Is.Null);
                Assert.That(memory.Get<string>(new CacheKey("ttl.zero.get", 0)), Is.Null);
                Assert.That(distributed.Get<string>(new CacheKey("ttl.zero.get", 10)), Is.EqualTo("cached"));
            });
        }

        [Test]
        public async Task Distributed_GetAsyncTaskAcquire_PerRequestEntryOfOtherType_DoesNotThrow_Test()
        {
            var manager = NewFallbackManager(NewAccessor());
            var key = new CacheKey("per.request.type", 10);
            await manager.SetAsync(key, 42);

            var result = await manager.GetAsync(key, () => Task.FromResult("text"));

            Assert.That(result, Is.EqualTo("text"));
        }

        [Test]
        public async Task Distributed_GetAsyncFuncAcquire_PerRequestEntryOfOtherType_DoesNotThrow_Test()
        {
            var manager = NewFallbackManager(NewAccessor());
            var key = new CacheKey("per.request.type", 10);
            await manager.SetAsync(key, 42);

            var result = await manager.GetAsync(key, () => "text");

            Assert.That(result, Is.EqualTo("text"));
        }

        [Test]
        public async Task Distributed_GetWithAcquire_PerRequestEntryOfOtherType_DoesNotThrow_Test()
        {
            var manager = NewFallbackManager(NewAccessor());
            var key = new CacheKey("per.request.type", 10);
            await manager.SetAsync(key, 42);

            var result = manager.Get(key, () => "text");

            Assert.That(result, Is.EqualTo("text"));
        }

        [Test]
        public async Task Distributed_PerRequestEntryOfSameType_IsStillReturned_Test()
        {
            var manager = NewFallbackManager(NewAccessor());
            var key = new CacheKey("per.request.same", 10);
            await manager.SetAsync(key, "cached");

            var acquired = false;
            var result = await manager.GetAsync(key, () => { acquired = true; return Task.FromResult("fresh"); });

            Assert.That(result, Is.EqualTo("cached"));
            Assert.That(acquired, Is.False);
        }

        [Test]
        public void PerRequestCache_RemoveByPrefix_IsOrdinalStartsWith_Test()
        {
            var accessor = NewAccessor();
            var cache = new PerRequestCache(accessor);
            cache.Set("user.1", "a");
            cache.Set("userX1", "b");
            cache.Set("app.user.1", "c");
            cache.Set("USER.1", "d");
            accessor.HttpContext!.Items["unrelated"] = "kept";

            cache.RemoveByPrefix("user.");

            Assert.Multiple(() =>
            {
                Assert.That(cache.IsSet("user.1"), Is.False);
                Assert.That(cache.IsSet("userX1"), Is.True);
                Assert.That(cache.IsSet("app.user.1"), Is.True);
                Assert.That(cache.IsSet("USER.1"), Is.True);
                Assert.That(accessor.HttpContext.Items["unrelated"], Is.EqualTo("kept"));
            });
        }

        [Test]
        public void PerRequestCache_RemoveByPrefix_RegexCharactersAreLiteral_Test()
        {
            var cache = new PerRequestCache(NewAccessor());
            cache.Set("price(usd).1", "a");
            cache.Set("tag[1].x", "b");
            cache.Set("priceusd.1", "c");

            Assert.DoesNotThrow(() => cache.RemoveByPrefix("tag[1"));
            cache.RemoveByPrefix("price(usd)");

            Assert.Multiple(() =>
            {
                Assert.That(cache.IsSet("price(usd).1"), Is.False);
                Assert.That(cache.IsSet("tag[1].x"), Is.False);
                Assert.That(cache.IsSet("priceusd.1"), Is.True);
            });
        }

        [Test]
        public async Task Distributed_RemoveByPrefixAsync_PerRequestUsesLiteralPrefix_Test()
        {
            var accessor = NewAccessor();
            var manager = NewFallbackManager(accessor);
            await manager.SetAsync(new CacheKey("order.1", 10), "a");
            await manager.SetAsync(new CacheKey("orderX1", 10), "b");

            await manager.RemoveByPrefixAsync("order.");

            var perRequest = new PerRequestCache(accessor);
            Assert.That(perRequest.IsSet("order.1"), Is.False);
            Assert.That(perRequest.IsSet("orderX1"), Is.True);
        }

        [Test]
        public async Task Hybrid_GetAsyncTaskAcquire_ValueTypeMiss_CallsAcquire_Test()
        {
            var manager = NewHybridManager(out _);

            var result = await manager.GetAsync(new CacheKey("hybrid.int.miss", 10), () => Task.FromResult(5));

            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public async Task Hybrid_GetAsyncFuncAcquire_ValueTypeMiss_CallsAcquire_Test()
        {
            var manager = NewHybridManager(out _);

            var result = await manager.GetAsync(new CacheKey("hybrid.int.miss", 10), () => 5);

            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public void Hybrid_GetWithAcquire_ValueTypeMiss_CallsAcquire_Test()
        {
            var manager = NewHybridManager(out _);

            var result = manager.Get(new CacheKey("hybrid.int.miss", 10), () => 5);

            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public async Task Hybrid_CachedDefaultValue_IsAHit_Test()
        {
            var manager = NewHybridManager(out _);
            var key = new CacheKey("hybrid.int.zero", 10);
            await manager.SetAsync(key, 0);

            var acquireCalls = 0;
            var asyncResult = await manager.GetAsync(key, () => { acquireCalls++; return Task.FromResult(5); });
            var syncResult = manager.Get(key, () => { acquireCalls++; return 5; });

            Assert.Multiple(() =>
            {
                Assert.That(asyncResult, Is.EqualTo(0));
                Assert.That(syncResult, Is.EqualTo(0));
                Assert.That(acquireCalls, Is.EqualTo(0));
            });
        }

        [Test]
        public async Task Hybrid_GetWithoutAcquire_ValueTypeOnlyInL2_IsReturned_Test()
        {
            var manager = NewHybridManager(out var distributed);
            var key = new CacheKey("hybrid.int.l2", 10);
            await distributed.SetAsync(key, 7);

            Assert.That(manager.Get<int>(key), Is.EqualTo(7));
        }

        [Test]
        public async Task Hybrid_GetWithoutAcquire_ValueTypeMiss_DoesNotCacheDefault_Test()
        {
            var manager = NewHybridManager(out _);
            var key = new CacheKey("hybrid.int.nothing", 10);

            var missing = manager.Get<int>(key);
            var result = await manager.GetAsync(key, () => Task.FromResult(5));

            Assert.That(missing, Is.EqualTo(0));
            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public async Task Hybrid_ReferenceType_HitAndMiss_Unchanged_Test()
        {
            var manager = NewHybridManager(out _);
            var key = new CacheKey("hybrid.string", 10);

            var first = await manager.GetAsync(key, () => Task.FromResult("first"));
            var second = await manager.GetAsync(key, () => Task.FromResult("second"));

            Assert.That(first, Is.EqualTo("first"));
            Assert.That(second, Is.EqualTo("first"));
        }

        [Test]
        public async Task Memory_RemoveByPrefixAsync_MatchesKeyPrefixTagsNotKeyName_Test()
        {
            var manager = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            await AssertDocumentedMemoryPrefixSemanticsAsync(manager, manager.Get<string>);
        }

        [Test]
        public async Task DistributedFallback_RemoveByPrefixAsync_MatchesKeyPrefixTagsNotKeyName_Test()
        {
            var manager = NewFallbackManager(new HttpContextAccessor());
            await AssertDocumentedMemoryPrefixSemanticsAsync(manager, manager.Get<string>);
        }

        private static async Task AssertDocumentedMemoryPrefixSemanticsAsync(Base.Caching.Interfaces.ICacheManager manager, Func<CacheKey, string> get)
        {
            var quickStart = manager.PrepareKey(new CacheKey("user.{0}", 10, "users"), 1);
            var untaggedWithMatchingName = new CacheKey("users.list", 10);
            var documented = manager.PrepareKey(new CacheKey("user.{0}", 10, "user."), 2);

            await manager.SetAsync(quickStart, "quick");
            await manager.SetAsync(untaggedWithMatchingName, "list");
            await manager.SetAsync(documented, "documented");

            await manager.RemoveByPrefixAsync("users");

            Assert.That(get(quickStart), Is.Null);
            Assert.That(get(untaggedWithMatchingName), Is.EqualTo("list"));
            Assert.That(get(documented), Is.EqualTo("documented"));

            await manager.RemoveByPrefixAsync("user.");

            Assert.That(get(documented), Is.Null);
            Assert.That(get(untaggedWithMatchingName), Is.EqualTo("list"));
        }
    }
}
