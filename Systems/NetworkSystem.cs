using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ZstdSharp;

namespace ValhallaPerformance
{
    public class NetworkSystem : ISystem
    {
        private enum PrefabClass
        {
            Unknown,
            Ship,
            Mob,
            Important
        }

        private class PeerNetState
        {
            public float FarInterval;
            public int SyncBudget;
            public float LastQualityCheck;
        }

        private static readonly Dictionary<ZDOID, float> LastSendTime = new Dictionary<ZDOID, float>();
        private static readonly Dictionary<ZDOID, uint> LastRevision = new Dictionary<ZDOID, uint>();
        private static readonly Dictionary<int, PrefabClass> PrefabClassCache = new Dictionary<int, PrefabClass>();
        private static readonly List<ZDO> ZoneControlBuffer = new List<ZDO>(256);
        private static readonly List<ZNetPeer> PeerBuffer = new List<ZNetPeer>(16);
        private static readonly Dictionary<long, PeerNetState> PeerStates = new Dictionary<long, PeerNetState>();
        private static readonly Dictionary<Type, SocketAccess> SocketAccessCache = new Dictionary<Type, SocketAccess>();

        private sealed class SocketAccess
        {
            public FieldInfo CompressionField;
            public FieldInfo MaxSendQueueSizeField;
            public FieldInfo SendBufferSizeField;
            public FieldInfo SendQueueSizeField;
            public MethodInfo GetSendQueueSizeMethod;
            public MethodInfo GetConnectionQualityMethod;
            public MethodInfo FlushMethod;
            public bool LoggedTuningFallback;
        }

        private static readonly string[] ZoneControlPrefabs =
        {
            "zone_ctrl",
            "ZoneCtrl",
            "zonecontrol",
            "zone_control"
        };

        private static readonly Type ZdoPeerType = AccessTools.TypeByName("ZDOMan+ZDOPeer");
        private static readonly FieldInfo PeerWrapperPeerField = ZdoPeerType != null ? AccessTools.Field(ZdoPeerType, "m_peer") : null;
        private static readonly MethodInfo PeerGetRefPosMethod = AccessTools.Method(typeof(ZNetPeer), "GetRefPos");
        private static readonly MethodInfo ZNetGetNetStatsMethod = AccessTools.Method(typeof(ZNet), "GetNetStats");
        private static readonly MethodInfo ZNetGetConnectedPeersMethod = AccessTools.Method(typeof(ZNet), "GetConnectedPeers");
        private static readonly MethodInfo ZDOGetAllWithPrefabMethod = AccessTools.Method(typeof(ZDOMan), "GetAllZDOsWithPrefabIterative");
        private static readonly MethodInfo ZRpcGetSocketMethod = AccessTools.Method(typeof(ZRpc), "GetSocket");
        private static readonly FieldInfo ZDOManSendFpsField = AccessTools.Field(typeof(ZDOMan), "c_SendFPS");

        private static float _adaptiveFarInterval = 4f;
        private static int _adaptiveSyncBudget = 180;
        private static float _lastAdaptiveUpdate = -999f;
        private static float _lastCleanupUpdate = -999f;
        private static Vector3 _fallbackPlayerPos = Vector3.zero;

        private static int _sent;
        private static int _skipped;
        private static int _trimmed;

        // Zstd compression -- magic byte prefix for compressed payloads
        private const byte ZstdMagic = 0x5A;

        [ThreadStatic]
        private static Compressor _threadCompressor;
        [ThreadStatic]
        private static Decompressor _threadDecompressor;

        private static Compressor GetCompressor()
        {
            if (_threadCompressor == null)
                _threadCompressor = new Compressor(Mathf.Clamp(Cfg.ZstdCompressionLevel.Value, 1, 19));
            return _threadCompressor;
        }

        private static Decompressor GetDecompressor()
        {
            if (_threadDecompressor == null)
                _threadDecompressor = new Decompressor();
            return _threadDecompressor;
        }

