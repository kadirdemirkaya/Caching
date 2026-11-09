using System.Reflection;
using Base.Caching.Configurations;
using Base.Caching.Managers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Caching.Test
{
    [TestFixture]
    public class HybridInvalidationChannelTests
    {
        [Test]
        public async Task HybridCache_InvalidationChannel_IsLiteral_Test()
        {
            var subscriber = DispatchProxy.Create<ISubscriber, RecordingProxy>();
            var subscriberRecorder = (RecordingProxy)(object)subscriber;
            var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, RecordingProxy>();
            var multiplexerRecorder = (RecordingProxy)(object)multiplexer;
            multiplexerRecorder.Results["get_IsConnected"] = true;
            multiplexerRecorder.Results["GetSubscriber"] = subscriber;

            var memory = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            var distributed = new DistributedCacheManager(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                new HttpContextAccessor());
            var configuration = new BaseDistributedCacheConfiguration { InstanceName = "Tenant*" };

            var manager = new HybridCacheManager(memory, distributed, multiplexer, configuration);
            await manager.ClearAsync();

            var subscribe = subscriberRecorder.Calls.Single(c => c.Method == "Subscribe");
            var publish = subscriberRecorder.Calls.Single(c => c.Method == "Publish");
            var subscribedChannel = (RedisChannel)subscribe.Args[0]!;
            var publishedChannel = (RedisChannel)publish.Args[0]!;

            Assert.Multiple(() =>
            {
                Assert.That(subscribedChannel.ToString(), Is.EqualTo("Tenant*:InvalidationChannel"));
                Assert.That(subscribedChannel.IsPattern, Is.False);
                Assert.That(publishedChannel.ToString(), Is.EqualTo("Tenant*:InvalidationChannel"));
                Assert.That(publishedChannel.IsPattern, Is.False);
            });
        }

        public class RecordingProxy : DispatchProxy
        {
            public List<(string Method, object?[] Args)> Calls { get; } = new();

            public Dictionary<string, object> Results { get; } = new();

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var name = targetMethod!.Name;
                Calls.Add((name, args ?? Array.Empty<object?>()));

                if (Results.TryGetValue(name, out var result))
                    return result;

                var returnType = targetMethod.ReturnType;
                return returnType.IsValueType && returnType != typeof(void) ? Activator.CreateInstance(returnType) : null;
            }
        }
    }
}
