using System.Text.Json;
using Base.Caching.Configurations;
using Base.Caching.Interfaces;
using Base.Caching.Key;
using Base.Caching.Managers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using RecordingProxy = Caching.Test.HybridInvalidationChannelTests.RecordingProxy;

namespace Caching.Test
{
    [TestFixture]
    public class HybridSubscriptionTests
    {
        private RecordingProxy _subscriberRecorder = null!;
        private ServiceProvider _provider = null!;

        [SetUp]
        public void SetUp()
        {
            var subscriber = System.Reflection.DispatchProxy.Create<ISubscriber, RecordingProxy>();
            _subscriberRecorder = (RecordingProxy)(object)subscriber;
            var multiplexer = System.Reflection.DispatchProxy.Create<IConnectionMultiplexer, RecordingProxy>();
            var multiplexerRecorder = (RecordingProxy)(object)multiplexer;
            multiplexerRecorder.Results["get_IsConnected"] = true;
            multiplexerRecorder.Results["GetSubscriber"] = subscriber;

            var services = new ServiceCollection();
            services.AddMemoryCache();
            services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
            services.AddSingleton(new BaseDistributedCacheConfiguration { InstanceName = "Scopes" });
            services.AddSingleton(multiplexer);
            services.AddSingleton<MemoryCacheManager>();
            services.AddScoped(sp => new DistributedCacheManager(sp.GetRequiredService<IDistributedCache>(), new HttpContextAccessor()));
            services.AddScoped<ICacheManager, HybridCacheManager>();
            _provider = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown()
        {
            _provider.Dispose();
        }

        private List<ICacheManager> ResolveInScopes(int count)
        {
            var managers = new List<ICacheManager>();
            for (var i = 0; i < count; i++)
                managers.Add(_provider.CreateScope().ServiceProvider.GetRequiredService<ICacheManager>());
            return managers;
        }

        private List<Action<RedisChannel, RedisValue>> Handlers()
        {
            return _subscriberRecorder.Calls
                .Where(c => c.Method == "Subscribe")
                .Select(c => (Action<RedisChannel, RedisValue>)c.Args[1]!)
                .ToList();
        }

        private void Deliver(string payload)
        {
            foreach (var handler in Handlers())
                handler(RedisChannel.Literal("Scopes:InvalidationChannel"), payload);
        }

        private static string RemoteMessage(string action, string target)
        {
            return JsonSerializer.Serialize(new { Action = action, Target = target, OriginatorId = "another-process" });
        }

        [Test]
        public void HybridCache_ManyScopes_SubscribeOnce_Test()
        {
            var managers = ResolveInScopes(5);

            Assert.That(managers.Distinct().Count(), Is.EqualTo(5));
            Assert.That(_subscriberRecorder.Calls.Count(c => c.Method == "Subscribe"), Is.EqualTo(1));
        }

        [Test]
        public async Task HybridCache_OwnMessageFromAnotherScope_IsIgnored_Test()
        {
            var managers = ResolveInScopes(2);
            var memory = _provider.GetRequiredService<MemoryCacheManager>();
            var key = new CacheKey("own:message:key", 10);

            await managers[1].RemoveAsync(key);
            await memory.SetAsync(key, "kept");

            var published = _subscriberRecorder.Calls.Single(c => c.Method == "Publish");
            Deliver(((RedisValue)published.Args[1]!).ToString());

            Assert.That(memory.Get<string>(key), Is.EqualTo("kept"));
        }

        [Test]
        public async Task HybridCache_RemoteRemove_KeyWithBraces_IsRemoved_Test()
        {
            ResolveInScopes(1);
            var memory = _provider.GetRequiredService<MemoryCacheManager>();
            var key = new CacheKey("product:{1}:{name}", 10);
            await memory.SetAsync(key, "stale");

            Deliver(RemoteMessage("Remove", key.Key));

            Assert.That(memory.Get<string>(key), Is.Null);
        }

        [Test]
        public async Task HybridCache_RemoteRemoveByPrefix_PrefixWithBraces_IsRemoved_Test()
        {
            ResolveInScopes(1);
            var memory = _provider.GetRequiredService<MemoryCacheManager>();
            var key = new CacheKey("tenant:{a}:item", 10, "tenant:{a}");
            await memory.SetAsync(key, "stale");

            Deliver(RemoteMessage("RemoveByPrefix", "tenant:{a}"));

            Assert.That(memory.Get<string>(key), Is.Null);
        }

        [Test]
        public async Task HybridCache_RemoteClear_ClearsLocalMemory_Test()
        {
            ResolveInScopes(3);
            var memory = _provider.GetRequiredService<MemoryCacheManager>();
            var key = new CacheKey("clear:me", 10);
            await memory.SetAsync(key, "stale");

            Deliver(RemoteMessage("Clear", string.Empty));

            Assert.That(memory.Get<string>(key), Is.Null);
        }
    }
}
