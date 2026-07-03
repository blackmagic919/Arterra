using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace Arterra.GamePlay.UI {
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class StreamingAssetsVideoPlayer : MonoBehaviour {
        [SerializeField] private string relativePath = "Splash.mp4";
        [SerializeField] private bool playOnStart = true;

        private VideoPlayer videoPlayer;
        private bool hasError;

        private void Awake() {
            videoPlayer = GetComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = Path.Combine(Application.streamingAssetsPath, relativePath);
            videoPlayer.errorReceived += OnVideoError;
        }

        private IEnumerator Start() {
            if (!playOnStart) yield break;

            videoPlayer.Prepare();
            while (!videoPlayer.isPrepared && !hasError) {
                yield return null;
            }

            if (!hasError) videoPlayer.Play();
        }

        private void OnDestroy() {
            if (videoPlayer != null) videoPlayer.errorReceived -= OnVideoError;
        }

        private void OnVideoError(VideoPlayer source, string message) {
            hasError = true;
            Debug.LogError($"Failed to play splash video at '{source.url}': {message}");
        }
    }
}
