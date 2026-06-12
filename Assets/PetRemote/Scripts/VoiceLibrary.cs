using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PetRemote
{
    [DisallowMultipleComponent]
    public class VoiceLibrary : MonoBehaviour
    {
        public string voicesFolderName = "voices";
        public bool recursive = true;

        readonly List<string> cachedVoiceUrls = new List<string>();
        static readonly string[] audioExtensions = { ".wav", ".mp3", ".ogg" };

        public List<string> GetVoiceUrls()
        {
            Refresh();
            return new List<string>(cachedVoiceUrls);
        }

        public void Refresh()
        {
            cachedVoiceUrls.Clear();

            string root = Path.Combine(Application.streamingAssetsPath, voicesFolderName);
            if (!Directory.Exists(root)) return;

            var files = Directory.GetFiles(root, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                if (!IsAudioFile(path)) continue;
                try
                {
                    cachedVoiceUrls.Add(new Uri(path).AbsoluteUri);
                }
                catch
                {
                    cachedVoiceUrls.Add("file:///" + path.Replace("\\", "/"));
                }
            }
        }

        static bool IsAudioFile(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            for (int i = 0; i < audioExtensions.Length; i++)
            {
                if (string.Equals(ext, audioExtensions[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