        public void Init(Harmony harmony)
        {
            _adaptiveFarInterval = Mathf.Max(0.1f, Cfg.FarInterval.Value);
            _adaptiveSyncBudget = Mathf.Max(16, Cfg.AdaptiveSyncBudgetMax.Value);

            harmony.PatchAll(typeof(Patches));
            if (Cfg.EnableZstdCompression.Value)
                harmony.PatchAll(typeof(CompressionPatches));

            Plugin.Log.LogInfo($"[Network] Active zstd={Cfg.EnableZstdCompression.Value}");
        }

        public void Tick()
        {
            if (Player.m_localPlayer != null)
                _fallbackPlayerPos = Player.m_localPlayer.transform.position;

            float now = Time.unscaledTime;
            if (Cfg.AdaptiveNetworkControl.Value && now - _lastAdaptiveUpdate >= 1f)
            {
                RefreshAdaptiveState();
                _lastAdaptiveUpdate = now;
            }

            if (Cfg.ZoneOwnerManagement.Value &&
                ZNet.instance != null &&
                ZNet.instance.IsServer() &&
                StaggerScheduler.ShouldRun("network.zone_owner", Cfg.ZoneOwnerUpdateInterval.Value))
            {
                RebalanceZoneOwners();
            }

            if (Cfg.SteamSocketTuning.Value && StaggerScheduler.ShouldRun("network.transport_tune", 3f))
                ApplyTransportTuning();

            if (now - _lastCleanupUpdate >= 60f)
            {
                PurgeCaches(now);
                _lastCleanupUpdate = now;
            }

            if (!StaggerScheduler.ShouldRun("network.report", 240f))
                return;

            int total = _sent + _skipped;
            if (total <= 0 && _trimmed <= 0)
                return;

            Plugin.Log.LogInfo($"[Network] sent={_sent} skipped={_skipped} trimmed={_trimmed} far={GetFarInterval():F2}s budget={GetSyncBudget()} peers={PeerStates.Count}");
            _sent = 0;
            _skipped = 0;
            _trimmed = 0;
        }

        public void Cleanup()
        {
            LastSendTime.Clear();
            LastRevision.Clear();
            PrefabClassCache.Clear();
            ZoneControlBuffer.Clear();
            PeerBuffer.Clear();
            PeerStates.Clear();
            SocketAccessCache.Clear();

            _threadCompressor?.Dispose();
            _threadCompressor = null;
            _threadDecompressor?.Dispose();
            _threadDecompressor = null;
        }

        private static bool AllowSync(ZDO zdo, Vector3 receiverPos, float now, float peerFarInterval)
        {
            if (zdo == null || !zdo.IsValid())
                return false;

            if (Cfg.PriorityPlayers.Value && IsPlayerZdo(zdo))
            {
                _sent++;
                return true;
            }

            float distSq = (zdo.GetPosition() - receiverPos).sqrMagnitude;
            float nearSq = Cfg.NearRange.Value * Cfg.NearRange.Value;
            float farSq = Cfg.FarRange.Value * Cfg.FarRange.Value;

            if (distSq <= nearSq)
            {
                _sent++;
                return true;
            }

            if (distSq > farSq)
            {
                ZDOID uid = zdo.m_uid;
                float farInterval = peerFarInterval;
                if (LastSendTime.TryGetValue(uid, out float last) && now - last < farInterval)
                {
                    _skipped++;
                    return false;
                }

                LastSendTime[uid] = now;
            }

            if (Cfg.SkipUnchanged.Value)
            {
                ZDOID uid = zdo.m_uid;
                uint rev = (uint)zdo.DataRevision;
                if (LastRevision.TryGetValue(uid, out uint old) && old == rev)
                {
                    _skipped++;
                    return false;
                }

                LastRevision[uid] = rev;
            }

            _sent++;
            return true;
        }

        private static int CompareByPriority(ZDO a, ZDO b, Vector3 receiverPos)
        {
            int scoreA = GetPriorityScore(a, receiverPos);
            int scoreB = GetPriorityScore(b, receiverPos);
            if (scoreA != scoreB)
                return scoreB.CompareTo(scoreA);

            float distA = (a.GetPosition() - receiverPos).sqrMagnitude;
            float distB = (b.GetPosition() - receiverPos).sqrMagnitude;
            return distA.CompareTo(distB);
        }

