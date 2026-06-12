using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace PetRemote
{
    [DisallowMultipleComponent]
    public class SocketIoPollingClient : MonoBehaviour
    {
        [Serializable]
        class EngineOpenPacket
        {
            public string sid;
            public int pingInterval;
            public int pingTimeout;
        }

        public float reconnectDelaySeconds = 2f;
        public int requestTimeoutSeconds = 35;
        public bool autoReconnect = true;

        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<string, JArray, int?> EventReceived;

        readonly Queue<string> outboundPackets = new Queue<string>();
        readonly Dictionary<int, Action<JArray>> pendingAcks = new Dictionary<int, Action<JArray>>();

        Coroutine connectRoutine;
        Coroutine pollRoutine;
        Coroutine sendRoutine;
        string baseUrl;
        string engineSid;
        bool wantsConnection;
        bool namespaceConnected;
        bool disconnectNotified;
        int nextAckId = 1;

        public bool IsConnected => namespaceConnected;

        public void Connect(string serverUrl)
        {
            baseUrl = string.IsNullOrWhiteSpace(serverUrl) ? string.Empty : serverUrl.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) return;

            wantsConnection = true;
            if (connectRoutine == null) connectRoutine = StartCoroutine(ConnectionLoop());
        }

        public void Disconnect(string reason = "manual", bool allowReconnect = false)
        {
            wantsConnection = allowReconnect;
            StopActiveCoroutines();
            outboundPackets.Clear();
            pendingAcks.Clear();
            engineSid = null;
            namespaceConnected = false;
            NotifyDisconnected(reason);
        }

        public void Emit(string eventName, object payload = null)
        {
            if (!namespaceConnected || string.IsNullOrWhiteSpace(eventName)) return;
            EnqueueSocketPacket(BuildEventPacket(eventName, payload, null));
        }

        public void EmitWithAck(string eventName, object payload, Action<JArray> ack)
        {
            if (!namespaceConnected || string.IsNullOrWhiteSpace(eventName)) return;
            int ackId = nextAckId++;
            if (ack != null) pendingAcks[ackId] = ack;
            EnqueueSocketPacket(BuildEventPacket(eventName, payload, ackId));
        }

        public void ReplyToAck(int ackId, object payload)
        {
            if (ackId < 0 || !namespaceConnected) return;
            var args = new JArray();
            args.Add(ToJToken(payload));
            EnqueueRawPacket("43" + ackId + args.ToString(Newtonsoft.Json.Formatting.None));
        }

        IEnumerator ConnectionLoop()
        {
            while (wantsConnection)
            {
                disconnectNotified = false;

                bool openOk = false;
                yield return StartCoroutine(OpenTransport(result => openOk = result));

                if (openOk)
                {
                    pollRoutine = StartCoroutine(PollLoop());
                    sendRoutine = StartCoroutine(SendLoop());

                    while (wantsConnection && engineSid != null)
                    {
                        yield return null;
                    }
                }

                StopActiveCoroutines(false);
                pendingAcks.Clear();
                outboundPackets.Clear();
                namespaceConnected = false;

                if (!wantsConnection || !autoReconnect) break;
                yield return new WaitForSecondsRealtime(reconnectDelaySeconds);
            }

            connectRoutine = null;
        }

        IEnumerator OpenTransport(Action<bool> onComplete)
        {
            bool ok = false;
            string payload = null;
            yield return StartCoroutine(GetText(BuildSocketUrl(null), text => payload = text, success => ok = success));
            if (!ok || string.IsNullOrWhiteSpace(payload))
            {
                NotifyDisconnected("handshake_failed");
                onComplete(false);
                yield break;
            }

            var packets = SplitPayload(payload);
            for (int i = 0; i < packets.Count; i++)
            {
                var packet = packets[i];
                if (string.IsNullOrEmpty(packet) || packet[0] != '0') continue;
                try
                {
                    var open = JObject.Parse(packet.Substring(1)).ToObject<EngineOpenPacket>();
                    if (open != null && !string.IsNullOrWhiteSpace(open.sid))
                    {
                        engineSid = open.sid;
                        ok = true;
                        break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PetRemote] Engine open parse failed: " + e.Message);
                }
            }

            if (!ok || string.IsNullOrWhiteSpace(engineSid))
            {
                NotifyDisconnected("handshake_parse_failed");
                onComplete(false);
                yield break;
            }

            bool postOk = false;
            yield return StartCoroutine(PostText(BuildSocketUrl(engineSid), "40", success => postOk = success));
            if (!postOk)
            {
                NotifyDisconnected("namespace_open_failed");
                onComplete(false);
                yield break;
            }

            onComplete(true);
        }

        IEnumerator PollLoop()
        {
            while (wantsConnection && !string.IsNullOrWhiteSpace(engineSid))
            {
                bool ok = false;
                string payload = null;
                yield return StartCoroutine(GetText(BuildSocketUrl(engineSid), text => payload = text, success => ok = success));
                if (!ok)
                {
                    HandleTransportFailure("poll_failed");
                    yield break;
                }
                HandlePayload(payload);
            }
        }

        IEnumerator SendLoop()
        {
            while (wantsConnection && !string.IsNullOrWhiteSpace(engineSid))
            {
                if (outboundPackets.Count == 0)
                {
                    yield return null;
                    continue;
                }

                var batch = new StringBuilder();
                while (outboundPackets.Count > 0)
                {
                    if (batch.Length > 0) batch.Append('\x1e');
                    batch.Append(outboundPackets.Dequeue());
                }

                bool ok = false;
                yield return StartCoroutine(PostText(BuildSocketUrl(engineSid), batch.ToString(), success => ok = success));
                if (!ok)
                {
                    HandleTransportFailure("post_failed");
                    yield break;
                }
            }
        }

        void HandlePayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return;

            var packets = SplitPayload(payload);
            for (int i = 0; i < packets.Count; i++)
            {
                string packet = packets[i];
                if (string.IsNullOrEmpty(packet)) continue;

                switch (packet[0])
                {
                    case '0':
                        break;
                    case '1':
                        HandleTransportFailure("server_closed");
                        return;
                    case '2':
                        EnqueueRawPacket("3");
                        break;
                    case '4':
                        HandleSocketPacket(packet.Substring(1));
                        break;
                }
            }
        }

        void HandleSocketPacket(string packet)
        {
            if (string.IsNullOrEmpty(packet)) return;
            char type = packet[0];
            string rest = packet.Length > 1 ? packet.Substring(1) : string.Empty;

            if (type == '0')
            {
                if (!namespaceConnected)
                {
                    namespaceConnected = true;
                    Connected?.Invoke();
                }
                return;
            }

            if (type == '1')
            {
                HandleTransportFailure("socket_closed");
                return;
            }

            if (type == '2')
            {
                int? ackId;
                JArray data;
                if (!TryParseSocketData(rest, out ackId, out data)) return;
                if (data == null || data.Count == 0) return;
                string eventName = data[0] != null ? data[0].ToString() : string.Empty;
                EventReceived?.Invoke(eventName, data, ackId);
                return;
            }

            if (type == '3')
            {
                int? ackId;
                JArray data;
                if (!TryParseSocketData(rest, out ackId, out data) || !ackId.HasValue) return;
                Action<JArray> handler;
                if (pendingAcks.TryGetValue(ackId.Value, out handler))
                {
                    pendingAcks.Remove(ackId.Value);
                    handler?.Invoke(data);
                }
            }
        }

        bool TryParseSocketData(string raw, out int? ackId, out JArray data)
        {
            ackId = null;
            data = null;
            if (string.IsNullOrEmpty(raw)) return false;

            int index = 0;
            while (index < raw.Length && char.IsDigit(raw[index])) index++;
            if (index > 0)
            {
                int parsed;
                if (int.TryParse(raw.Substring(0, index), out parsed)) ackId = parsed;
            }

            string json = raw.Substring(index);
            if (string.IsNullOrWhiteSpace(json)) return true;

            try
            {
                data = JArray.Parse(json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PetRemote] Socket packet parse failed: " + e.Message + " / " + json);
                return false;
            }
        }

        void EnqueueSocketPacket(string socketPacket)
        {
            if (string.IsNullOrWhiteSpace(socketPacket)) return;
            EnqueueRawPacket(socketPacket);
        }

        void EnqueueRawPacket(string rawPacket)
        {
            if (string.IsNullOrWhiteSpace(rawPacket)) return;
            outboundPackets.Enqueue(rawPacket);
        }

        static string BuildEventPacket(string eventName, object payload, int? ackId)
        {
            var args = new JArray();
            args.Add(eventName);
            if (payload != null) args.Add(ToJToken(payload));
            return "42" + (ackId.HasValue ? ackId.Value.ToString() : string.Empty) + args.ToString(Newtonsoft.Json.Formatting.None);
        }

        string BuildSocketUrl(string sid)
        {
            string stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            if (string.IsNullOrWhiteSpace(sid))
            {
                return baseUrl + "/socket.io/?EIO=4&transport=polling&t=" + stamp;
            }
            return baseUrl + "/socket.io/?EIO=4&transport=polling&sid=" + UnityWebRequest.EscapeURL(sid) + "&t=" + stamp;
        }

        static List<string> SplitPayload(string payload)
        {
            return new List<string>(payload.Split('\x1e'));
        }

        IEnumerator GetText(string url, Action<string> onText, Action<bool> onDone)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = requestTimeoutSeconds;
                yield return request.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
                bool ok = request.result == UnityWebRequest.Result.Success;
