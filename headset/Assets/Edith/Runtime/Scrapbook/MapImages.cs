using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// The map images of the Places lens. The world image is fetched once and kept for the app's life; the street
    /// maps of the cities seen most recently are kept by city and zoom. An image that fails to load is not asked for
    /// again until the Scrapbook is next opened.
    public sealed class MapImages
    {
        const int AreasKept = 4;
        const int World = 0;

        static Texture2D world;

        readonly RecentCache<Texture2D> areas = new RecentCache<Texture2D>(AreasKept, UnityEngine.Object.Destroy);
        readonly HashSet<int> loading = new HashSet<int>(), failed = new HashSet<int>();

        /// Forgets which images failed, so they are tried again.
        public void Retry() => failed.Clear();

        /// The world image, or null while it has not arrived. Asks `client` for it when it has not been asked yet.
        public Texture WorldImage(ScrapbookClient client, CancellationTokenSource session)
        {
            if (world == null && Wanted(World))
                Load(World, () => client.WorldMap(session.Token), session, texture =>
                {
                    // East of longitude 180 is longitude -180 again.
                    texture.wrapModeU = TextureWrapMode.Repeat;
                    world = texture;
                });
            return world;
        }

        public bool WorldFailed => world == null && failed.Contains(World);

        /// The street map of `city`, or null while it has not arrived. Asks `client` for it when it has not been
        /// asked yet.
        public Texture AreaImage(City city, ScrapbookClient client, CancellationTokenSource session)
        {
            var key = Key(city);
            if (areas.TryGet(key, out var area)) return area;
            if (Wanted(key))
                Load(key, () => client.AreaMap(city.MapCentre, city.Zoom, session.Token), session, texture => areas.Put(key, texture));
            return null;
        }

        public bool AreaFailed(City city) => failed.Contains(Key(city));

        /// Drops the street maps. The world image stays.
        public void Clear() => areas.Clear();

        bool Wanted(int key) => !loading.Contains(key) && !failed.Contains(key);

        // Odd, so no city has the world image's key.
        static int Key(City city) => HashCode.Combine(city.MapCentre.Lat, city.MapCentre.Lon, city.Zoom) | 1;

        async void Load(int key, Func<Task<byte[]>> fetch, CancellationTokenSource session, Action<Texture2D> keep)
        {
            loading.Add(key);
            try
            {
                var png = await fetch();
                if (session.IsCancellationRequested) return;
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp };
                if (texture.LoadImage(png, true)) keep(texture);
                else
                {
                    UnityEngine.Object.Destroy(texture);
                    throw new FormatException("not an image");
                }
            }
            catch (Exception e)
            {
                if (session.IsCancellationRequested) return;
                Debug.Log($"[EDITH scrapbook] no map image: {e.GetBaseException().Message}");
                failed.Add(key);
            }
            finally
            {
                loading.Remove(key);
            }
        }
    }
}
