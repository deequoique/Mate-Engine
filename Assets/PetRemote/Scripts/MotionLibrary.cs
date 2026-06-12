using System;
using System.Collections.Generic;
using CustomDancePlayer;
using UnityEngine;

namespace PetRemote
{
    [DisallowMultipleComponent]
    public class MotionLibrary : MonoBehaviour
    {
        AvatarDanceHandler danceHandler;
        readonly List<MotionManifestEntry> manifestEntries = new List<MotionManifestEntry>();
        bool manifestLoaded;

        public void RebindAvatar()
        {
            danceHandler = null;
        }

        public List<MotionMeta> GetAvailableMotions()
        {
            EnsureManifestLoaded();
            EnsureDanceHandler();

            var result = new List<MotionMeta>();
            var manifestById = new Dictionary<string, MotionManifestEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < manifestEntries.Count; i++)
            {
                var entry = manifestEntries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id)) continue;
                manifestById[entry.id] = entry;
            }

            if (danceHandler != null)
            {
                var live = danceHandler.GetAvailableMotions();
                for (int i = 0; i < live.Count; i++)
                {
                    var motion = live[i];
                    if (motion == null || string.IsNullOrWhiteSpace(motion.id)) continue;

                    MotionManifestEntry manifest;
                    manifestById.TryGetValue(motion.id, out manifest);
                    result.Add(new MotionMeta
                    {
                        id = motion.id,
                        label = manifest != null && !string.IsNullOrWhiteSpace(manifest.label) ? manifest.label : motion.label,
                        loop = manifest != null ? manifest.loop : motion.loop
                    });
                }
            }

            if (result.Count == 0)
            {
                for (int i = 0; i < manifestEntries.Count; i++)
                {
                    var entry = manifestEntries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.id)) continue;
                    result.Add(new MotionMeta
                    {
                        id = entry.id,
                        label = string.IsNullOrWhiteSpace(entry.label) ? entry.id : entry.label,
                        loop = entry.loop
                    });
                }
            }

            return result;
        }

        public bool PlayMotion(string motionName)
        {
            EnsureDanceHandler();
            if (danceHandler == null || string.IsNullOrWhiteSpace(motionName)) return false;

            if (danceHandler.TryPlayByName(motionName)) return true;
            return danceHandler.TryPlayFirst();
        }

        void EnsureManifestLoaded()
        {
            if (manifestLoaded) return;
            manifestLoaded = true;
            manifestEntries.Clear();

            var manifest = Resources.Load<TextAsset>("motions/manifest");
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.text)) return;

            try
            {
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MotionManifestEntry>>(manifest.text);
                if (parsed != null) manifestEntries.AddRange(parsed);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PetRemote] Failed to parse motion manifest: " + e.Message);
            }
        }

        void EnsureDanceHandler()
        {
            if (danceHandler == null)
            {
                danceHandler = FindFirstObjectByType<AvatarDanceHandler>(FindObjectsInactive.Include);
            }
        }
    }
}