#else
                bool ok = !(request.isHttpError || request.isNetworkError);
#endif
                onText?.Invoke(ok ? request.downloadHandler.text : null);
                onDone?.Invoke(ok);
            }
        }

        IEnumerator PostText(string url, string payload, Action<bool> onDone)
        {
            var body = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.timeout = requestTimeoutSeconds;
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "text/plain;charset=UTF-8");
                yield return request.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
                bool ok = request.result == UnityWebRequest.Result.Success;
#else
                bool ok = !(request.isHttpError || request.isNetworkError);
#endif
                onDone?.Invoke(ok);
            }
        }

        void HandleTransportFailure(string reason)
        {
            StopActiveCoroutines(false);
            engineSid = null;
            namespaceConnected = false;
            NotifyDisconnected(reason);
        }

        void NotifyDisconnected(string reason)
        {
            if (disconnectNotified) return;
            disconnectNotified = true;
            Disconnected?.Invoke(reason);
        }

        void StopActiveCoroutines(bool stopConnectRoutine = true)
        {
            if (pollRoutine != null)
            {
                StopCoroutine(pollRoutine);
                pollRoutine = null;
            }
            if (sendRoutine != null)
            {
                StopCoroutine(sendRoutine);
                sendRoutine = null;
            }
            if (stopConnectRoutine && connectRoutine != null)
            {
                StopCoroutine(connectRoutine);
                connectRoutine = null;
            }
        }

        static JToken ToJToken(object payload)
        {
            if (payload == null) return JValue.CreateNull();
            if (payload is JToken token) return token;
            return JToken.FromObject(payload);
        }
    }
}
