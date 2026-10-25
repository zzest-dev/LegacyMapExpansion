using System;
using System.Text;
using LegacyMapExpansion;
using UnityEngine.Networking;

internal static class AdmissionTests
{
    private static int assertions;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static T RoundTrip<T>(T value) where T : MessageBase, new()
    {
        var writer = new NetworkWriter(); value.Serialize(writer);
        var copy = new T(); copy.Deserialize(new NetworkReader(writer.ToArray())); return copy;
    }
    private static int Main()
    {
        try
        {
            string identity = MapNetworkProtocol.Identity(Bytes("map"), Bytes("compat"), Bytes("ground"), Bytes("asset hashes"));
            Check(identity.Length == 64, "Identity format");
            Check(identity == MapNetworkProtocol.Identity(Bytes("map"), Bytes("compat"), Bytes("ground"), Bytes("asset hashes")), "Cross-platform common resources identity");
            Check(identity != MapNetworkProtocol.Identity(Bytes("different map"), Bytes("compat"), Bytes("ground"), Bytes("asset hashes")), "Geometry affects identity");
            Check(identity != MapNetworkProtocol.Identity(Bytes("map"), Bytes("different"), Bytes("ground"), Bytes("asset hashes")), "Compatibility affects identity");
            Check(identity != MapNetworkProtocol.Identity(Bytes("map"), Bytes("compat"), Bytes("different"), Bytes("asset hashes")), "Ground affects identity");
            Check(identity != MapNetworkProtocol.Identity(Bytes("map"), Bytes("compat"), Bytes("ground"), Bytes("different asset hashes")), "Asset bundle manifest affects identity");
            Check(MapNetworkProtocol.Identity(Bytes("map"), Bytes("compat"), Bytes("ground"), Bytes("assets\r\nmanifest")) ==
                MapNetworkProtocol.Identity(Bytes("map"), Bytes("compat"), Bytes("ground"), Bytes("assets\nmanifest")), "Windows/Linux manifest line endings are compatible");
            Check(MapNetworkProtocol.Validate(1, identity, true, identity) == null, "Matching plugin accepted");
            Check(MapNetworkProtocol.Validate(0, identity, true, identity) != null, "Old protocol rejected");
            Check(MapNetworkProtocol.Validate(1, identity, false, identity) != null, "Failed plugin rejected");
            Check(MapNetworkProtocol.Validate(1, "", true, identity) != null, "Missing plugin identity rejected");
            Check(MapNetworkProtocol.Validate(1, new string('0', 64), true, identity) != null, "Different map rejected");
            var session = new MapAdmissionSession(100, 8);
            Check(!session.Authorized && session.Awaiting && session.Nonce.Length == 32, "Default deny");
            session.ReadyRequested = true; session.PlayerRequested = 0;
            Check(!session.Authorized && session.ReadyRequested && session.PlayerRequested == 0, "Queued Ready/AddPlayer do not authorize a connection");
            Check(!session.TimedOut(107.99) && session.TimedOut(108), "Missing plugin deadline");
            Check(!session.IsCurrent("stale"), "Stale challenge rejected");
            Check(session.IsCurrent(session.Nonce), "Current challenge accepted");
            string oldNonce = session.Nonce;
            session.Accept(101, 30);
            Check(session.Authorized && !session.Awaiting && session.NextChallenge == 131, "Accept unlocks one session");
            Check(!session.IsCurrent(oldNonce), "Duplicate proof ignored");
            session.Challenge(131, 8);
            Check(session.Authorized && session.Awaiting && session.Nonce != oldNonce, "Heartbeat refreshes nonce");
            Check(!session.IsCurrent(oldNonce) && session.TimedOut(139), "Old proof cannot satisfy heartbeat");
            session.Reject(139);
            Check(!session.Authorized && !session.Awaiting && session.DisconnectAt == 139.25, "Rejected session immediately loses access");
            var second = new MapAdmissionSession(100, 8);
            Check(second.Nonce != session.Nonce && !second.Authorized, "Connection state is isolated");
            var challenge = RoundTrip(new MapChallenge { Protocol = 1, Nonce = second.Nonce, Identity = identity });
            Check(challenge.Protocol == 1 && challenge.Nonce == second.Nonce && challenge.Identity == identity, "UNet challenge serialization");
            var proof = RoundTrip(new MapProof { Protocol = 1, Nonce = second.Nonce, Identity = identity, Ready = true });
            Check(proof.Protocol == 1 && proof.Nonce == second.Nonce && proof.Ready && proof.Identity == identity, "UNet proof serialization");
            var result = RoundTrip(new MapResult { Nonce = second.Nonce, Accepted = false, Reason = "Установи плагин карты" });
            Check(!result.Accepted && result.Nonce == second.Nonce && result.Reason == "Установи плагин карты", "UNet rejection and UTF8");
            string ping = "ip:127.0.0.1|port:7800|count:1|max:16|";
            string extended = MapNetworkProtocol.AddPingFields(ping, true, identity);
            Check(extended.StartsWith(ping) && extended.Contains("lmeRequired:1|lmeReady:1|") && extended.Contains("lmeMap:" + identity), "Native ping metadata");
            Check(MapNetworkProtocol.AddPingFields(extended, true, identity) == extended, "Ping fields appended once");
            Check(MapNetworkProtocol.AddPingFields("{\"servers\":[]}", true, identity) == "{\"servers\":[]}", "Other UDP protocols unchanged");
            Check(MapNetworkProtocol.AddPingFields(ping, false, identity).Contains("lmeReady:0|"), "Failed map advertised unavailable");
            Console.WriteLine("MAP_ADMISSION_TESTS_OK assertions=" + assertions);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
