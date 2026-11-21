using System;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Edith.Scan
{
    /// One whole camera image as a JPEG, with the camera's pose for that image.
    public readonly struct ScanFrame
    {
        public readonly byte[] Jpeg;
        public readonly Vector2Int Size;
        public readonly Pose CameraPose;

        public ScanFrame(byte[] jpeg, Vector2Int size, Pose cameraPose)
        {
            Jpeg = jpeg;
            Size = size;
            CameraPose = cameraPose;
        }
    }

    public static class ScanCapture
    {
        /// Reads back the camera's latest image without stalling the frame and encodes it on a worker thread.
        /// `camera` must be playing.
        public static Task<ScanFrame> CaptureAsync(PassthroughCameraAccess camera, int jpegQuality)
        {
            var pose = camera.GetCameraPose();
            var completion = new TaskCompletionSource<ScanFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            AsyncGPUReadback.Request(camera.GetTexture(), 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    completion.SetException(new InvalidOperationException("GPU readback of the camera texture failed."));
                    return;
                }
                var pixels = request.GetData<byte>().ToArray();
                var size = new Vector2Int(request.width, request.height);
                Task.Run(() =>
                {
                    try
                    {
                        var jpeg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)size.x, (uint)size.y, 0, jpegQuality);
                        completion.SetResult(new ScanFrame(jpeg, size, pose));
                    }
                    catch (Exception e)
                    {
                        completion.SetException(e);
                    }
                });
            });
            return completion.Task;
        }
    }
}
