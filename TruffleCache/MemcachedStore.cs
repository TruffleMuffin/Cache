﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using MemcachedSharp;

namespace TruffleCache
{
    /// <summary>
    /// An implementation of the <see cref="ICacheStore" /> using the <see cref="MemcachedClient" />.
    /// </summary>
    public sealed class MemcachedStore : ICASCacheStore
    {
        private readonly Lazy<MemcachedClient> client;
        private readonly string prefix;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemcachedStore" /> class.
        /// </summary>
        public MemcachedStore()
        {
            client = new Lazy<MemcachedClient>(() => new MemcachedClient("localhost:11211", new MemcachedOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                ReceiveTimeout = TimeSpan.FromSeconds(2),
                EnablePipelining = true,
                MaxConnections = 2,
                MaxConcurrentRequestPerConnection = 15
            }));
            this.prefix = ConfigurationManager.AppSettings["TruffleCache.CachePrefix"] ?? "_";
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MemcachedStore" /> class.
        /// </summary>
        /// <param name="client">The client.</param>
        public MemcachedStore(MemcachedClient client)
        {
            this.client = new Lazy<MemcachedClient>(() => client);
            this.prefix = ConfigurationManager.AppSettings["TruffleCache.CachePrefix"] ?? "_";
        }

        /// <summary>
        /// Adds the specified instance to the cache
        /// </summary>
        /// <param name="key">The unqiue key to use for the item in the cache.</param>
        /// <param name="value">The value to add in the cache.</param>
        /// <param name="expiresIn">The timespan after which the item is expired from the cache.</param>
        /// <returns>
        /// True if the item was sucessfully added to the cache, false otherwise
        /// </returns>
        public Task SetAsync(string key, object value, TimeSpan expiresIn)
        {
            return client.Value.Set(PrefixKey(key), Serializer.Serialize(value), CreateOptions(expiresIn));
        }

        /// <summary>
        /// Gets multiple items from the cache.
        /// </summary>
        /// <param name="keys">The keys of the items to return.</param>
        /// <returns>
        /// A dictionary of the items from the cache, with the key being
        /// used as the dictionary key
        /// </returns>
        public async Task<IDictionary<string, T>> GetAsync<T>(params string[] keys)
        {
            var taskLookup = keys.AsParallel().ToDictionary(a => a, a => client.Value.Get(PrefixKey(a)));

            await Task.WhenAll(taskLookup.Values);

            return taskLookup.ToDictionary(a => a.Key, a => a.Value.Result == null ? default(T) : Serializer.Deserialize<T>(a.Value.Result.Data));
        }

        /// <summary>
        /// Removes the specified key from the cache.
        /// </summary>
        /// <param name="key">The unqiue key to use for the item in the cache.</param>
        /// <returns>
        /// True if the item was removed from the cache, false otherwise
        /// </returns>
        public Task<bool> RemoveAsync(string key)
        {
            return client.Value.Delete(PrefixKey(key));
        }

        /// <summary>
        /// Gets the specified item from the cache.
        /// </summary>
        /// <typeparam name="T">The type of object in the cache</typeparam>
        /// <param name="key">The unqiue key to use for the item in the cache.</param>
        /// <returns>
        /// The instance that was retrieved from the cache
        /// </returns>
        public async Task<T> GetAsync<T>(string key)
        {
            var result = await client.Value.Get(PrefixKey(key));

            if (result == null) return default(T);

            return await Serializer.DeserializeAsync<T>(result.Data);
        }

        /// <summary>
        /// Adds the specified instance to the cache if the checkValue matches the one already in the cache
        /// </summary>
        /// <param name="key">The unqiue key to use for the item in the cache.</param>
        /// <param name="checkValue">The check value.</param>
        /// <param name="value">The value to add in the cache.</param>
        /// <param name="expiresIn">The timespan after which the item is expired from the cache.</param>
        /// <returns>
        /// True if the item was sucessfully added to the cache, false otherwise
        /// </returns>
        public async Task<bool> SetAsync(string key, long checkValue, object value, TimeSpan expiresIn)
        {
            return await client.Value.Cas(PrefixKey(key), checkValue, Serializer.Serialize(value), CreateOptions(expiresIn)) == CasResult.Stored;
        }

        /// <summary>
        /// Gets the specified item from the cache with its check value.
        /// </summary>
        /// <typeparam name="T">The type of object in the cache</typeparam>
        /// <param name="key">The unqiue key to use for the item in the cache.</param>
        /// <returns>
        /// The instance that was retrieved from the cache, and its check value
        /// </returns>
        public async Task<CheckResult<T>> GetWithCheckAsync<T>(string key) where T : class
        {
            var result = await client.Value.Gets(PrefixKey(key));

            if (result == null) return new CheckResult<T> { Result = default(T) };

            return new CheckResult<T>
                {
                    CheckValue = result.CasUnique.Value,
                    Result = Serializer.Deserialize<T>(result.Data)
                };
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (client.IsValueCreated)
            {
                client.Value.Dispose();
            }
        }

        /// <summary>
        /// Creates the storage options.
        /// </summary>
        /// <param name="expiresIn">The expires in.</param>
        /// <returns>A <see cref="MemcachedStorageOptions"/></returns>
        private static MemcachedStorageOptions CreateOptions(TimeSpan expiresIn)
        {
            // Support unlimited caching by setting this to be null
            if (expiresIn == TimeSpan.Zero) return null;

            return new MemcachedStorageOptions { ExpirationTime = expiresIn };
        }

        /// <summary>
        /// Prefixes the key.
        /// </summary>
        /// <param name="key">The key.</param>
        private string PrefixKey(string key)
        {
            return string.Concat(prefix, key);
        }
    }
}