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

        /// <summary>
        /// Returns the string stored in <see cref="HttpContext.Items"/> under <paramref name="key"/>.
        /// </summary>
        /// <returns>The value, or <c>null</c> when nothing is stored or the value is not a string.</returns>
        public static string? GetDataInRoute(this HttpContext httpContext, string key)
            => httpContext.Items[$"{key}"] as string;


        /// <summary>
        /// Returns the value of type <typeparamref name="T"/> stored in <see cref="HttpContext.Items"/> under <paramref name="key"/>.
        /// </summary>
        /// <returns>The value, or <c>null</c> when nothing is stored or the value is not a <typeparamref name="T"/>.</returns>
        public static T? GetDataInRoute<T>(this HttpContext httpContext, string key)
            where T : class
            => httpContext.Items[$"{key}"] as T;

    }
}
