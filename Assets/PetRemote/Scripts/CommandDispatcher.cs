using System;
using System.Collections;
using System.Collections.Generic;
using CustomDancePlayer;
using UnityEngine;
using UnityEngine.Networking;

namespace PetRemote
{
    [DisallowMultipleComponent]
    public class CommandDispatcher : MonoBehaviour
    {
        VoicePlayer voicePlayer;
        MotionLibrary motionLibrary;
        UniversalBlendshapes blendshapes;
        AvatarWindowHandler windowHandler;
        readonly Dictionary<string, Coroutine> expressionTimers = new Dictionary<string, Coroutine>(StringComparer.OrdinalIgnoreCase);

        public void Initialize(VoicePlayer player, MotionLibrary motions)
        {
            voicePlayer = player;
            motionLibrary = motions;
            RebindAvatar();
        }

        public void RebindAvatar()
        {
            blendshapes = FindFirstObjectByType<UniversalBlendshapes>(FindObjectsInactive.Include);
            windowHandler = FindFirstObjectByType<AvatarWindowHandler>(FindObjectsInactive.Include);
            if (motionLibrary != null) motionLibrary.RebindAvatar();
            if (voicePlayer != null) voicePlayer.RebindAvatar();
        }

        public void HandleRemoteCommand(RemoteCommand command, string serverUrl)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.type)) return;

            switch (command.type)
            {
                case "expression":
                    ApplyExpression(command.name, command.strength ?? 1f, command.holdMs ?? 800);
                    break;
                case "animation":
                    if (motionLibrary != null) motionLibrary.PlayMotion(command.name);
                    break;
                case "say_audio":
                    if (voicePlayer != null && !string.IsNullOrWhiteSpace(command.url)) voicePlayer.PlayUrl(command.url);
                    break;
                case "say_tts":
                    if (voicePlayer != null)
                    {
                        string text = string.IsNullOrWhiteSpace(command.text) ? string.Empty : command.text.Trim();
                        if (text.Length > 0)
                        {
                            string url = serverUrl.TrimEnd('/') + "/api/tts?text=" + UnityWebRequest.EscapeURL(text);
                            voicePlayer.PlayUrl(url);
                        }
                    }
                    break;
                case "relocate":
                    if (ResolveWindowHandler() != null) ResolveWindowHandler().RelocateToCorner(command.corner);
                    break;
            }
        }

        void ApplyExpression(string name, float strength, int holdMs)
        {
            var target = ResolveBlendshapes();
            if (target == null || string.IsNullOrWhiteSpace(name)) return;

            target.SetEmotion(name, Mathf.Clamp01(strength));

            Coroutine running;
            if (expressionTimers.TryGetValue(name, out running) && running != null)
            {
                StopCoroutine(running);
            }
            expressionTimers[name] = StartCoroutine(ClearExpressionAfterDelay(name, Mathf.Clamp(holdMs, 150, 5000) / 1000f));
        }

        IEnumerator ClearExpressionAfterDelay(string name, float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            var target = ResolveBlendshapes();
            if (target != null) target.ClearEmotion(name);
            expressionTimers.Remove(name);
        }

        UniversalBlendshapes ResolveBlendshapes()
        {
            if (blendshapes == null)
            {
                blendshapes = FindFirstObjectByType<UniversalBlendshapes>(FindObjectsInactive.Include);
            }
            return blendshapes;
        }

        AvatarWindowHandler ResolveWindowHandler()
        {
            if (windowHandler == null)
            {
                windowHandler = FindFirstObjectByType<AvatarWindowHandler>(FindObjectsInactive.Include);
            }
            return windowHandler;
        }
    }
}
