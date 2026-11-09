using Base.Caching.Helper;
using System.Globalization;
using System.Text;

namespace Base.Caching.Key
{
    /// <summary>
    /// Prepares cache keys and prefixes by formatting them with parameters.
    /// </summary>
    public class CacheKeyService
    {
        private string HashAlgorithm => "SHA1";

        /// <summary>
        /// Formats <paramref name="prefix"/> with <paramref name="prefixParameters"/>, each converted by
        /// <see cref="CreateCacheKeyParameters(object)"/>.
        /// </summary>
        /// <param name="prefix">Prefix, optionally a format string.</param>
        /// <param name="prefixParameters">Values formatted into the prefix.</param>
        /// <returns>The formatted prefix, or <paramref name="prefix"/> unchanged when there are no parameters.</returns>
        protected virtual string PrepareKeyPrefix(string prefix, params object[] prefixParameters)
        {
            return prefixParameters?.Any() ?? false
                ? string.Format(prefix, prefixParameters.Select(CreateCacheKeyParameters).ToArray())
                : prefix;
        }
        /// <summary>
        /// Creates a SHA1 hash of the sorted identifiers.
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <returns>The hash, or an empty string when <paramref name="ids"/> is empty.</returns>
        protected virtual string CreateIdsHash(IEnumerable<int> ids)
        {
            var identifiers = ids.ToList();

            if (!identifiers.Any())
                return string.Empty;

            var identifiersString = string.Join(", ", identifiers.OrderBy(id => id));
            return HashHelper.CreateHash(Encoding.UTF8.GetBytes(identifiersString), HashAlgorithm);
        }

        /// <summary>
        /// Converts a key parameter before it is formatted: <c>null</c> becomes <c>"null"</c>, a collection of
        /// <see cref="int"/> becomes the hash of its sorted values, a <see cref="decimal"/> is formatted with the
        /// invariant culture; any other value is returned unchanged.
        /// </summary>
        /// <param name="parameter">Key parameter.</param>
        /// <returns>The value to format into the key.</returns>
        protected virtual object CreateCacheKeyParameters(object parameter)
        {
            return parameter switch
            {
                null => "null",
                IEnumerable<int> ids => CreateIdsHash(ids),
                decimal param => param.ToString(CultureInfo.InvariantCulture),
                _ => parameter
            };
        }

        /// <summary>
        /// Returns a copy of <paramref name="cacheKey"/> whose key and prefixes are formatted with
        /// <paramref name="cacheKeyParameters"/>; the expiration is kept.
        /// </summary>
        /// <param name="cacheKey">Key template.</param>
        /// <param name="cacheKeyParameters">Values formatted into the key and its prefixes.</param>
        /// <returns>The prepared key.</returns>
        public virtual CacheKey PrepareKey(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            return cacheKey.Create(CreateCacheKeyParameters, cacheKeyParameters);
        }

        /// <summary>
        /// Like <see cref="PrepareKey(CacheKey, object[])"/>, and sets the cache time of the prepared key to
        /// <paramref name="expireMinute"/>.
        /// </summary>
        /// <param name="cacheKey">Key template.</param>
        /// <param name="expireMinute">Cache time in minutes; zero or less disables caching.</param>
        /// <param name="cacheKeyParameters">Values formatted into the key and its prefixes.</param>
        /// <returns>The prepared key.</returns>
        public virtual CacheKey PrepareKey(CacheKey cacheKey, int expireMinute, params object[] cacheKeyParameters)
        {
            var key = cacheKey.Create(CreateCacheKeyParameters, cacheKeyParameters);

            key.CacheTime = expireMinute;

            return key;
        }

        /// <summary>
        /// Like <see cref="PrepareKey(CacheKey, object[])"/>, and sets the cache time of the prepared key to
        /// <see cref="CachingDefaults.CacheTime"/>.
        /// </summary>
        /// <param name="cacheKey">Key template.</param>
        /// <param name="cacheKeyParameters">Values formatted into the key and its prefixes.</param>
        /// <returns>The prepared key.</returns>
        public virtual CacheKey PrepareKeyForDefaultCache(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            var key = cacheKey.Create(CreateCacheKeyParameters, cacheKeyParameters);

            key.CacheTime = CachingDefaults.CacheTime;

            return key;
        }

        /// <summary>
        /// Like <see cref="PrepareKey(CacheKey, object[])"/>, and sets the cache time of the prepared key to
        /// <see cref="CachingDefaults.ShortCacheTime"/>.
        /// </summary>
        /// <param name="cacheKey">Key template.</param>
        /// <param name="cacheKeyParameters">Values formatted into the key and its prefixes.</param>
        /// <returns>The prepared key.</returns>
        public virtual CacheKey PrepareKeyForShortTermCache(CacheKey cacheKey, params object[] cacheKeyParameters)
        {
            var key = cacheKey.Create(CreateCacheKeyParameters, cacheKeyParameters);

            key.CacheTime = CachingDefaults.ShortCacheTime;

            return key;
        }
    }
}
