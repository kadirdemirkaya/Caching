using Base.Caching;
using Base.Caching.Key;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Caching.Test
{
    public class User
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public string Role { get; set; }
    }

    public class MemoryTest
    {
        public static IConfiguration _configuration { get; private set; }
        public ServiceCollection _services;
        private IServiceProvider _serviceProvider;

        [OneTimeSetUp]
        public void GlobalSetup()
        {
            _services = new ServiceCollection();

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();

            _services.AddCaching(_configuration);

            _serviceProvider = _services.BuildServiceProvider();
        }

        private IStaticCacheManager GetCacheManager() => _serviceProvider.GetRequiredService<IStaticCacheManager>();

        [Test]
        public async Task Memory_GetCache_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(new CacheKey(CacheKeyConstants.DefaultKey), async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Kadir",
                    Role = "Admin"
                };

                return data;
            });

            var cacheData2 = _staticMemoryCacheManager.Get<User>(new CacheKey(CacheKeyConstants.DefaultKey));

            Assert.Pass();
        }

        [Test]
        public async Task Memory_CacheRemove_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey);

            var prepareKey = _staticMemoryCacheManager.PrepareKey(cacheKey);

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(prepareKey, async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Kadir",
                    Role = "Admin"
                };

                return data;
            });

            await _staticMemoryCacheManager.RemoveAsync(cacheKey);

            var cacheData2 = _staticMemoryCacheManager.Get<User>(cacheKey);

            Assert.Pass();
        }

        /// <summary>
        /// burada prefixler ile token oluşturulacak ve sonradan silnmek için prefixleri kullanabileceğiz !
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task Prefix_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey, "paramkey1", "paramkey2");

            var prepareKey = _staticMemoryCacheManager.PrepareKey(cacheKey);

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(prepareKey, async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Kadir",
                    Role = "Admin"
                };

                return data;
            });

            //await _staticMemoryCacheManager.RemoveAsync(cacheKey);
            //await _staticMemoryCacheManager.RemoveByPrefixAsync("paramkey1", "paramkey2");
            //await _staticMemoryCacheManager.RemoveByPrefixAsync("paramkey1", "paramkey2");
            await _staticMemoryCacheManager.RemoveByPrefixAsync("paramkey2");

            var cacheData2 = _staticMemoryCacheManager.Get<User>(cacheKey);

            Assert.Pass();
        }

        [Test]
        public async Task DefaultPrepareKey_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey, "paramkey1", "paramkey2");

            var prepareKey = _staticMemoryCacheManager.PrepareKeyForDefaultCache(cacheKey);

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(prepareKey, async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Kadir",
                    Role = "Admin"
                };

                return data;
            });

            var cacheData2 = _staticMemoryCacheManager.Get<User>(cacheKey);

            Assert.Pass();

        }

        [Test]
        public async Task Memory_CacheKeyParameter_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey);

            var prepareKey = _staticMemoryCacheManager.PrepareKeyForDefaultCache(cacheKey, "cache.key.1", "cache.key.2");

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(prepareKey, async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Kadir",
                    Role = "Admin"
                };

                return data;
            });

            //await _staticMemoryCacheManager.RemoveAsync(cacheKey, "cache.key.1");
            await _staticMemoryCacheManager.RemoveAsync(cacheKey);

            var cacheData2 = _staticMemoryCacheManager.Get<User>(cacheKey);

            Assert.Pass();
        }

        [Test]
        public async Task Memory_All_Clear()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            var cacheKey = new CacheKey("key{0},key{1}", "prefix.1", "prefix.2");

            var prepareKey = _staticMemoryCacheManager.PrepareKeyForDefaultCache(cacheKey, "cache.key.1", "cache.key.2");

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(prepareKey, async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Kadir",
                    Role = "Admin"
                };

                return data;
            });

            var cacheData2 = _staticMemoryCacheManager.Get<User>(prepareKey);

            await _staticMemoryCacheManager.ClearAsync();

            var cacheData3 = _staticMemoryCacheManager.Get<User>(prepareKey);

            Assert.Pass();
        }
    }
}
