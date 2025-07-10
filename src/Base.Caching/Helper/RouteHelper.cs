using Base.Caching.Key;
using Microsoft.AspNetCore.Http;

namespace Base.Caching.Helper
{
    public static class RouteHelper
    {
        public static void SetDataInRoute(this HttpContext httpContext, string key, string value)
        {
            httpContext.Items[$"{key}"] = value;
        }

        public static void SetDataInRoute<T>(this HttpContext httpContext, string key, T value)
        {
            httpContext.Items[$"{key}"] = value;
        }

        public static string GetDataInRoute(this HttpContext httpContext, string key)
            => httpContext.Items[$"{key}"] as string;


        public static T GetDataInRoute<T>(this HttpContext httpContext, string key)
            where T : class
            => httpContext.Items[$"{key}"] as T;

    }
}
