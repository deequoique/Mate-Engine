using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace PetRemote
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class VoicePlayer : MonoBehaviour
    {
        public float lipScale = 18f;
        [Range(0.01f, 1f)] public float lipSmoothing = 0.22f;
        public FFTWindow fftWindow = FFTWindow.BlackmanHarris;

        AudioSource audioSource;
        UniversalBlendshapes blendshapes;
        Coroutine playRoutine;
        AudioClip runtimeClip;
        float mouthValue;
        readonly float[] spectrum = new float[128];

        void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
        }

        public void RebindAvatar()
        {
            blendshapes = null;
        }

        public void PlayUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (playRoutine != null) StopCoroutine(playRoutine);
            StopPlaybackInternal();
            playRoutine = StartCoroutine(PlayUrlRoutine(url.Trim()));
        }

        public void StopPlayback()
        {
            StopPlaybackInternal();
        }

        void Update()
        {
            var targetBlendshapes = ResolveBlendshapes();
            if (targetBlendshapes == null) return;

            float target = 0f;
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.GetSpectrumData(spectrum, 0, fftWindow);
                float sum = 0f;
                int upper = Mathf.Min(32, spectrum.Length);
                for (int i = 2; i < upper; i++) sum += spectrum[i];
                target = Mathf.Clamp01(sum * lipScale);
            }

            mouthValue = Mathf.Lerp(mouthValue, target, lipSmoothing);
            if (mouthValue < 0.001f && target <= 0f) mouthValue = 0f;
            targetBlendshapes.SetEmotion("aa", mouthValue);
        }

        IEnumerator PlayUrlRoutine(string url)
        {
            var audioType = GuessAudioType(url);
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
#if UNITY_2020_2_OR_NEWER
                yield return request.SendWebRequest();
                bool ok = request.result == UnityWebRequest.Result.Success;
#else
                yield return request.SendWebRequest();
                bool ok = !(request.isHttpError || request.isNetworkError);
#endif
                if (!ok)
                {
                    Debug.LogWarning("[PetRemote] Voice playback failed: " + request.error + " @ " + url);
                    playRoutine = null;
                    yield break;
                }

                runtimeClip = DownloadHandlerAudioClip.GetContent(request);
            }

            if (runtimeClip == null)
            {
                playRoutine = null;
                yield break;
            }

            audioSource.Stop();
            audioSource.clip = runtimeClip;
            audioSource.time = 0f;
            audioSource.Play();

            while (audioSource != null && audioSource.isPlaying)
            {
                yield return null;
            }

            ReleaseRuntimeClip();
            playRoutine = null;
        }

        void StopPlaybackInternal()
        {
            if (playRoutine != null)
            {
                StopCoroutine(playRoutine);
                playRoutine = null;
            }
            if (audioSource != null) audioSource.Stop();
            ReleaseRuntimeClip();
        }

        UniversalBlendshapes ResolveBlendshapes()
        {
            if (blendshapes == null)
            {
                blendshapes = FindFirstObjectByType<UniversalBlendshapes>(FindObjectsInactive.Include);
            }
            return blendshapes;
        }

        void ReleaseRuntimeClip()
        {
            if (audioSource != null) audioSource.clip = null;
            if (runtimeClip != null)
            {
                Destroy(runtimeClip);
                runtimeClip = null;
            }
        }

        static AudioType GuessAudioType(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return AudioType.UNKNOWN;
            if (url.IndexOf("/api/tts", StringComparison.OrdinalIgnoreCase) >= 0) return AudioType.MPEG;

            try
            {
                var uri = new Uri(url);
                return GuessAudioTypeFromPath(uri.LocalPath);
            }
            catch
            {
                return GuessAudioTypeFromPath(url);
            }
        }

        static AudioType GuessAudioTypeFromPath(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return AudioType.UNKNOWN;
            if (string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase)) return AudioType.WAV;
            if (string.Equals(ext, ".ogg", StringComparison.OrdinalIgnoreCase)) return AudioType.OGGVORBIS;
            if (string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase)) return AudioType.MPEG;
            return AudioType.UNKNOWN;
        }
    }
}
