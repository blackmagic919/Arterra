using System;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Arterra.Editor {
    [DefaultExecutionOrder(10000)]
    public class PanoramaRecorder : MonoBehaviour {
        private const int Width = 4096;
        private const int Height = Width / 2;
        private const int FaceSize = 1024;
        private const int FrameRate = 30;
        private static PanoramaRecorder instance;
        private static readonly Vector3[] Directions = {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };
        private static readonly Vector3[] Up = {
            Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up
        };

        private Camera source;
        private Vector3 capturePosition;
        private RenderTexture faces;
        private RenderTexture panorama;
        private Texture2D pixels;
        private Material projection;
        private MediaEncoder encoder;
        private string outputPath;
        private int frames;
        private float previousCaptureDeltaTime;
        private bool changedTiming;
        private bool projectionReady;
        private readonly Matrix4x4[] worldToClip = new Matrix4x4[6];

        [InitializeOnLoadMethod]
        private static void Initialize() {
            AssemblyReloadEvents.beforeAssemblyReload += StopRecording;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingPlayMode) StopRecording();
            };
        }

        [MenuItem("Arterra/Capture/Toggle Panorama Recording _F8")]
        private static void ToggleRecording() {
            if (instance != null) {
                StopRecording();
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || Camera.main == null) {
                Debug.LogWarning("Panorama recording requires Play Mode and an active main camera.");
                return;
            }

            var owner = new GameObject("Panorama Recorder") { hideFlags = HideFlags.HideAndDontSave };
            instance = owner.AddComponent<PanoramaRecorder>();
            try { instance.Begin(Camera.main); }
            catch (Exception exception) {
                Debug.LogException(exception);
                StopRecording();
            }
        }

        private static void StopRecording() {
            if (instance != null) DestroyImmediate(instance.gameObject);
        }

        private void Begin(Camera camera) {
            source = camera;
            capturePosition = camera.transform.position;
            var request = new UniversalRenderPipeline.SingleCameraRequest();
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new NotSupportedException("The active render pipeline does not support URP camera capture requests.");
            if (!SystemInfo.supports2DArrayTextures || (SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) == 0)
                throw new NotSupportedException("Panorama recording requires texture arrays and cross-type texture copies.");

            Shader shader = Shader.Find("Hidden/Arterra/PanoramaProjection");
            if (shader == null || !shader.isSupported)
                throw new NotSupportedException("The panorama projection shader is missing or unsupported.");
            projection = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            faces = new RenderTexture(FaceSize, FaceSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 6,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            panorama = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            if (!faces.Create() || !panorama.Create())
                throw new InvalidOperationException("Could not allocate panorama recording textures.");
            pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            projection.SetTexture("_Faces", faces);

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Panoramas"));
            Directory.CreateDirectory(folder);
            outputPath = Path.Combine(folder, $"Panorama-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}.mp4");
            encoder = new MediaEncoder(outputPath, new VideoTrackAttributes {
                frameRate = new MediaRational(FrameRate),
                width = Width,
                height = Height,
                includeAlpha = false,
                bitRateMode = VideoBitrateMode.High
            });
            previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / FrameRate;
            changedTiming = true;
            Debug.Log($"Recording {Width}x{Height} panoramic video at {FrameRate} fps from {capturePosition}. Press F8 to finish.\n{outputPath}");
        }

        private void LateUpdate() {
            if (encoder == null) return;
            if (source == null || source != Camera.main) {
                Debug.LogWarning("Panorama recording stopped because the main camera changed.");
                StopRecording();
                return;
            }
            try {
                CaptureFrame();
                frames++;
            } catch (Exception exception) {
                Debug.LogException(exception);
                StopRecording();
            }
        }

        private void CaptureFrame() {
            Vector3 position = source.transform.position;
            Quaternion rotation = source.transform.rotation;
            float fieldOfView = source.fieldOfView;
            bool physical = source.usePhysicalProperties;
            float focalLength = source.focalLength;
            Rect rect = source.rect;
            int mask = source.cullingMask;
            RenderTexture target = source.targetTexture;
            RenderTexture active = RenderTexture.active;
            var additional = source.GetUniversalAdditionalCameraData();
            AntialiasingMode antialiasing = additional.antialiasing;
            bool sRGBWrite = GL.sRGBWrite;
            try {
                // Render the actual main camera so the existing atmosphere passes
                // rebuild their view-dependent caches for every captured face.
                source.usePhysicalProperties = false;
                // A small guard band gives bilinear filtering valid samples at face edges.
                source.fieldOfView = 2f * Mathf.Atan(1f + 2f / FaceSize) * Mathf.Rad2Deg;
                source.aspect = 1;
                source.rect = new Rect(0, 0, 1, 1);
                source.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
                additional.antialiasing = AntialiasingMode.None;
                source.ResetProjectionMatrix();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = faces };
                for (int face = 0; face < 6; face++) {
                    source.transform.SetPositionAndRotation(capturePosition, Quaternion.LookRotation(Directions[face], Up[face]));
                    if (!projectionReady)
                        worldToClip[face] = GL.GetGPUProjectionMatrix(source.projectionMatrix, true) * source.worldToCameraMatrix;
                    request.slice = face;
                    RenderPipeline.SubmitRenderRequest(source, request);
                }
                if (!projectionReady) {
                    projection.SetMatrixArray("_FaceWorldToClip", worldToClip);
                    projectionReady = true;
                }
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(null, panorama, projection);
                RenderTexture.active = panorama;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                pixels.Apply(false, false);
                if (!encoder.AddFrame(pixels)) throw new IOException("The video encoder rejected a panorama frame.");
            } finally {
                source.transform.SetPositionAndRotation(position, rotation);
                source.targetTexture = target;
                source.rect = rect;
                source.usePhysicalProperties = physical;
                source.fieldOfView = fieldOfView;
                if (physical) source.focalLength = focalLength;
                source.ResetAspect();
                source.ResetProjectionMatrix();
                source.cullingMask = mask;
                additional.antialiasing = antialiasing;
                RenderTexture.active = active;
                GL.sRGBWrite = sRGBWrite;
            }
        }

        private void OnDisable() {
            if (instance == this) instance = null;
            bool finalized = true;
            try { encoder?.Dispose(); }
            catch (Exception exception) {
                finalized = false;
                Debug.LogException(exception);
            }
            finally {
                encoder = null;
                if (changedTiming) Time.captureDeltaTime = previousCaptureDeltaTime;
                if (faces != null) { faces.Release(); DestroyImmediate(faces); }
                if (panorama != null) { panorama.Release(); DestroyImmediate(panorama); }
                if (pixels != null) DestroyImmediate(pixels);
                if (projection != null) DestroyImmediate(projection);
            }
            if (finalized && frames > 0) Debug.Log($"Saved panoramic video: {frames} frames ({frames / (float)FrameRate:F1}s).\n{outputPath}");
        }
    }
}
