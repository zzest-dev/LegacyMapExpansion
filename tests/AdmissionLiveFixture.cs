using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

public static class AdmissionLiveFixture
{
    private const int Port = 27841;
    private static string root, game;
    private static Assembly plugin, gameAssembly;
    private static Type admission, netManagerType, proofType;
    private static object manager, clientManager, harmony;
    private static Type harmonyType;
    private static NetworkClient client;
    private static int stage, mode, readyCount, playerCount, checks;
    private static double started, stageStarted;
    private static bool connected, disconnected;
    private static string identity = new string('a', 64);
    private static GameObject serverObject, clientObject;

    private static object Call(string name, params object[] args)
        => admission.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++; Debug.Log("MAP_LIVE_CHECK " + message);
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string directory in new[] { Path.Combine(game, "BepInEx/core"), Path.Combine(game, "game_Data/Managed") })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
    public static void Run()
    {
        try
        {
            root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            game = @"C:\Program Files (x86)\Steam\steamapps\common\MadOut2_BigCityOnline";
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            gameAssembly = Assembly.LoadFrom(Path.Combine(game, "game_Data/Managed/Assembly-CSharp.dll"));
            plugin = Assembly.LoadFrom(Path.Combine(root, "bin/Release/LegacyMapExpansion.dll"));
            admission = plugin.GetType("LegacyMapExpansion.MapNetworkAdmission", true);
            proofType = plugin.GetType("LegacyMapExpansion.MapProof", true);
            var pluginType = plugin.GetType("LegacyMapExpansion.Plugin", true);
            pluginType.GetField("Ready", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
            admission.GetField("Identity", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, identity);
            Call("Install", 1f, .5f);
            netManagerType = gameAssembly.GetType("NetManager", true);
            serverObject = new GameObject("AdmissionServer"); serverObject.SetActive(false);
            manager = serverObject.AddComponent(netManagerType);
            clientObject = new GameObject("AdmissionClient"); clientObject.SetActive(false);
            clientManager = clientObject.AddComponent(netManagerType);
            netManagerType.GetField("me", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SetValue(null, manager);
            var harmonyAssembly = Assembly.LoadFrom(Path.Combine(game, "BepInEx/core/0Harmony.dll"));
            harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", true);
            harmony = Activator.CreateInstance(harmonyType, new object[] { "map.live.fixture" });
            Hook(typeof(NetworkManager).GetMethod("OnServerReady"), nameof(RecordReady));
            Hook(netManagerType.GetMethod("OnServerAddPlayer", new[] { typeof(NetworkConnection), typeof(short) }), nameof(RecordPlayer));
            Hook(netManagerType.GetMethod("OnClientConnect"), nameof(ResumeClient));
            pluginType.GetField("Ready", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
            Check(!(bool)typeof(NetworkManager).GetMethod("StartServer", Type.EmptyTypes).Invoke(manager, null), "Server start fails closed if map resources failed");
            pluginType.GetField("Ready", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
            Check(NetworkServer.Listen("127.0.0.1", Port), "Local UNet server listens on loopback");
            Call("ServerStarted");
            NetworkServer.RegisterHandler(MsgType.Connect, message => Call("ServerConnected", message.conn));
            NetworkServer.RegisterHandler(MsgType.Disconnect, message => Call("ServerDisconnected", message.conn));
            NetworkServer.RegisterHandler(MsgType.Ready, message => ((NetworkManager)manager).OnServerReady(message.conn));
            NetworkServer.RegisterHandler(MsgType.AddPlayer, message => ((NetworkManager)manager).OnServerAddPlayer(message.conn, message.ReadMessage<AddPlayerMessage>().playerControllerId));
            started = EditorApplication.timeSinceStartup;
            Connect(0);
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error); }
    }

    private static void Hook(MethodInfo original, string name)
    {
        var hmType = harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod", true);
        var prefix = Activator.CreateInstance(hmType, new object[] { typeof(AdmissionLiveFixture).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static) });
        hmType.GetField("priority").SetValue(prefix, 0);
        var patch = harmonyType.GetMethods().First(method => method.Name == "Patch" && method.GetParameters()[0].ParameterType == typeof(MethodBase));
        var args = new object[patch.GetParameters().Length]; args[0] = original; args[1] = prefix;
        patch.Invoke(harmony, args);
    }
    private static bool RecordReady(NetworkConnection conn, bool __runOriginal) { if (__runOriginal) readyCount++; return false; }
    private static bool RecordPlayer(NetworkConnection conn, bool __runOriginal) { if (__runOriginal) playerCount++; return false; }
    private static bool ResumeClient(NetworkConnection conn, bool __runOriginal = true)
    {
        if (!__runOriginal) return false;
        conn.Send(MsgType.Ready, new EmptyMessage());
        conn.Send(MsgType.AddPlayer, new AddPlayerMessage { playerControllerId = 0 });
        return false;
    }
    private static void Connect(int kind)
    {
        mode = kind; connected = disconnected = false; readyCount = playerCount = 0;
        client = new NetworkClient();
        if (kind == 2) Call("ClientStarted", client);
        else
        {
            client.RegisterHandler(27920, message => {
                if (mode != 1) return;
                var reader = message.reader;
                reader.ReadInt32(); string nonce = reader.ReadString(); reader.ReadString();
                var proof = Activator.CreateInstance(proofType);
                proofType.GetField("Protocol", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proof, 1);
                proofType.GetField("Nonce", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proof, nonce);
                proofType.GetField("Identity", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proof, new string('b', 64));
                proofType.GetField("Ready", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proof, true);
                message.conn.Send(27921, (MessageBase)proof);
            });
            client.RegisterHandler(27922, message => { });
        }
        client.RegisterHandler(MsgType.Connect, message => {
            connected = true;
            if (mode == 2) ((NetworkManager)clientManager).OnClientConnect(message.conn);
            else ResumeClient(message.conn);
        });
        client.RegisterHandler(MsgType.Disconnect, message => disconnected = true);
        client.RegisterHandler(MsgType.Error, message => Debug.Log("MAP_LIVE_TRANSPORT_ERROR " + message.ReadMessage<ErrorMessage>().errorCode));
        stageStarted = EditorApplication.timeSinceStartup;
        client.Connect("127.0.0.1", Port);
    }
    private static void Pump()
    {
        typeof(NetworkServer).GetMethod("Update", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        typeof(NetworkClient).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(client, null);
        Call("Update");
    }
    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 25) throw new Exception("UNet integration test timeout at stage " + stage);
            Pump();
            double elapsed = EditorApplication.timeSinceStartup - stageStarted;
            if (stage == 0 && disconnected)
            {
                Check(connected && readyCount == 0 && playerCount == 0, "Client without plugin cannot become ready or spawn through direct IP");
                client.Shutdown(); stage = 1; Connect(1);
            }
            else if (stage == 1 && disconnected)
            {
                Check(connected && readyCount == 0 && playerCount == 0, "Different-map client rejected before Ready/AddPlayer");
                client.Shutdown(); stage = 2; Connect(2);
            }
            else if (stage == 2 && readyCount == 1 && playerCount == 1)
            {
                Check(!disconnected, "Matching plugin completes real client/server challenge and spawns once");
                stage = 3; stageStarted = EditorApplication.timeSinceStartup;
            }
            else if (stage == 3 && elapsed > 1.5)
            {
                Check(!disconnected && readyCount == 1 && playerCount == 1, "Heartbeat keeps compatible player connected without duplicate spawn");
                client.RegisterHandler(27920, message => { });
                stage = 4; stageStarted = EditorApplication.timeSinceStartup;
            }
            else if (stage == 4 && disconnected)
            {
                Check(readyCount == 1 && playerCount == 1, "Stopping plugin heartbeat disconnects an admitted client");
                Finish(null);
            }
        }
        catch (Exception error) { Finish(error); }
    }
    private static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        if (error != null) Debug.LogException(error);
        else Debug.Log("MAP_ADMISSION_LIVE_OK checks=" + checks);
        try { client?.Shutdown(); NetworkServer.Shutdown(); }
        catch (Exception cleanup) { Debug.LogException(cleanup); }
        try { if (harmony != null) harmonyType.GetMethod("UnpatchSelf").Invoke(harmony, null); }
        catch { }
        AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        if (serverObject) UnityEngine.Object.DestroyImmediate(serverObject);
        if (clientObject) UnityEngine.Object.DestroyImmediate(clientObject);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