        private static int GetPriorityScore(ZDO zdo, Vector3 receiverPos)
        {
            if (zdo == null)
                return int.MinValue;

            int score = 0;
            if (Cfg.PriorityPlayers.Value && IsPlayerZdo(zdo))
                score += 10000;

            PrefabClass cls = GetPrefabClass(zdo);
            if (Cfg.PrioritizeShips.Value && cls == PrefabClass.Ship)
                score += 5000;
            if (Cfg.PrioritizeMobs.Value && cls == PrefabClass.Mob)
                score += 3000;
            if (Cfg.PrioritizeImportant.Value && cls == PrefabClass.Important)
                score += 1800;

            float dist = Vector3.Distance(zdo.GetPosition(), receiverPos);
            score += Mathf.Clamp(1500 - Mathf.RoundToInt(dist * 10f), 0, 1500);
            return score;
        }

        private static PrefabClass GetPrefabClass(ZDO zdo)
        {
            int hash = zdo.GetPrefab();
            if (hash == 0)
                return PrefabClass.Unknown;

            if (PrefabClassCache.TryGetValue(hash, out PrefabClass cached))
                return cached;

            PrefabClass cls = PrefabClass.Unknown;
            try
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
                if (prefab != null)
                {
                    string n = prefab.name.ToLowerInvariant();

                    if (n.Contains("ship") || n.Contains("boat") || n.Contains("raft") || n.Contains("karve") || n.Contains("longship"))
                    {
                        cls = PrefabClass.Ship;
                    }
                    else if (prefab.GetComponent<Character>() != null && prefab.GetComponent<Player>() == null)
                    {
                        cls = PrefabClass.Mob;
                    }
                    else if (n.Contains("portal") || n.Contains("chest") || n.Contains("bed") ||
                             n.Contains("workbench") || n.Contains("stonecutter") || n.Contains("smelter") ||
                             n.Contains("station") || n.Contains("spawner"))
                    {
                        cls = PrefabClass.Important;
                    }
                }
            }
            catch { }

            PrefabClassCache[hash] = cls;
            return cls;
        }

