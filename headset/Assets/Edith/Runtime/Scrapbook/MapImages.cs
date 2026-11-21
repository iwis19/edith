using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// The map images of the Places lens. The maps seen most recently are kept by centre and zoom. An image that fails to load is not asked
    /// for again until the Scrapbook is next opened.
    public sealed class MapImages
    {
        const int AreasKept = 6;

        readonly RecentCache<Texture2D> areas = new RecentCache<Texture2D>(AreasKept, UnityEngine.Object.Destroy);
        readonly HashSet<int> loading = new HashSet<int>(), failed = new HashSet<int>();

        /// Forgets which images failed, so they are tried again.
        public void Retry() => failed.Clear();

        /// The map of `frame`, or null while it has not arrived. Asks `client` for it when it has not been asked yet.
        public Texture AreaImage(MapFrame frame, ScrapbookClient client, CancellationTokenSource session)
        {
            var key = Key(frame);
            if (areas.TryGet(key, out var area)) return area;
            if (Wanted(key))
                Load(key, () => client.AreaMap(frame.Centre, frame.Zoom, session.Token), session, texture => areas.Put(key, texture));
            return null;
        }

        public bool AreaFailed(MapFrame frame) => failed.Contains(Key(frame));

        /// Drops the maps.
        public void Clear() => areas.Clear();

        bool Wanted(int key) => !loading.Contains(key) && !failed.Contains(key);

        static int Key(MapFrame frame) => HashCode.Combine(frame.Centre.Lat, frame.Centre.Lon, frame.Zoom);

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
