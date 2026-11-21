using System;
using System.Threading.Tasks;
using Edith.Capture;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Edith.Ask
{
    /// A photo as a JPEG of at most a given size: the GPU scales it and reads it back without stalling the frame, and
    /// a worker thread encodes it.
    public sealed class PhotoJpeg
    {
        const int Quality = 80;

        volatile byte[] jpeg;

        /// Null until the JPEG is ready, and for good when it fails.
        public byte[] Result => jpeg;

        public static PhotoJpeg Start(Texture photo, int maxLongSide)
        {
            var result = new PhotoJpeg();
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Debug.LogWarning("[EDITH ask] this GPU cannot read photos back, so the question goes without one");
                return result;
            }
            var size = CropSize.FitLongSide(photo.width, photo.height, maxLongSide);
            var scaled = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(photo, scaled);
            AsyncGPUReadback.Request(scaled, 0, TextureFormat.RGBA32, request =>
            {
                RenderTexture.ReleaseTemporary(scaled);
                if (request.hasError)
                {
                    Debug.LogWarning("[EDITH ask] reading the photo back failed, so the question goes without one");
                    return;
                }
                var pixels = request.GetData<byte>().ToArray();
                Task.Run(() =>
                {
                    try
                    {
                        result.jpeg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_UNorm,
                            (uint)size.x, (uint)size.y, 0, Quality);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[EDITH ask] encoding the photo failed: {e.Message}");
                    }
                });
            });
            return result;
        }
    }
}
