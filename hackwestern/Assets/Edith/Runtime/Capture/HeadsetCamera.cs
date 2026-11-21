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

    /// A region of the headset camera's image, as a JPEG.
    public readonly struct CameraFrame
    {
        public readonly byte[] Jpeg;
        public readonly Vector2Int Size;

        public CameraFrame(byte[] jpeg, Vector2Int size)
        {
            Jpeg = jpeg;
            Size = size;
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

    /// Streams the headset's left passthrough camera once the user allows it, and hands out JPEG crops of it.
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
            return Readback(texture, pixels);
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

        Task<CameraFrame> Readback(Texture texture, RectInt pixels)
        {
            var completion = new TaskCompletionSource<CameraFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            AsyncGPUReadback.Request(texture, 0, pixels.x, pixels.width, pixels.y, pixels.height, 0, 1, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    completion.SetException(new InvalidOperationException("GPU readback of the camera texture failed."));
                    return;
                }

                try
                {
                    var encoded = ImageConversion.EncodeNativeArrayToJPG(
                        request.GetData<byte>(), GraphicsFormat.R8G8B8A8_UNorm, (uint)request.width, (uint)request.height, 0, jpegQuality);
                    var jpeg = encoded.ToArray();
                    encoded.Dispose();
                    completion.SetResult(new CameraFrame(jpeg, new Vector2Int(request.width, request.height)));
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
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
