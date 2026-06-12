using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PetRemote
{
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public class RemoteManager : MonoBehaviour
    {
        public string serverUrl = "http://localhost:3030";
        public string roomSecret = "change-me";
        public bool autoConnect = true;

        SocketIoPollingClient socketClient;
        VoiceLibrary voiceLibrary;
        MotionLibrary motionLibrary;
        VoicePlayer voicePlayer;
        CommandDispatcher commandDispatcher;
        bool joinRejected;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);

            socketClient = GetComponent<SocketIoPollingClient>() ?? gameObject.AddComponent<SocketIoPollingClient>();
            voiceLibrary = GetComponent<VoiceLibrary>() ?? gameObject.AddComponent<VoiceLibrary>();
            motionLibrary = GetComponent<MotionLibrary>() ?? gameObject.AddComponent<MotionLibrary>();
            voicePlayer = GetComponent<VoicePlayer>() ?? gameObject.AddComponent<VoicePlayer>();
            commandDispatcher = GetComponent<CommandDispatcher>() ?? gameObject.AddComponent<CommandDispatcher>();
            commandDispatcher.Initialize(voicePlayer, motionLibrary);

            socketClient.Connected += HandleSocketConnected;
            socketClient.Disconnected += HandleSocketDisconnected;
            socketClient.EventReceived += HandleSocketEvent;

            ApplyEnvironmentOverrides();
        }

        void Start()
        {
            RebindSceneRefs();
            if (autoConnect) socketClient.Connect(serverUrl);
        }

        void OnDestroy()
        {
            if (socketClient != null)
            {
                socketClient.Connected -= HandleSocketConnected;
                socketClient.Disconnected -= HandleSocketDisconnected;
                socketClient.EventReceived -= HandleSocketEvent;
            }
        }

        public void HandleAvatarLoaded(GameObject avatarRoot)
        {
            RebindSceneRefs();
        }

        public void RebindSceneRefs()
        {
            if (commandDispatcher != null) commandDispatcher.RebindAvatar();
            if (voicePlayer != null) voicePlayer.RebindAvatar();
            if (motionLibrary != null) motionLibrary.RebindAvatar();
        }

        void HandleSocketConnected()
        {
            joinRejected = false;
            socketClient.EmitWithAck("pet:join", new { secret = roomSecret, role = "pet" }, HandleJoinAck);
        }

        void HandleJoinAck(JArray ackArgs)
        {
            if (ackArgs == null || ackArgs.Count == 0) return;

            try
            {
                var response = ackArgs[0].ToObject<JoinAckResponse>();
                if (response == null) return;
                if (!response.ok)
                {
                    joinRejected = true;
                    socketClient.Disconnect("join_rejected", false);
                    Debug.LogWarning("[PetRemote] Join rejected: " + response.error);
                    return;
                }

                Debug.Log("[PetRemote] Joined remote room as pet.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PetRemote] Join ack parse failed: " + e.Message);
            }
        }

        void HandleSocketDisconnected(string reason)
        {
            if (joinRejected) return;
            Debug.LogWarning("[PetRemote] Socket disconnected: " + reason);
        }

        void HandleSocketEvent(string eventName, JArray data, int? ackId)
        {
            switch (eventName)
            {
                case "pet:command":
                    if (data != null && data.Count > 1)
                    {
                        try
                        {
                            var command = data[1].ToObject<RemoteCommand>();
                            commandDispatcher.HandleRemoteCommand(command, serverUrl);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[PetRemote] Command parse failed: " + e.Message);
                        }
                    }
                    break;
                case "pet:list-voices":
                    if (ackId.HasValue)
                    {
                        voiceLibrary.Refresh();
                        socketClient.ReplyToAck(ackId.Value, voiceLibrary.GetVoiceUrls().ToArray());
                    }
                    break;
                case "pet:list-motions":
                    if (ackId.HasValue)
                    {
                        socketClient.ReplyToAck(ackId.Value, motionLibrary.GetAvailableMotions().ToArray());
                    }
                    break;
                case "room:peers":
                    Debug.Log("[PetRemote] Peer status updated.");
                    break;
                case "room:kicked":
                    joinRejected = true;
                    string reason = "replaced";
                    if (data != null && data.Count > 1)
                    {
                        try
                        {
                            var payload = data[1].ToObject<RoomKickedPayload>();
                            if (payload != null && !string.IsNullOrWhiteSpace(payload.reason)) reason = payload.reason;
                        }
                        catch { }
                    }
                    Debug.LogWarning("[PetRemote] Kicked from room: " + reason);
                    socketClient.Disconnect("room_kicked", false);
                    break;
            }
        }

        void ApplyEnvironmentOverrides()
        {
            string envServer = Environment.GetEnvironmentVariable("PET_SERVER_URL");
            if (string.IsNullOrWhiteSpace(envServer)) envServer = Environment.GetEnvironmentVariable("SERVER_URL");
            if (!string.IsNullOrWhiteSpace(envServer)) serverUrl = envServer.Trim();

            string envSecret = Environment.GetEnvironmentVariable("PET_ROOM_SECRET");
            if (string.IsNullOrWhiteSpace(envSecret)) envSecret = Environment.GetEnvironmentVariable("ROOM_SECRET");
            if (!string.IsNullOrWhiteSpace(envSecret)) roomSecret = envSecret.Trim();
        }
    }
}
