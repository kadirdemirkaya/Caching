using Base.Caching;
using Base.Caching.Helper;
using Base.Caching.Key;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Caching.Test
{
    public class Person
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public string Role { get; set; }
    }
    public class DistributedTest
    {
        public static IConfiguration _configuration { get; private set; }
        public ServiceCollection _services;
        private IServiceProvider _serviceProvider;
        private IHttpContextAccessor _httpContextAccessor;
        private DefaultHttpContext _httpContext;
        private ICacheLocker _locker;

        [OneTimeSetUp]
        public void GlobalSetup()
        {
            _services = new ServiceCollection();

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();

            _services.AddCaching(_configuration);

            _services.AddHttpContextAccessor();

            _serviceProvider = _services.BuildServiceProvider();

            _httpContextAccessor = _serviceProvider.GetRequiredService<IHttpContextAccessor>();

            _httpContext = new DefaultHttpContext();

            _httpContextAccessor.HttpContext = _httpContext;

            _locker = _serviceProvider.GetRequiredService<ICacheLocker>();

            _serviceProvider = _services.BuildServiceProvider();
        }

        private ICacheManager GetCacheManager() => _serviceProvider.GetRequiredService<ICacheManager>();
        private IHttpContextAccessor GetHttpContext() => _serviceProvider.GetRequiredService<IHttpContextAccessor>();

        [Test]
        public async Task Distributed_GetCache_Test()
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

            if (cacheData2 is not null)
                Assert.Pass();
            Assert.Fail();
        }

        [Test]
        public async Task Distributed_GetCache_And_Route_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();
            var _httpContextAccesor = GetHttpContext();

            CacheKey cacheKey = new CacheKey("distributed.cache");

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(cacheKey, async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Mehmet",
                    Role = "Moderator"
                };

                return data;
            });

            var cacheData2 = _staticMemoryCacheManager.GetAsync<User>(cacheKey, () => default(User));

            var cacheData3 = _httpContextAccesor.HttpContext.Items[$"{cacheKey.Key}"];
            var helperData = _httpContextAccesor.HttpContext.GetDataInRoute<User>(cacheKey.Key);

            if (cacheData3 is not null)
                Assert.Pass();
            Assert.Fail();
        }

        [Test]
        public async Task Distributed_CacheRemove_And_ExpireTime_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            CacheKey cacheKey = new CacheKey("distributed.cache", 1);

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(cacheKey, async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Mehmet",
                    Role = "Moderator"
                };

                return data;
            });

            //var cacheData1 = _staticMemoryCacheManager.Get<User>(cacheKey);

            var cacheData2 = _staticMemoryCacheManager.GetAsync<User>(cacheKey, () => default(User));

            await _staticMemoryCacheManager.RemoveAsync(cacheKey);

            var cacheData3 = _staticMemoryCacheManager.GetAsync<User>(cacheKey, () => default(User));
        }

        /// <summary>
        /// prefix bu kýsýmda key'ler ile eþleþen önekler olacak !
        /// </summary>
        /// <returns></returns>
        [Test]
        public async Task Prefix_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey, "default", "default.k"); // default.key

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
            //await _staticMemoryCacheManager.RemoveByPrefixAsync("default", "default.k");

            var cacheData2 = _staticMemoryCacheManager.Get<User>(cacheKey);

            await _staticMemoryCacheManager.RemoveByPrefixAsync("defa", "default.k");

            var cacheData3 = _staticMemoryCacheManager.Get<User>(cacheKey);

            Assert.Pass();
        }

        [Test]
        public async Task Distributed_CacheKeyParameter_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            //var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey); // default.key
            //var prepareKey = _staticMemoryCacheManager.PrepareKey(cacheKey, "default");

            var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey, "default"); // default.key
            var prepareKey = _staticMemoryCacheManager.PrepareKey(cacheKey, "def", "de");

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

            //await _staticMemoryCacheManager.RemoveAsync(cacheKey); yes
            //await _staticMemoryCacheManager.RemoveAsync(prepareKey); yes
            //await _staticMemoryCacheManager.RemoveByPrefixAsync("default");
            //await _staticMemoryCacheManager.RemoveByPrefixAsync("def");
            await _staticMemoryCacheManager.RemoveAsync(prepareKey, "de");

            var cacheData3 = _staticMemoryCacheManager.Get<User>(prepareKey);

            //await _staticMemoryCacheManager.RemoveByPrefixAsync("defa", "default.k");

            Assert.Pass();
        }

        [Test]
        public async Task Distributed_Clear_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            //var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey); // default.key
            //var prepareKey = _staticMemoryCacheManager.PrepareKey(cacheKey, "default");

            var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey, "default"); // default.key
            var prepareKey = _staticMemoryCacheManager.PrepareKey(cacheKey, "def", "de");

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

            if (cacheData3 == null)
                Assert.Pass();
            Assert.Fail();
        }

        [Test]
        public async Task Distributed_ActionLock_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            //var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey); // default.key
            //var prepareKey = _staticMemoryCacheManager.PrepareKey(cacheKey, "default");

            var cacheKey = new CacheKey(CacheKeyConstants.DefaultKey, "default"); // default.key
            var prepareKey = _staticMemoryCacheManager.PrepareKey(cacheKey, "def", "de");

            var cacheKey2 = new CacheKey("new_default", "newdefault"); // default.key
            var prepareKey2 = _staticMemoryCacheManager.PrepareKey(cacheKey, "newde", "newd");

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(prepareKey2, async () =>
            {
                var data = new User()
                {
                    Age = 13,
                    Name = "KADIR",
                    Role = "MODERATOR"
                };

                return data;
            });

            var cacheData2 = _staticMemoryCacheManager.GetAsync<User>(prepareKey2, () => default(User));

            _locker.PerformActionWithLock(prepareKey.Key, TimeSpan.FromMinutes(10), () =>
            {
                //Burada güncelleme iþlemleri yapýldý !!

                User updatedUser = new()
                {
                    Age = 13,
                    Name = "KADIR",
                    Role = "ADMIN"
                };

                // Db Update vs.
            });

            var cacheData3 = _staticMemoryCacheManager.Get<User>(prepareKey2);

            Assert.Pass();
        }


        [Test]
        public async Task Distributed_Set_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();

            User user1 = new User() { Age = 11, Name = "user1", Role = "USER" };
            User user2 = new User() { Age = 21, Name = "user2", Role = "USER" };
            User user3 = new User() { Age = 31, Name = "user3", Role = "USER" };

            await _staticMemoryCacheManager.SetAsync(new CacheKey("setkey1"), user1);
            await _staticMemoryCacheManager.SetAsync(new CacheKey("setkey2"), user2);
            await _staticMemoryCacheManager.SetAsync(new CacheKey("setkey3"), user3);
        }

        [Test]
        public async Task Distributed_RouteHelper_Test()
        {
            var _staticMemoryCacheManager = GetCacheManager();
            var _httpContextAccesor = GetHttpContext();

            CacheKey cacheKey = new CacheKey("distributed.cache");

            var cacheData1 = await _staticMemoryCacheManager.GetAsync<User>(cacheKey, async () =>
            {
                var data = new User()
                {
                    Age = 12,
                    Name = "Mehmet",
                    Role = "Moderator"
                };

                return data;
            });

            CacheKey cacheKey2 = new CacheKey("string.cache");

            await _staticMemoryCacheManager.SetAsync(cacheKey2, "string_route_helper_test_data");

            var cacheData3 = _httpContextAccesor.HttpContext.GetDataInRoute<User>(cacheKey.Key);

            var cacheData4 = _httpContextAccesor.HttpContext.GetDataInRoute(cacheKey2.Key);

            _httpContextAccesor.HttpContext.SetDataInRoute(cacheKey2.Key, "string_route_helper_test_data_SET");
            _httpContextAccesor.HttpContext.SetDataInRoute("set.key", "set_key_VALUE");

            var cacheData5 = _httpContextAccesor.HttpContext.GetDataInRoute("set.key");
            var cacheData6 = _httpContextAccesor.HttpContext.GetDataInRoute(cacheKey2.Key);

            if (cacheData3 is not null && cacheData4 is not null)
                Assert.Pass();
            Assert.Fail();
        }
    }
}