        private static bool IsPlayerZdo(ZDO zdo)
        {
            try
            {
                return zdo.GetInt(ZDOVars.s_playerID, 0) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static Vector3 GetPeerReferencePosition(object peerWrapper)
        {
            if (peerWrapper != null && PeerWrapperPeerField != null)
            {
                try
                {
                    ZNetPeer peer = PeerWrapperPeerField.GetValue(peerWrapper) as ZNetPeer;
                    if (peer != null && PeerGetRefPosMethod != null)
                        return (Vector3)PeerGetRefPosMethod.Invoke(peer, null);
                }
                catch { }
            }

            return _fallbackPlayerPos;
        }

        private static long GetPeerUid(object peerWrapper)
        {
            if (peerWrapper != null && PeerWrapperPeerField != null)
            {
                try
                {
                    ZNetPeer peer = PeerWrapperPeerField.GetValue(peerWrapper) as ZNetPeer;
                    if (peer != null)
                        return peer.m_uid;
                }
                catch { }
            }

            return 0;
        }

        private static float GetFarInterval()
        {
            return Cfg.AdaptiveNetworkControl.Value
                ? Mathf.Clamp(_adaptiveFarInterval, 0.25f, 30f)
                : Mathf.Max(0.1f, Cfg.FarInterval.Value);
        }

        private static int GetSyncBudget()
        {
            if (Cfg.AdaptiveNetworkControl.Value)
                return Mathf.Max(16, _adaptiveSyncBudget);

            return Mathf.Max(16, Cfg.AdaptiveSyncBudgetMax.Value);
        }

        private static float GetPeerFarInterval(long uid)
        {
            if (Cfg.AdaptiveNetworkControl.Value && PeerStates.TryGetValue(uid, out PeerNetState state))
                return Mathf.Clamp(state.FarInterval, 0.25f, 30f);
            return GetFarInterval();
        }

        private static int GetPeerSyncBudget(long uid)
        {
            if (Cfg.AdaptiveNetworkControl.Value && PeerStates.TryGetValue(uid, out PeerNetState state))
                return Mathf.Max(16, state.SyncBudget);
            return GetSyncBudget();
        }

        private static void RefreshAdaptiveState()
        {
            float baselineInterval = Mathf.Max(0.2f, Cfg.FarInterval.Value);
            int baselineBudget = Mathf.Max(16, Cfg.AdaptiveSyncBudgetMax.Value);

            float intervalMin = Mathf.Max(0.2f, Cfg.AdaptiveFarIntervalMin.Value);
            float intervalMax = Mathf.Max(intervalMin, Cfg.AdaptiveFarIntervalMax.Value);
            int budgetMin = Mathf.Max(16, Cfg.AdaptiveSyncBudgetMin.Value);
            int budgetMax = Mathf.Max(budgetMin, Cfg.AdaptiveSyncBudgetMax.Value);

            // Per-peer adaptive state
            if (TryGetPeers(PeerBuffer) && PeerBuffer.Count > 0)
            {
                float worstStress = 0f;
                for (int i = 0; i < PeerBuffer.Count; i++)
                {
                    ZNetPeer peer = PeerBuffer[i];
                    if (peer == null)
                        continue;

                    float pingMs = GetPeerPingMs(peer);
                    float pingT = Mathf.InverseLerp(Cfg.AdaptivePingGoodMs.Value,
                        Mathf.Max(Cfg.AdaptivePingGoodMs.Value + 1f, Cfg.AdaptivePingBadMs.Value), pingMs);
                    float stress = Mathf.Clamp01(pingT);

                    float peerInterval = Mathf.Lerp(intervalMin, intervalMax, stress);
                    int peerBudget = Mathf.RoundToInt(Mathf.Lerp(budgetMax, budgetMin, stress));

                    long uid = peer.m_uid;
                    if (!PeerStates.TryGetValue(uid, out PeerNetState state))
                    {
                        state = new PeerNetState();
                        PeerStates[uid] = state;
                    }

                    state.FarInterval = peerInterval;
                    state.SyncBudget = peerBudget;
                    state.LastQualityCheck = Time.unscaledTime;

                    if (stress > worstStress)
                        worstStress = stress;
                }

                // Update global fallbacks based on worst peer
                _adaptiveFarInterval = Mathf.Lerp(intervalMin, intervalMax, worstStress);
                _adaptiveSyncBudget = Mathf.RoundToInt(Mathf.Lerp(budgetMax, budgetMin, worstStress));
            }
            else
            {
                // No peers -- use global stats as before
                if (ZNet.instance == null)
                {
                    _adaptiveFarInterval = baselineInterval;
                    _adaptiveSyncBudget = baselineBudget;
                    return;
                }

                if (!TryGetNetStats(out float localQuality, out float remoteQuality, out int pingValue, out _, out _))
                {
                    _adaptiveFarInterval = baselineInterval;
                    _adaptiveSyncBudget = baselineBudget;
                    return;
                }

                float pingMs = Mathf.Max(0f, pingValue);
                float loss = Mathf.Clamp01(1f - Mathf.Min(localQuality, remoteQuality));

                float pingT = Mathf.InverseLerp(Cfg.AdaptivePingGoodMs.Value,
                    Mathf.Max(Cfg.AdaptivePingGoodMs.Value + 1f, Cfg.AdaptivePingBadMs.Value), pingMs);
                float lossT = Mathf.InverseLerp(0f, Mathf.Max(0.01f, Cfg.AdaptiveLossBad.Value), loss);
                float stress = Mathf.Clamp01(Mathf.Max(pingT, lossT));

                _adaptiveFarInterval = Mathf.Lerp(intervalMin, intervalMax, stress);
                _adaptiveSyncBudget = Mathf.RoundToInt(Mathf.Lerp(budgetMax, budgetMin, stress));
            }
        }

        private static void PurgeCaches(float now)
        {
            float ttl = Mathf.Max(10f, GetFarInterval() * 12f);
            var stale = new List<ZDOID>();

            foreach (var kv in LastSendTime)
            {
                if (now - kv.Value > ttl)
                    stale.Add(kv.Key);
            }

            foreach (ZDOID key in stale)
            {
                LastSendTime.Remove(key);
                LastRevision.Remove(key);
            }

            // Purge peer states for disconnected peers
            if (TryGetPeers(PeerBuffer))
            {
                var connectedUids = new HashSet<long>();
                for (int i = 0; i < PeerBuffer.Count; i++)
                {
                    if (PeerBuffer[i] != null)
                        connectedUids.Add(PeerBuffer[i].m_uid);
                }

                var stalePeers = new List<long>();
                foreach (var kv in PeerStates)
                {
                    if (!connectedUids.Contains(kv.Key))
                        stalePeers.Add(kv.Key);
                }

                foreach (long uid in stalePeers)
                    PeerStates.Remove(uid);
            }
            else
            {
                PeerStates.Clear();
            }
        }

        private static void RebalanceZoneOwners()
        {
            if (ZDOMan.instance == null || ZDOGetAllWithPrefabMethod == null)
                return;

            if (!TryCollectZoneControls(ZoneControlBuffer) || ZoneControlBuffer.Count == 0)
                return;

            if (!TryGetPeers(PeerBuffer) || PeerBuffer.Count == 0)
                return;

            int changed = 0;
            float maxDist = Mathf.Max(16f, Cfg.ZoneOwnerMaxDistance.Value);
            float distancePenalty = Mathf.Max(0f, Cfg.ZoneOwnerPingGainMs.Value);

            for (int i = 0; i < ZoneControlBuffer.Count; i++)
            {
                ZDO zone = ZoneControlBuffer[i];
                if (zone == null || !zone.IsValid())
                    continue;

                Vector3 zonePos = zone.GetPosition();
                ZNetPeer best = null;
                float bestScore = float.MaxValue;

                for (int p = 0; p < PeerBuffer.Count; p++)
                {
                    ZNetPeer peer = PeerBuffer[p];
                    if (peer == null)
                        continue;

                    Vector3 peerPos = GetPeerPosition(peer);
                    float dist = Vector3.Distance(zonePos, peerPos);
                    if (dist > maxDist)
                        continue;

                    float ping = GetPeerPingMs(peer);
                    float score = ping + (dist / 100f) * distancePenalty;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = peer;
                    }
                }

                if (best == null)
                    continue;

                long uid = best.m_uid;
                if (zone.GetOwner() == uid)
                    continue;

                zone.SetOwner(uid);
                changed++;
            }

            if (changed > 0)
                Plugin.Log.LogInfo($"[Network] Reassigned {changed} zone owners");
        }

        private static bool TryCollectZoneControls(List<ZDO> output)
        {
            output.Clear();

            foreach (string prefab in ZoneControlPrefabs)
            {
                int index = 0;
                bool done = false;

                while (!done)
                {
                    object[] args = { prefab, output, index };
                    done = (bool)ZDOGetAllWithPrefabMethod.Invoke(ZDOMan.instance, args);
                    index = (int)args[2];

                    if (index < 0 || output.Count > 100000)
                        break;
                }

                if (output.Count > 0)
                    return true;
            }

            return output.Count > 0;
        }

        private static bool TryGetPeers(List<ZNetPeer> peers)
        {
            peers.Clear();

            if (ZNet.instance == null)
                return false;

            try
            {
                if (ZNetGetConnectedPeersMethod != null)
                {
                    List<ZNetPeer> list = ZNetGetConnectedPeersMethod.Invoke(ZNet.instance, null) as List<ZNetPeer>;
                    if (list != null)
                        peers.AddRange(list);
                }
                else
                {
                    List<ZNetPeer> list = ZNet.instance.GetConnectedPeers();
                    if (list != null)
                        peers.AddRange(list);
                }
            }
            catch { }

            return peers.Count > 0;
        }

        private static Vector3 GetPeerPosition(ZNetPeer peer)
        {
            if (peer == null)
                return _fallbackPlayerPos;

            try
            {
                if (PeerGetRefPosMethod != null)
                    return (Vector3)PeerGetRefPosMethod.Invoke(peer, null);
            }
            catch { }

            return _fallbackPlayerPos;
        }

        private static float GetPeerPingMs(ZNetPeer peer)
        {
            if (peer?.m_rpc == null || ZRpcGetSocketMethod == null)
                return Cfg.AdaptivePingBadMs.Value;

            try
            {
                object socket = ZRpcGetSocketMethod.Invoke(peer.m_rpc, null);
                if (socket == null)
                    return Cfg.AdaptivePingBadMs.Value;

                SocketAccess access = GetSocketAccess(socket.GetType());
                if (!TryGetConnectionQuality(socket, access, out _, out _, out int ping, out _, out _))
                    return Cfg.AdaptivePingBadMs.Value;

                return Mathf.Max(0f, ping);
            }
            catch
            {
                return Cfg.AdaptivePingBadMs.Value;
            }
        }

        private static void ApplyTransportTuning()
        {
            if (ZDOManSendFpsField != null)
            {
                try
                {
                    float fps = Mathf.Clamp(Cfg.NetworkSendFPS.Value, 5f, 60f);
                    ZDOManSendFpsField.SetValue(null, fps);
                }
                catch { }
            }

            if (!TryGetPeers(PeerBuffer))
                return;

            for (int i = 0; i < PeerBuffer.Count; i++)
                ConfigurePeerTransport(PeerBuffer[i]);
        }

        private static void ConfigurePeerTransport(ZNetPeer peer)
        {
            if (peer?.m_rpc == null || ZRpcGetSocketMethod == null)
                return;

            object socket = null;
            try
            {
                socket = ZRpcGetSocketMethod.Invoke(peer.m_rpc, null);
            }
            catch { }

            if (socket == null)
                return;

            Type socketType = socket.GetType();
            SocketAccess access = GetSocketAccess(socketType);
            string socketName = socketType.Name;
            bool isPlayFab = socketName.IndexOf("PlayFab", StringComparison.OrdinalIgnoreCase) >= 0;

            if (Cfg.EnableTrafficCompression.Value && (!Cfg.CompressionPlayFabOnly.Value || isPlayFab))
                ForceCompression(socket, access);

            if (!Cfg.SteamSocketTuning.Value)
                return;

            bool hasLegacyFields = false;
            hasLegacyFields |= TrySetIntField(socket, access?.MaxSendQueueSizeField, Cfg.SendBufferSize.Value * 4);
            hasLegacyFields |= TrySetIntField(socket, access?.SendBufferSizeField, Cfg.SendBufferSize.Value);
            hasLegacyFields |= TrySetIntField(socket, access?.SendQueueSizeField, Cfg.SendBufferSize.Value * 2);

            if (!hasLegacyFields && access != null && !access.LoggedTuningFallback)
            {
                access.LoggedTuningFallback = true;
                Plugin.Log.LogInfo($"[Network] Socket tuning fallback on {socketType.Name}: legacy queue fields not found; using flush-based tuning.");
            }

            TryFlushLargeSendQueue(socket, access, Cfg.SendBufferSize.Value * 2);
        }

        private static void ForceCompression(object socket, SocketAccess access)
        {
            if (socket == null || access?.CompressionField == null || access.CompressionField.FieldType != typeof(bool))
                return;

            try
            {
                access.CompressionField.SetValue(socket, true);
            }
            catch { }
        }

        private static bool TrySetIntField(object target, FieldInfo field, int minValue)
        {
            if (target == null || field == null || field.FieldType != typeof(int))
                return false;

            try
            {
                int old = (int)field.GetValue(target);
                if (old < minValue)
                    field.SetValue(target, minValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryFlushLargeSendQueue(object socket, SocketAccess access, int threshold)
        {
            if (socket == null)
                return;

            try
            {
                int size;
                if (socket is ISocket typedSocket)
                {
                    size = typedSocket.GetSendQueueSize();
                }
                else
                {
                    MethodInfo queueSizeMethod = access?.GetSendQueueSizeMethod;
                    if (queueSizeMethod == null)
                        return;
                    size = Convert.ToInt32(queueSizeMethod.Invoke(socket, null));
                }

                if (size < threshold)
                    return;

                if (socket is ISocket flushSocket)
                {
                    flushSocket.Flush();
                }
                else
                {
                    access?.FlushMethod?.Invoke(socket, null);
                }
            }
            catch { }
        }

        private static SocketAccess GetSocketAccess(Type socketType)
        {
            if (socketType == null)
                return null;

            if (SocketAccessCache.TryGetValue(socketType, out SocketAccess cached))
                return cached;

            var access = new SocketAccess
            {
                CompressionField = FindField(socketType, "m_useCompression"),
                MaxSendQueueSizeField = FindField(socketType, "m_maxSendQueueSize", "m_maxQueuedSendSize", "m_maxQueuedSendBytes"),
                SendBufferSizeField = FindField(socketType, "m_sendBufferSize", "m_sendBufferBytes"),
                SendQueueSizeField = FindField(socketType, "m_sendQueueSize", "m_sendQueueBytes", "m_queuedSendBytes"),
                GetSendQueueSizeMethod = FindMethod(socketType, "GetSendQueueSize"),
                GetConnectionQualityMethod = FindConnectionQualityMethod(socketType),
                FlushMethod = FindMethod(socketType, "Flush")
            };

            SocketAccessCache[socketType] = access;
            return access;
        }

        private static FieldInfo FindField(Type type, params string[] candidates)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (int i = 0; i < candidates.Length; i++)
            {
                FieldInfo field = type.GetField(candidates[i], flags);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return type.GetMethod(name, flags, null, Type.EmptyTypes, null);
        }

        private static MethodInfo FindConnectionQualityMethod(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo noArgFallback = null;

            MethodInfo[] methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, "GetConnectionQuality", StringComparison.Ordinal))
                    continue;

                int paramCount = method.GetParameters().Length;
                if (paramCount == 5)
                    return method;

                if (paramCount == 0 && noArgFallback == null)
                    noArgFallback = method;
            }

            return noArgFallback;
        }

        private static bool TryGetConnectionQuality(object socket, SocketAccess access,
            out float localQuality, out float remoteQuality, out int ping, out float outByteSec, out float inByteSec)
        {
            localQuality = 1f;
            remoteQuality = 1f;
            ping = 0;
            outByteSec = 0f;
            inByteSec = 0f;

            if (socket == null)
                return false;

            try
            {
                if (socket is ISocket typedSocket)
                {
                    typedSocket.GetConnectionQuality(out localQuality, out remoteQuality, out ping, out outByteSec, out inByteSec);
                    return true;
                }
            }
            catch { }

            MethodInfo qualityMethod = access?.GetConnectionQualityMethod;
            if (qualityMethod == null)
                return false;

            try
            {
                ParameterInfo[] parameters = qualityMethod.GetParameters();
                if (parameters.Length == 5)
                {
                    object[] args = { 1f, 1f, 0, 0f, 0f };
                    object ret = qualityMethod.Invoke(socket, args);
                    if (qualityMethod.ReturnType == typeof(bool) && ret is bool ok && !ok)
                        return false;

                    localQuality = Convert.ToSingle(args[0]);
                    remoteQuality = Convert.ToSingle(args[1]);
                    ping = Convert.ToInt32(args[2]);
                    outByteSec = Convert.ToSingle(args[3]);
                    inByteSec = Convert.ToSingle(args[4]);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryGetNetStats(out float localQuality, out float remoteQuality, out int ping, out float outByteSec, out float inByteSec)
        {
            localQuality = 1f;
            remoteQuality = 1f;
            ping = 0;
            outByteSec = 0f;
            inByteSec = 0f;

            if (ZNet.instance == null)
                return false;

            try
            {
                ZNet.instance.GetNetStats(out localQuality, out remoteQuality, out ping, out outByteSec, out inByteSec);
                return true;
            }
            catch { }

            if (ZNetGetNetStatsMethod == null)
                return false;

            try
            {
                ParameterInfo[] parameters = ZNetGetNetStatsMethod.GetParameters();
                if (parameters.Length != 5)
                    return false;

                object[] args = { 1f, 1f, 0, 0f, 0f };
                object ret = ZNetGetNetStatsMethod.Invoke(ZNet.instance, args);
                if (ZNetGetNetStatsMethod.ReturnType == typeof(bool) && ret is bool ok && !ok)
                    return false;

                localQuality = Convert.ToSingle(args[0]);
                remoteQuality = Convert.ToSingle(args[1]);
                ping = Convert.ToInt32(args[2]);
                outByteSec = Convert.ToSingle(args[3]);
                inByteSec = Convert.ToSingle(args[4]);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // --- Zstd compression helpers ---

        private static byte[] CompressPayload(byte[] data)
        {
            if (data == null || data.Length < Cfg.CompressionMinBytes.Value)
                return data;

            try
            {
                var compressor = GetCompressor();
                byte[] compressed = compressor.Wrap(data).ToArray();

                // Only use compressed if it's actually smaller
                if (compressed.Length + 1 >= data.Length)
                    return data;

                // Prepend magic byte
                byte[] result = new byte[compressed.Length + 1];
                result[0] = ZstdMagic;
                Buffer.BlockCopy(compressed, 0, result, 1, compressed.Length);
                return result;
            }
            catch
            {
                return data;
            }
        }

        internal static byte[] DecompressPayload(byte[] data)
        {
            if (data == null || data.Length < 2 || data[0] != ZstdMagic)
                return data;

            try
            {
                var decompressor = GetDecompressor();
                byte[] compressed = new byte[data.Length - 1];
                Buffer.BlockCopy(data, 1, compressed, 0, compressed.Length);
                return decompressor.Unwrap(compressed).ToArray();
            }
            catch
            {
                return data;
            }
        }

        [HarmonyPatch]
        private static class Patches
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(ZDOMan), "CreateSyncList")]
            private static void Postfix_CreateSyncList(object __0, List<ZDO> __1)
            {
                if (__1 == null || __1.Count == 0)
                    return;

                Vector3 receiverPos = GetPeerReferencePosition(__0);
                float now = Time.unscaledTime;
                long peerUid = GetPeerUid(__0);

                if (Cfg.PriorityPlayers.Value || Cfg.PrioritizeShips.Value || Cfg.PrioritizeMobs.Value || Cfg.PrioritizeImportant.Value)
                    __1.Sort((a, b) => CompareByPriority(a, b, receiverPos));

                int budget = GetPeerSyncBudget(peerUid);
                float farInterval = GetPeerFarInterval(peerUid);
                int write = 0;

                for (int i = 0; i < __1.Count; i++)
                {
                    ZDO zdo = __1[i];
                    if (!AllowSync(zdo, receiverPos, now, farInterval))
                        continue;

                    __1[write++] = zdo;
                    if (write >= budget)
                        break;
                }

                if (write < __1.Count)
                {
                    int removed = __1.Count - write;
                    __1.RemoveRange(write, removed);
                    _trimmed += removed;
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
            private static void Postfix_OnNewConnection(ZNetPeer peer)
            {
                ConfigurePeerTransport(peer);
            }
        }

        [HarmonyPatch]
        private static class CompressionPatches
        {
            // Hook outgoing: ZPackage.GetArray() is called when preparing data to send.
            // We patch ZRpc.SendPackage to compress the payload before it hits the socket.
            [HarmonyPrefix]
            [HarmonyPatch(typeof(ZRpc), "SendPackage")]
            private static void Prefix_SendPackage(ref ZPackage __0)
            {
                if (__0 == null)
                    return;

                try
                {
                    byte[] data = __0.GetArray();
                    if (data == null || data.Length < Cfg.CompressionMinBytes.Value)
                        return;

                    byte[] compressed = CompressPayload(data);
                    if (compressed != data)
                    {
                        __0 = new ZPackage(compressed);
                    }
                }
                catch { }
            }

            // Hook incoming: patch ZRpc where it reads an incoming package.
            // ZRpc.HandlePackage receives a ZPackage from the socket.
            [HarmonyPrefix]
            [HarmonyPatch(typeof(ZRpc), "HandlePackage")]
            private static void Prefix_HandlePackage(ref ZPackage __0)
            {
                if (__0 == null)
                    return;

                try
                {
                    byte[] data = __0.GetArray();
                    if (data == null || data.Length < 2 || data[0] != ZstdMagic)
                        return;

                    byte[] decompressed = DecompressPayload(data);
                    if (decompressed != data)
                    {
                        __0 = new ZPackage(decompressed);
                    }
                }
                catch { }
            }
        }
    }
}
