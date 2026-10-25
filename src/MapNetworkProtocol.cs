using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LegacyMapExpansion
{
    internal static class MapNetworkProtocol
    {
        internal const int Version = 1;
        internal const string Release = "1.0.0";
        internal const short ChallengeId = 27920;
        internal const short ProofId = 27921;
        internal const short ResultId = 27922;

        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static string Identity(byte[] map, byte[] compatibility, byte[] groundMaterials, byte[] manifest)
        {
            byte[] normalizedManifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).TrimStart('\ufeff').Replace("\r\n", "\n").Replace("\r", "\n"));
            return Hash(Encoding.UTF8.GetBytes("LegacyMapExpansion\n" + Version + "\n" + Release + "\n" +
                Hash(map) + "\n" + Hash(compatibility) + "\n" + Hash(groundMaterials) + "\n" + Hash(normalizedManifest)));
        }

        internal static string Validate(int version, string identity, bool ready, string expected)
        {
            if (version != Version) return "Несовместимый сетевой протокол карты. Обнови LegacyMapExpansion на клиенте и сервере.";
            if (!ready) return "Плагин карты на клиенте не загрузился. Проверь LegacyMapExpansion и его ресурсы.";
            if (string.IsNullOrEmpty(identity) || identity.Length != 64 || identity != expected)
                return "Версии карты клиента и сервера различаются. Установи актуальный LegacyMapExpansion 1.0.0.";
            return null;
        }

        internal static string AddPingFields(string response, bool ready, string identity)
        {
            if (string.IsNullOrEmpty(response) || !response.StartsWith("ip:", StringComparison.Ordinal) ||
                !response.Contains("|count:") || response.Contains("|lmeRequired:")) return response;
            return response + (response.EndsWith("|", StringComparison.Ordinal) ? "" : "|") +
                "lmeRequired:1|lmeReady:" + (ready ? "1" : "0") + "|lmeProtocol:" + Version +
                "|lmeVersion:" + Release + "|lmeMap:" + (identity ?? "") + "|";
        }
    }

    internal sealed class MapAdmissionSession
    {
        internal string Nonce { get; private set; }
        internal bool Authorized { get; private set; }
        internal bool Awaiting { get; private set; }
        internal double Deadline { get; private set; }
        internal double NextChallenge { get; private set; }
        internal double DisconnectAt = double.PositiveInfinity;
        internal bool ReadyRequested;
        internal short? PlayerRequested;

        internal MapAdmissionSession(double now, double timeout) { Challenge(now, timeout); }
        internal void Challenge(double now, double timeout)
        {
            Nonce = Guid.NewGuid().ToString("N"); Awaiting = true; Deadline = now + timeout;
        }
        internal bool IsCurrent(string nonce) => Awaiting && nonce == Nonce;
        internal bool TimedOut(double now) => Awaiting && now >= Deadline;
        internal void Accept(double now, double interval)
        {
            Authorized = true; Awaiting = false; NextChallenge = now + interval;
        }
        internal void Reject(double now)
        {
            Authorized = false; Awaiting = false; DisconnectAt = now + .25;
        }
    }
}
