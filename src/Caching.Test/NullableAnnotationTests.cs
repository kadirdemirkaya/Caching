using Base.Caching;
using Base.Caching.Configurations;
using Base.Caching.Helper;
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
    public class NullableAnnotationTests
    {
        private static readonly NullabilityInfoContext Nullability = new();

        private static MethodInfo GetWithoutAcquire(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
        }

        private static NullabilityState ReturnState(MethodInfo method) => Nullability.Create(method.ReturnParameter).ReadState;

        private static NullabilityState ParameterState(MethodBase method, string name)
        {
            return Nullability.Create(method.GetParameters().Single(p => p.Name == name)).WriteState;
        }

        [TestCase(typeof(ICacheManager))]
        [TestCase(typeof(MemoryCacheManager))]
        [TestCase(typeof(DistributedCacheManager))]
        [TestCase(typeof(HybridCacheManager))]
        public void Get_WithoutAcquire_ReturnIsAnnotatedNullable_Test(Type type)
        {
            Assert.That(ReturnState(GetWithoutAcquire(type)), Is.EqualTo(NullabilityState.Nullable));
        }

        [TestCase("connectionMultiplexer")]
        [TestCase("distributedCacheConfiguration")]
        [TestCase("memoryCache")]
        [TestCase("logger")]
        public void DistributedCacheManager_OptionalConstructorParameters_AreNullable_Test(string name)
        {
            var constructor = typeof(DistributedCacheManager).GetConstructors().Single();

            Assert.That(ParameterState(constructor, name), Is.EqualTo(NullabilityState.Nullable));
            Assert.That(ParameterState(constructor, "distributedCache"), Is.EqualTo(NullabilityState.NotNull));
        }

        [TestCase("connectionMultiplexer")]
        [TestCase("configuration")]
        [TestCase("logger")]
        public void HybridCacheManager_OptionalConstructorParameters_AreNullable_Test(string name)
        {
            var constructor = typeof(HybridCacheManager).GetConstructors().Single();

            Assert.That(ParameterState(constructor, name), Is.EqualTo(NullabilityState.Nullable));
            Assert.That(ParameterState(constructor, "memoryCache"), Is.EqualTo(NullabilityState.NotNull));
        }

        [TestCase(nameof(BaseDistributedCacheConfiguration.ConnectionString))]
        [TestCase(nameof(BaseDistributedCacheConfiguration.InstanceName))]
        public void BaseDistributedCacheConfiguration_UnsetStrings_AreNullable_Test(string name)
        {
            var property = typeof(BaseDistributedCacheConfiguration).GetProperty(name)!;

            Assert.That(Nullability.Create(property).ReadState, Is.EqualTo(NullabilityState.Nullable));
            Assert.That(property.GetValue(new BaseDistributedCacheConfiguration()), Is.Null);
        }

        [TestCase(nameof(CacheConfiguration.baseCacheConfiguration))]
        [TestCase(nameof(CacheConfiguration.baseDistributedCacheConfiguration))]
        public void CacheConfiguration_Sections_AreNullable_Test(string name)
        {
            var field = typeof(CacheConfiguration).GetField(name)!;

            Assert.That(Nullability.Create(field).ReadState, Is.EqualTo(NullabilityState.Nullable));
            Assert.That(field.GetValue(new CacheConfiguration()), Is.Null);
        }

        [Test]
        public void RouteHelper_GetDataInRoute_ReturnsAreNullable_Test()
        {
            var methods = typeof(RouteHelper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == nameof(RouteHelper.GetDataInRoute)).ToList();

            Assert.That(methods, Has.Count.EqualTo(2));
            Assert.That(methods.Select(ReturnState), Is.All.EqualTo(NullabilityState.Nullable));
        }

        [Test]
        public void PerRequestCache_Set_AcceptsNullData_Test()
        {
            var set = typeof(PerRequestCache).GetMethod(nameof(PerRequestCache.Set))!;
            var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            var cache = new PerRequestCache(accessor);

            Assert.That(ParameterState(set, "data"), Is.EqualTo(NullabilityState.Nullable));
            Assert.DoesNotThrow(() => cache.Set("key", null));
            Assert.That(cache.IsSet("key"), Is.False);
        }

        [Test]
        public void AddCaching_WithCacheConfiguration_ConfigurationIsNullable_Test()
        {
            var addCaching = typeof(ServiceCollectionExtensions).GetMethods()
                .Single(m => m.Name == nameof(ServiceCollectionExtensions.AddCaching) && m.GetParameters().Length == 3);

            Assert.That(ParameterState(addCaching, "configuration"), Is.EqualTo(NullabilityState.Nullable));
            Assert.That(ParameterState(addCaching, "cacheConfiguration"), Is.EqualTo(NullabilityState.NotNull));
        }

        [Test]
        public void AddCaching_WithConfiguration_KeepsInstanceNameNullWhenNotConfigured_Test()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Caching:Cache:DefaultCacheTime"] = "60",
                ["Caching:DistributedCache:Enabled"] = "false"
            }).Build();
            var services = new ServiceCollection();

            services.AddCaching(configuration);
            using var provider = services.BuildServiceProvider();
            var distributed = provider.GetRequiredService<BaseDistributedCacheConfiguration>();

            Assert.That(distributed.InstanceName, Is.Null);
            Assert.That(distributed.ConnectionString, Is.Null);
            Assert.That(provider.GetRequiredService<ICacheManager>(), Is.InstanceOf<MemoryCacheManager>());
        }

        [Test]
        public async Task Memory_NullKey_BehavesAsUncachedKey_Test()
        {
            var manager = new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions()));
            var calls = 0;

            Assert.That(manager.Get<string>(null!), Is.Null);
            Assert.That(manager.Get<int>(null!), Is.EqualTo(0));
            Assert.That(await manager.GetAsync<string>(null!, () => { calls++; return "a"; }), Is.EqualTo("a"));
            Assert.That(await manager.GetAsync<string>(null!, () => { calls++; return Task.FromResult("b"); }), Is.EqualTo("b"));
            Assert.That(manager.Get<string>(null!, () => { calls++; return "c"; }), Is.EqualTo("c"));
            Assert.DoesNotThrowAsync(() => manager.SetAsync(null!, "d"));
            Assert.That(calls, Is.EqualTo(3));
        }

        [Test]
        public void Distributed_NullKey_GetReturnsDefaultAndSetIsIgnored_Test()
        {
            var manager = new DistributedCacheManager(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
                memoryCache: new MemoryCache(new MemoryCacheOptions()));

            Assert.That(manager.Get<string>(null!), Is.Null);
            Assert.That(manager.Get<int>(null!), Is.EqualTo(0));
            Assert.DoesNotThrowAsync(() => manager.SetAsync(null!, "value"));
        }

        [Test]
        public void Hybrid_NullKey_SetIsIgnored_Test()
        {
            var distributed = new DistributedCacheManager(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                new HttpContextAccessor(),
                memoryCache: new MemoryCache(new MemoryCacheOptions()));
            var manager = new HybridCacheManager(new MemoryCacheManager(new MemoryCache(new MemoryCacheOptions())), distributed);

            Assert.DoesNotThrowAsync(() => manager.SetAsync(null!, "value"));
        }

        [Test]
        public async Task Distributed_WithoutOptionalDependencies_WorksWithoutConnectionOrFallback_Test()
        {
            var manager = new DistributedCacheManager(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                new HttpContextAccessor());
            var key = new CacheKey("nullable.no.dependencies", 5);

            await manager.SetAsync(key, "value");

            Assert.That(manager.Get<string>(key), Is.Null);
            Assert.That(manager.Get<int>(new CacheKey("nullable.no.dependencies.int", 5)), Is.EqualTo(0));
            Assert.That(await manager.GetAsync<string>(new CacheKey("nullable.no.dependencies.async", 5), () => "acquired"), Is.EqualTo("acquired"));
            Assert.That(manager.Get(new CacheKey("nullable.no.dependencies.sync", 5), () => "acquired.sync"), Is.EqualTo("acquired.sync"));
            Assert.DoesNotThrowAsync(() => manager.RemoveAsync(key));
            Assert.DoesNotThrowAsync(() => manager.RemoveByPrefixAsync("nullable."));
            Assert.DoesNotThrowAsync(() => manager.ClearAsync());
            Assert.That(manager.PerformActionWithLock("nullable.lock", TimeSpan.FromSeconds(5), () => { }), Is.True);
            Assert.That(await manager.PerformActionWithLockAsync("nullable.lock", TimeSpan.FromSeconds(5), () => Task.CompletedTask), Is.True);
        }

        [Test]
        public async Task Distributed_FallbackGet_ReturnsCachedValueAndDefaultOnMiss_Test()
        {
            var manager = new DistributedCacheManager(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                new HttpContextAccessor(),
                memoryCache: new MemoryCache(new MemoryCacheOptions()));
            var key = new CacheKey("nullable.fallback", 5);

            Assert.That(manager.Get<string>(key), Is.Null);

            await manager.SetAsync(key, "value");

            Assert.That(manager.Get<string>(key), Is.EqualTo("value"));
            Assert.That(await manager.GetAsync<string>(key, () => "acquired"), Is.EqualTo("value"));
        }
    }
}
