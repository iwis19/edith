using System;
using System.Collections;
using System.Threading.Tasks;
using Edith.Occlusion;
using Meta.XR;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Edith.Capture
{
    public enum CameraStatus { Idle, Unsupported, Requesting, PermissionDenied, Starting, Streaming }

    /// The headset camera's image, or a region of it, as a JPEG.
    public readonly struct CameraFrame
    {
        public readonly byte[] Jpeg;
        public readonly Vector2Int Size;
        /// When the camera took the image, in UTC.
        public readonly DateTime CapturedAt;
        /// Where the camera was for this image.
        public readonly Pose CameraPose;

        public CameraFrame(byte[] jpeg, Vector2Int size, DateTime capturedAt, Pose cameraPose)
        {
            Jpeg = jpeg;
            Size = size;
            CapturedAt = capturedAt;
            CameraPose = cameraPose;
        }
    }

    /// The four corners of a rectangle in the world, as the camera's user sees them.
    public readonly struct WorldQuad
    {
        public readonly Vector3 BottomLeft, BottomRight, TopRight, TopLeft;

        public WorldQuad(Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight, Vector3 topLeft)
        {
            BottomLeft = bottomLeft;
            BottomRight = bottomRight;
            TopRight = topRight;
            TopLeft = topLeft;
        }
    }

    /// Streams the headset's left passthrough camera once the user allows it, and hands out JPEGs of it: whole
    /// images, or crops scaled to at most CropSize.MaxSide on the longer side.
    public sealed class HeadsetCamera : MonoBehaviour
    {
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
        // A corner at or behind the lens projects to nonsense, since the projection divides by depth unchecked.
        const float MinCornerDepth = 0.05f;
        // Meta shows one permission prompt at a time, so the camera's waits for the spatial data one.
        const float SpatialPromptWaitSeconds = 10f;

        [SerializeField] PassthroughCameraAccess cameraAccess;
        [SerializeField, Range(1, 100)] int jpegQuality = 85;

        public CameraStatus Status { get; private set; }

        IEnumerator Start()
        {
            if (!PassthroughCameraAccess.IsSupported)
            {
                SetStatus(CameraStatus.Unsupported);
                yield break;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
            {
                SetStatus(CameraStatus.Requesting);
                var deadline = Time.realtimeSinceStartup + SpatialPromptWaitSeconds;
                yield return new WaitUntil(() => SpatialDataPermission.Asked || Time.realtimeSinceStartup > deadline);

                var answered = false;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => answered = true;
                callbacks.PermissionDenied += _ => answered = true;
                callbacks.PermissionDeniedAndDontAskAgain += _ => answered = true;
                Permission.RequestUserPermission(HeadsetCameraPermission, callbacks);
                yield return new WaitUntil(() => answered);
                if (!Permission.HasUserAuthorizedPermission(HeadsetCameraPermission))
                {
                    SetStatus(CameraStatus.PermissionDenied);
                    yield break;
                }
            }
#endif
            cameraAccess.enabled = true;
            SetStatus(CameraStatus.Starting);
        }

        void Update()
        {
            if (Status == CameraStatus.Starting && cameraAccess.IsPlaying) SetStatus(CameraStatus.Streaming);
        }

        /// Reads back the part of the latest image that `region` covers. The region is projected with the camera's own
        /// pose for that image, so head motion since the region was measured does not shift the crop. Throws
        /// InvalidOperationException when the camera is not Streaming, a corner is behind or at the lens, or under half
        /// of the region is on the image.
        public Task<CameraFrame> CaptureAsync(WorldQuad region)
        {
            if (Status != CameraStatus.Streaming)
                throw new InvalidOperationException($"Camera is {Status}, not Streaming.");

            var pose = cameraAccess.GetCameraPose();
            var texture = cameraAccess.GetTexture();
            var pixels = RegionPixels(region, pose, new Vector2Int(texture.width, texture.height));
            return Readback(texture, pixels, CropSize.MaxSide, cameraAccess.Timestamp, pose);
        }

        /// Reads back the whole latest image at full size. Throws InvalidOperationException when the camera is not
        /// Streaming.
        public Task<CameraFrame> CaptureFullAsync()
        {
            if (Status != CameraStatus.Streaming)
                throw new InvalidOperationException($"Camera is {Status}, not Streaming.");

            var texture = cameraAccess.GetTexture();
            return Readback(texture, new RectInt(0, 0, texture.width, texture.height), Mathf.Max(texture.width, texture.height),
                cameraAccess.Timestamp, cameraAccess.GetCameraPose());
        }

        RectInt RegionPixels(WorldQuad quad, Pose pose, Vector2Int image)
        {
            Vector2 Project(Vector3 corner)
            {
                var inCamera = Quaternion.Inverse(pose.rotation) * (corner - pose.position);
                if (inCamera.z <= MinCornerDepth)
                    throw new InvalidOperationException($"Crop corner is {inCamera.z:F3} m in front of the camera lens.");
                return cameraAccess.WorldToViewportPoint(corner, pose);
            }

            return CropRect.FromViewport(
                       Project(quad.BottomLeft), Project(quad.BottomRight), Project(quad.TopRight), Project(quad.TopLeft), image)
                   ?? throw new InvalidOperationException("Crop frame is mostly or wholly outside the camera image.");
        }

        /// The GPU scales the region down while it copies it out, so only the fitted size crosses to the CPU, and the
        /// JPEG is encoded on a worker thread so a full image does not stall the frame.
        Task<CameraFrame> Readback(Texture texture, RectInt pixels, int maxSide, DateTime capturedAt, Pose pose)
        {
            var size = CropSize.FitLongSide(pixels.width, pixels.height, maxSide);
            var scaled = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, scaled,
                new Vector2(pixels.width / (float)texture.width, pixels.height / (float)texture.height),
                new Vector2(pixels.x / (float)texture.width, pixels.y / (float)texture.height));

            var completion = new TaskCompletionSource<CameraFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            AsyncGPUReadback.Request(scaled, 0, TextureFormat.RGBA32, request =>
            {
                RenderTexture.ReleaseTemporary(scaled);
                if (request.hasError)
                {
                    completion.SetException(new InvalidOperationException("GPU readback of the camera texture failed."));
                    return;
                }

                var rgba = request.GetData<byte>().ToArray();
                var read = new Vector2Int(request.width, request.height);
                var quality = jpegQuality;
                Task.Run(() =>
                {
                    try
                    {
                        var jpeg = ImageConversion.EncodeArrayToJPG(rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)read.x, (uint)read.y, 0, quality);
                        completion.SetResult(new CameraFrame(jpeg, read, capturedAt, pose));
                    }
                    catch (Exception e)
                    {
                        completion.SetException(e);
                    }
                });
            });
            return completion.Task;
        }

        void SetStatus(CameraStatus status)
        {
            if (Status == status) return;
            Status = status;
            Debug.Log($"[EDITH camera] status -> {status}");
        }
    }
}
