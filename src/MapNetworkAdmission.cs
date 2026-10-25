using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace LegacyMapExpansion
{
    internal static class MapNetworkAdmission
    {
        private const string HarmonyId = "com.bigcitylegacy.map1018.admission";
        private static readonly Dictionary<NetworkConnection, MapAdmissionSession> sessions = new Dictionary<NetworkConnection, MapAdmissionSession>();
        private static Harmony harmony;
        private static NetworkConnection clientConnection;
        private static NetManager clientManager;
        private static bool clientWaiting, clientApproved, resuming;
        private static double clientDeadline;
        private static string clientNonce, failure;
        private static float timeout = 8, heartbeat = 30;
        private static double nextUpdate;
        internal static string Identity;
        internal static bool Installed { get; private set; }
        internal static bool ShowFailure;

        internal static void Install(float timeoutSeconds, float heartbeatSeconds)
        {
            timeout = timeoutSeconds; heartbeat = heartbeatSeconds;
            harmony = new Harmony(HarmonyId);
            try
            {
                Patch(typeof(NetManager), "OnStartServer", Type.EmptyTypes, null, nameof(ServerStarted));
                Patch(typeof(NetManager), "OnStartClient", new[] { typeof(NetworkClient) }, null, nameof(ClientStarted));
                Patch(typeof(NetManager), "OnServerConnect", new[] { typeof(NetworkConnection) }, null, nameof(ServerConnected));
                Patch(typeof(NetManager), "OnServerDisconnect", new[] { typeof(NetworkConnection) }, nameof(ServerDisconnected));
                Patch(typeof(NetManager), "OnStopServer", Type.EmptyTypes, nameof(ServerStopped));
                Patch(typeof(NetManager), "OnClientConnect", new[] { typeof(NetworkConnection) }, nameof(ClientConnected));
                Patch(typeof(NetManager), "OnClientDisconnect", new[] { typeof(NetworkConnection) }, nameof(ClientDisconnected));
                Patch(typeof(NetworkManager), "OnServerReady", new[] { typeof(NetworkConnection) }, nameof(ServerReady));
                Patch(typeof(NetManager), "OnServerAddPlayer", new[] { typeof(NetworkConnection), typeof(short) }, nameof(ServerAddPlayer));
                Patch(typeof(NetManager), "OnServerAddPlayer", new[] { typeof(NetworkConnection), typeof(short), typeof(NetworkReader) }, nameof(ServerAddPlayer));
                Patch(typeof(NetConnect), "Cmd_SetPrivateID", new[] { typeof(string), typeof(string), typeof(string), typeof(RuntimePlatform) }, nameof(GuardPlayer));
                Patch(typeof(NetConnect), "InitInputControl", new[] { typeof(string), typeof(RuntimePlatform) }, nameof(GuardPlayer));
                Patch(typeof(NetworkManager), "StartServer", Type.EmptyTypes, nameof(CanStartServer));
                Patch(typeof(SimpleUDP.Message), "SendBack", new[] { typeof(string) }, nameof(Ping));
                Installed = true;
            }
            catch { harmony.UnpatchSelf(); throw; }
        }

        private static void Patch(Type type, string name, Type[] args, string prefix, string postfix = null)
        {
            var target = AccessTools.DeclaredMethod(type, name, args);
            if (target == null) throw new MissingMethodException(type.FullName, name);
            var before = prefix == null ? null : new HarmonyMethod(typeof(MapNetworkAdmission), prefix) { priority = Priority.First };
            var after = postfix == null ? null : new HarmonyMethod(typeof(MapNetworkAdmission), postfix) { priority = Priority.Last };
            harmony.Patch(target, before, after);
        }

        private static bool CanStartServer(NetworkManager __instance, ref bool __result)
        {
            if (!(__instance is NetManager) || Plugin.Ready) return true;
            __result = false;
            Plugin.Warn("Server start blocked: LegacyMapExpansion resources did not load. Check map errors before starting the server.");
            return false;
        }

        private static void ServerStarted()
        {
            sessions.Clear();
            if (NetworkServer.handlers.TryGetValue(MapNetworkProtocol.ProofId, out var existing) && existing != (NetworkMessageDelegate)OnProof)
                throw new InvalidOperationException("Map handshake message ID is already in use.");
            NetworkServer.RegisterHandler(MapNetworkProtocol.ProofId, OnProof);
        }

        private static void ClientStarted(NetworkClient client)
        {
            clientConnection = null; clientManager = null; clientWaiting = clientApproved = false; clientNonce = null;
            if (client.handlers.TryGetValue(MapNetworkProtocol.ChallengeId, out var challenge) && challenge != (NetworkMessageDelegate)OnChallenge ||
                client.handlers.TryGetValue(MapNetworkProtocol.ResultId, out var result) && result != (NetworkMessageDelegate)OnResult)
                throw new InvalidOperationException("Map handshake message IDs are already in use.");
            client.RegisterHandler(MapNetworkProtocol.ChallengeId, OnChallenge);
            client.RegisterHandler(MapNetworkProtocol.ResultId, OnResult);
        }

        private static void ServerConnected(NetworkConnection conn)
        {
            if (conn == null) return;
            var state = new MapAdmissionSession(Time.realtimeSinceStartup, timeout);
            sessions[conn] = state;
            if (!Plugin.Ready) { Reject(conn, state, "Карта сервера не загрузилась. Подключение временно недоступно."); return; }
            SendChallenge(conn, state);
        }

        private static void SendChallenge(NetworkConnection conn, MapAdmissionSession state)
        {
            conn.Send(MapNetworkProtocol.ChallengeId, new MapChallenge { Protocol = MapNetworkProtocol.Version, Nonce = state.Nonce, Identity = Identity });
        }

        private static bool ClientConnected(NetManager __instance, NetworkConnection conn)
        {
            if (resuming || conn == clientConnection && clientApproved) return true;
            clientManager = __instance; clientConnection = conn; clientWaiting = true; clientApproved = false;
            clientDeadline = Time.realtimeSinceStartup + timeout + 2;
            return false;
        }

        private static void OnChallenge(NetworkMessage message)
        {
            if (message.conn != clientConnection || clientManager == null) return;
            try
            {
                var challenge = message.ReadMessage<MapChallenge>();
                if (challenge.Nonce == null || challenge.Nonce.Length != 32) throw new InvalidOperationException("Invalid map challenge.");
                clientNonce = challenge.Nonce;
                string error = MapNetworkProtocol.Validate(challenge.Protocol, challenge.Identity, Plugin.Ready, Identity);
                message.conn.Send(MapNetworkProtocol.ProofId, new MapProof {
                    Protocol = MapNetworkProtocol.Version, Nonce = challenge.Nonce, Identity = Identity ?? "", Ready = Plugin.Ready
                });
                if (error != null) FailClient(error);
            }
            catch (Exception error) { FailClient("Ошибка проверки карты: " + error.Message); }
        }

        private static void OnProof(NetworkMessage message)
        {
            if (!sessions.TryGetValue(message.conn, out var state) || !state.Awaiting) return;
            try
            {
                var proof = message.ReadMessage<MapProof>();
                if (!state.IsCurrent(proof.Nonce)) return;
                if (state.TimedOut(Time.realtimeSinceStartup)) { Reject(message.conn, state, MissingPlugin); return; }
                string error = MapNetworkProtocol.Validate(proof.Protocol, proof.Identity, proof.Ready && Plugin.Ready, Identity);
                if (error != null) { Reject(message.conn, state, error); return; }
                bool first = !state.Authorized;
                state.Accept(Time.realtimeSinceStartup, heartbeat);
                message.conn.Send(MapNetworkProtocol.ResultId, new MapResult { Nonce = state.Nonce, Accepted = true, Reason = "" });
                if (first)
                {
                    Plugin.LogInfo("Map handshake accepted: connection " + message.conn.connectionId + ", map " + Identity);
                    var manager = NetManager.me;
                    if (state.ReadyRequested && manager) { state.ReadyRequested = false; manager.OnServerReady(message.conn); }
                    if (state.PlayerRequested.HasValue && manager)
                    {
                        short controller = state.PlayerRequested.Value; state.PlayerRequested = null;
                        manager.OnServerAddPlayer(message.conn, controller);
                    }
                }
            }
            catch (Exception error) { Reject(message.conn, state, "Ошибка сетевой проверки карты."); Plugin.Warn("Map proof rejected: " + error.Message); }
        }

        private static void OnResult(NetworkMessage message)
        {
            if (message.conn != clientConnection) return;
            try
            {
                var result = message.ReadMessage<MapResult>();
                if (result.Nonce != clientNonce) return;
                if (!result.Accepted) { FailClient(result.Reason); return; }
                if (clientApproved) return;
                clientApproved = true; clientWaiting = false;
                resuming = true;
                try { clientManager.OnClientConnect(clientConnection); }
                finally { resuming = false; }
            }
            catch (Exception error) { FailClient("Ошибка подтверждения карты: " + error.Message); }
        }

        private static bool Authorized(NetworkConnection conn)
            => conn != null && Plugin.Ready && sessions.TryGetValue(conn, out var state) && state.Authorized;

        private static bool ServerReady(NetworkManager __instance, NetworkConnection conn)
        {
            if (!(__instance is NetManager) || !NetworkServer.active || Authorized(conn)) return true;
            if (conn != null && sessions.TryGetValue(conn, out var state)) state.ReadyRequested = true;
            return false;
        }

        private static bool ServerAddPlayer(NetworkConnection conn, short playerControllerId)
        {
            if (!NetworkServer.active || Authorized(conn)) return true;
            if (conn != null && sessions.TryGetValue(conn, out var state) && playerControllerId == 0) state.PlayerRequested = playerControllerId;
            return false;
        }

        private static bool GuardPlayer(NetConnect __instance) => !NetworkServer.active || Authorized(__instance.conn);
        private static void ServerDisconnected(NetworkConnection conn) { if (conn != null) sessions.Remove(conn); }
        private static void ServerStopped() { sessions.Clear(); }
        private static void ClientDisconnected()
        {
            if (clientWaiting) QueueFailure("Подключение прервано до проверки карты. На сервере и клиенте нужен актуальный LegacyMapExpansion 1.0.0.");
            clientWaiting = clientApproved = false; clientConnection = null; clientManager = null;
        }

        private const string MissingPlugin = "Для входа на этот сервер требуется актуальный LegacyMapExpansion 1.0.0. Установи плагин карты и перезапусти игру.";
        private static void Reject(NetworkConnection conn, MapAdmissionSession state, string reason)
        {
            if (!double.IsPositiveInfinity(state.DisconnectAt)) return;
            state.Reject(Time.realtimeSinceStartup);
            conn.Send(MapNetworkProtocol.ResultId, new MapResult { Nonce = state.Nonce, Accepted = false, Reason = reason });
            Plugin.Warn("Map handshake rejected: connection " + conn.connectionId + ": " + reason);
        }

        private static void QueueFailure(string reason)
        {
            if (string.IsNullOrEmpty(failure)) failure = reason;
        }
        private static void FailClient(string reason)
        {
            QueueFailure(reason);
            clientWaiting = clientApproved = false;
            if (clientManager) clientManager.StopClient();
        }

        internal static void Update()
        {
            double now = Time.realtimeSinceStartup;
            if (now < nextUpdate) return;
            nextUpdate = now + .1;
            foreach (var pair in sessions.ToArray())
            {
                if (now >= pair.Value.DisconnectAt) { sessions.Remove(pair.Key); pair.Key.Disconnect(); continue; }
                if (pair.Value.TimedOut(now)) { Reject(pair.Key, pair.Value, MissingPlugin); continue; }
                if (pair.Value.Authorized && !pair.Value.Awaiting && now >= pair.Value.NextChallenge)
                { pair.Value.Challenge(now, timeout); SendChallenge(pair.Key, pair.Value); }
            }
            if (clientWaiting && now >= clientDeadline) FailClient("Сервер не подтвердил совместимость карты. Нужен актуальный LegacyMapExpansion на сервере и клиенте.");
            if (!string.IsNullOrEmpty(failure))
            {
                string text = failure; failure = null;
                Plugin.Warn(text);
                FailureText = text;
                try
                {
                    var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BigCityLegacy");
                    var method = assembly?.GetType("NativeErrorDialog")?.GetMethod("Show", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string), typeof(string) }, null);
                    if (method != null && (bool)method.Invoke(null, new object[] { "Legacy Map Expansion", text })) ShowFailure = false;
                    else ShowFailure = true;
                }
                catch { ShowFailure = true; }
            }
        }

        internal static string FailureText { get; private set; }
        private static void Ping(ref string send)
        {
            if (NetManager.isServer) send = MapNetworkProtocol.AddPingFields(send, Plugin.Ready, Identity);
        }
    }
}
