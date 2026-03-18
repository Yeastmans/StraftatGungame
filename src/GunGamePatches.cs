using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet;
using FishNet.Object;
using HarmonyLib;
using UnityEngine;

namespace GunGameMod
{
    public static class GunGamePatches
    {
        private static Harmony _harmony;

        private static Dictionary<int, int> _killsPerPlayer = new Dictionary<int, int>();
        private static HashSet<int> _waitingForSetStartTime = new HashSet<int>();
        private static HashSet<int> _pendingPlaceableReplace = new HashSet<int>();
        private static Dictionary<int, GameObject> _playerRagdolls = new Dictionary<int, GameObject>();
        private static bool _winSequenceInProgress = false;
        private static HashSet<int> _respawnInProgress = new HashSet<int>();

        private static MethodInfo _miCmdRespawn;
        private static MethodInfo _miRoundWon;
        private static FieldInfo _fiSpawnedObject;
        private static MethodInfo _miUnsubscribeFromInput;
        private static MethodInfo _miSpawnPlayer4Args;
        private static MethodInfo _miReturnSpawnPoint;
        private static MethodInfo _miRpcLogic_DropObjectServer;
        private static MethodInfo _miMatchLogsSendToAll;

        public static void Apply()
        {
            if (_harmony != null) return;
            _harmony = new Harmony("com.modder.gungame.patches");

            CacheReflectionMethods();

            try
            {
                PatchPrefix(typeof(GameManager), "WaitForDraw", nameof(GameManager_WaitForDraw_Prefix));
                PatchPrefix(typeof(GameManager), "RpcLogic___PlayerDied_3316948804", nameof(GameManager_RpcLogic_PlayerDied_Prefix));

                if (_miCmdRespawn != null)
                    _harmony.Patch(_miCmdRespawn, prefix: new HarmonyMethod(typeof(GunGamePatches).GetMethod(nameof(PlayerManager_RpcLogic_CmdRespawn_Prefix), BindingFlags.Public | BindingFlags.Static)));

                PatchPrefix(typeof(RoundManager), "CmdEndRound", nameof(RoundManager_CmdEndRound_Prefix));
                PatchPrefix(typeof(GameManager), "SetStartTime", nameof(GameManager_SetStartTime_Prefix));
                PatchPrefix(typeof(PlayerManager), "RpcLogic___SetPlayerMove_1140765316", nameof(PlayerManager_RpcLogic_SetPlayerMove_Prefix));
                PatchPrefix(typeof(KillCam), "Update", nameof(KillCam_Update_Prefix));
                PatchPrefix(typeof(ItemBehaviour), "Start", nameof(ItemBehaviour_Start_Prefix));

                PatchPostfixReflected(typeof(PlayerPickup), "RpcLogic___SetObjectInHandObserver_46969756",
                    nameof(PlayerPickup_SetObjectInHandObserver_Postfix));

                PatchPrefix(typeof(ItemBehaviour), "StickOnGround", nameof(ItemBehaviour_StickOnGround_Prefix));
                PatchPrefix(typeof(Spawner), "Update", nameof(Spawner_Update_Prefix));
                PatchPrefix(typeof(PlayerPickup), "RightHandFix", nameof(PlayerPickup_RightHandFix_Prefix));
                PatchPrefix(typeof(PlayerPickup), "RightHandDrop", nameof(PlayerPickup_RightHandDrop_Prefix));
                PatchPrefix(typeof(PlayerPickup), "SwitchWeapons", nameof(PlayerPickup_SwitchWeapons_Prefix));
                PatchPrefix(typeof(PlayerPickup), "LeftHandPickup", nameof(PlayerPickup_LeftHandPickup_Prefix));

                if (_miRpcLogic_DropObjectServer != null)
                {
                    _harmony.Patch(_miRpcLogic_DropObjectServer, prefix: new HarmonyMethod(
                        typeof(GunGamePatches).GetMethod(nameof(PlayerPickup_RpcLogic_DropObjectServer_Prefix), BindingFlags.Public | BindingFlags.Static)));
                }

                PatchPrefix(typeof(PauseManager), "StartRoundDelay", nameof(PauseManager_StartRoundDelay_Prefix));
                PatchPrefix(typeof(ProximityMine), "KillShockWave", nameof(ProximityMine_KillShockWave_Prefix));
                PatchPrefix(typeof(Claymore), "KillShockWave", nameof(Claymore_KillShockWave_Prefix));
                PatchPrefix(typeof(MatchLogs), "RpcLogic___RpcSendChatLine_3615296227", nameof(MatchLogs_RpcSendChatLine_Prefix));

                PatchPostfix(typeof(PlayerManager), "SpawnPlayer", nameof(PlayerManager_SpawnPlayer_Postfix), new Type[] { typeof(int), typeof(int) });
                PatchPostfix(typeof(PlayerManager), "RpcLogic___CmdRespawn_2166136261", nameof(PlayerManager_RpcLogic_CmdRespawn_Postfix));
                PatchPostfix(typeof(PlayerHealth), "RpcLogic___ExplodeForAll_576886416", nameof(PlayerHealth_ExplodeForAll_Postfix));
                PatchPostfixReflected(typeof(PlayerValues), "sync___set_value_playerClient", nameof(PlayerValues_SetPlayerClient_Postfix));

                PatchPrefix(typeof(DamageZone), "OnEnable", nameof(DamageZone_OnEnable_Prefix));
                PatchPrefix(typeof(FirstPersonController), "OnTriggerStay", nameof(FirstPersonController_OnTriggerStay_Prefix));
            }
            catch { }
        }

        private static void CacheReflectionMethods()
        {
            _miCmdRespawn = typeof(PlayerManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => m.Name.Contains("RpcLogic___CmdRespawn"));

            _miRoundWon = typeof(GameManager).GetMethod("RoundWon", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _fiSpawnedObject = typeof(PlayerManager).GetField("SpawnedObject", BindingFlags.Instance | BindingFlags.NonPublic);
            _miUnsubscribeFromInput = typeof(PlayerManager).GetMethod("UnsubscribeFromInput", BindingFlags.Instance | BindingFlags.NonPublic);
            _miReturnSpawnPoint = typeof(PlayerManager).GetMethod("ReturnSpawnPoint", BindingFlags.Instance | BindingFlags.NonPublic);
            _miSpawnPlayer4Args = typeof(PlayerManager).GetMethod("SpawnPlayer", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new Type[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) }, null);

            _miRpcLogic_DropObjectServer = typeof(PlayerPickup).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => m.Name.Contains("RpcLogic___DropObjectServer"));

            _miMatchLogsSendToAll = typeof(MatchLogs).GetMethod("RpcSendChatLineToAllObservers",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        private static void PatchPrefix(Type type, string methodName, string patchName, Type[] paramTypes = null)
        {
            var target = paramTypes == null
                ? type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                : type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, paramTypes, null);
            if (target == null) return;
            _harmony.Patch(target, prefix: new HarmonyMethod(typeof(GunGamePatches).GetMethod(patchName, BindingFlags.Public | BindingFlags.Static)));
        }

        private static void PatchPostfix(Type type, string methodName, string patchName, Type[] paramTypes = null)
        {
            var target = paramTypes == null
                ? type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                : type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, paramTypes, null);
            if (target == null) return;
            _harmony.Patch(target, postfix: new HarmonyMethod(typeof(GunGamePatches).GetMethod(patchName, BindingFlags.Public | BindingFlags.Static)));
        }

        private static void PatchPostfixReflected(Type type, string methodName, string patchName)
        {
            var target = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (target == null) return;
            _harmony.Patch(target, postfix: new HarmonyMethod(typeof(GunGamePatches).GetMethod(patchName, BindingFlags.Public | BindingFlags.Static)));
        }

        public static void ResetState()
        {
            _killsPerPlayer.Clear();
            _waitingForSetStartTime.Clear();
            _pendingPlaceableReplace.Clear();
            _playerRagdolls.Clear();
            _winSequenceInProgress = false;
            _respawnInProgress.Clear();
        }

        public static int GetCurrentWeaponIndex(int playerId)
        {
            int kills = _killsPerPlayer.TryGetValue(playerId, out int k) ? k : 0;
            int weaponCount = GunGamePlugin.GetOrderedWeaponCount();
            return (weaponCount > 0) ? (kills % weaponCount) : 0;
        }

        public static bool GameManager_WaitForDraw_Prefix(ref IEnumerator __result)
        {
            if (!GunGamePlugin.MatchOver) { __result = EmptyCoroutine(); return false; }
            return true;
        }

        public static bool KillCam_Update_Prefix() => !GunGamePlugin.Enabled.Value;

        private static IEnumerator EmptyCoroutine() { yield break; }

        public static bool PlayerManager_RpcLogic_CmdRespawn_Prefix(PlayerManager __instance)
        {
            if (!GunGamePlugin.Enabled.Value) return true;
            try
            {
                if (_miSpawnPlayer4Args == null)
                    return true;

                Transform spawnTrans = _miReturnSpawnPoint?.Invoke(__instance, null) as Transform;
                if (spawnTrans == null)
                {
                    var spawns = UnityEngine.Object.FindObjectsOfType<SpawnPoint>();
                    if (spawns.Length > 0) spawnTrans = spawns[UnityEngine.Random.Range(0, spawns.Length)].transform;
                }
                if (spawnTrans == null)
                    return true;

                GameObject spawnedObj = _fiSpawnedObject?.GetValue(__instance) as GameObject;
                if (spawnedObj != null)
                {
                    try { _miUnsubscribeFromInput?.Invoke(__instance, null); } catch { }
                    if (__instance.IsServer)
                    {
                        var netObj = spawnedObj.GetComponent<NetworkObject>();
                        if (netObj != null && netObj.IsSpawned)
                        {
                            if (!spawnedObj.activeSelf) spawnedObj.SetActive(true);
                            __instance.NetworkManager.ServerManager.Despawn(spawnedObj);
                        }
                        else UnityEngine.Object.Destroy(spawnedObj);
                    }
                }

                int suit = CosmeticsManager.Instance != null ? CosmeticsManager.Instance.currentsuitIndex : 0;
                int cig  = CosmeticsManager.Instance != null ? CosmeticsManager.Instance.currentcigIndex  : 0;
                try
                {
                    _miSpawnPlayer4Args.Invoke(__instance, new object[] {
                        suit, cig, spawnTrans.position, Quaternion.Euler(0f, spawnTrans.eulerAngles.y, 0f)
                    });
                }
                catch
                {
                    try { if (spawnedObj != null) spawnedObj.SetActive(true); } catch { }
                }
            }
            catch { }
            return false;
        }

        public static bool GameManager_RpcLogic_PlayerDied_Prefix(int playerId)
        {
            if (!GunGamePlugin.Enabled.Value) return true;
            if (GunGamePlugin.MatchOver) return false;

            try
            {
                if (InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.IsServer)
                {
                    var deadPickup = GunGamePlugin.FindPickupForPlayerId(playerId);
                    if (deadPickup != null) GunGameWeaponManager.DespawnHeldWeapon(deadPickup);

                    ClearPlacedExplosivesForPlayer(playerId);

                    int deadPlayerKills = _killsPerPlayer.TryGetValue(playerId, out int dk) ? dk : 0;
                    int weaponCount2 = GunGamePlugin.GetOrderedWeaponCount();
                    int respawnWeaponIndex = (weaponCount2 > 0) ? (deadPlayerKills % weaponCount2) : 0;
                    GunGameWeaponManager.QueueWeaponUpgrade(playerId, respawnWeaponIndex);
                }

                if (GameManager.Instance?.alivePlayers != null && !GameManager.Instance.alivePlayers.Contains(playerId))
                    GameManager.Instance.alivePlayers.Add(playerId);

                if (InstanceFinder.NetworkManager == null || !InstanceFinder.NetworkManager.IsServer)
                    return true;

                int killerId = FindKillerId(playerId);
                if (killerId >= 0 && killerId != playerId)
                {
                    if (!_killsPerPlayer.ContainsKey(killerId)) _killsPerPlayer[killerId] = 0;
                    _killsPerPlayer[killerId]++;
                    int kills = _killsPerPlayer[killerId];

                    int weaponCount = GunGamePlugin.GetOrderedWeaponCount();
                    int nextIndex = (weaponCount > 0) ? (kills % weaponCount) : 0;
                    int usedIdx = (weaponCount > 0) ? ((kills - 1 + weaponCount) % weaponCount) : 0;

                    string usedName = GunGamePlugin.GetOrderedWeaponPrefab(usedIdx)?.name ?? "Unknown";
                    string killerName = GetPlayerName(killerId);
                    string deadName = GetPlayerName(playerId);

                    string msg = (kills >= GunGamePlugin.KillsToWin.Value)
                        ? $"<b><color=yellow>[Gun Game]</color></b> <b>{killerName}</b> <color=red>killed</color> <b>{deadName}</b> with <b><color=white>{usedName}</color></b> — <b><color=yellow>WINS THE ROUND!</color></b>"
                        : $"<b><color=yellow>[Gun Game]</color></b> <b>{killerName}</b> <color=red>killed</color> <b>{deadName}</b> with <b><color=white>{usedName}</color></b> <color=#00FFFF>[{kills}/{GunGamePlugin.KillsToWin.Value}]</color>";
                    BroadcastChat(msg);

                    GunGameWeaponManager.GiveWeaponToPlayer(killerId, nextIndex);
                    GameManager.Instance.StartCoroutine(DelayedKillWeaponGive(killerId, nextIndex));

                    if (usedName == "ProximityMine" || usedName == "APMine" || usedName == "Claymore")
                        ClearPlacedExplosivesForPlayer(killerId);

                    if (kills >= GunGamePlugin.KillsToWin.Value && !_winSequenceInProgress)
                    {
                        _winSequenceInProgress = true;
                        GunGamePlugin.MatchOver = true;
                        GameManager.Instance.StartCoroutine(WinSequenceCoroutine(killerId));
                        return false;
                    }
                }
                else
                {
                    string suicideName = GetPlayerName(playerId);
                    BroadcastChat($"<b><color=yellow>[Gun Game]</color></b> <b>{suicideName}</b> <color=#888888>killed themselves</color>");
                }

                if (GameManager.Instance != null)
                    GameManager.Instance.StartCoroutine(SinglePlayerRespawnCoroutine(playerId));
            }
            catch
            {
                try { if (GameManager.Instance != null) GameManager.Instance.StartCoroutine(SinglePlayerRespawnCoroutine(playerId)); } catch { }
            }

            return false;
        }

        public static void PlayerManager_SpawnPlayer_Postfix(PlayerManager __instance)
        {
            if (!GunGamePlugin.Enabled.Value || __instance == null) return;

            var client = __instance.GetComponent<ClientInstance>();
            if (client == null) return;
            int playerId = client.PlayerId;
            if (playerId < 0) return;

            if (InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.IsServer)
            {
                if (_killsPerPlayer.ContainsKey(playerId))
                    _killsPerPlayer.Remove(playerId);
                GunGameWeaponManager.ClearPendingForPlayer(playerId);

                if (GameManager.Instance?.alivePlayers != null && !GameManager.Instance.alivePlayers.Contains(playerId))
                    GameManager.Instance.alivePlayers.Add(playerId);

                _waitingForSetStartTime.Add(playerId);

                if (GunGamePlugin.GetOrderedWeaponCount() > 0)
                {
                    GunGameWeaponManager.GiveWeaponToPlayer(playerId, 0);
                    GameManager.Instance?.StartCoroutine(DelayedInitialSpawnFallback(playerId));
                }
            }

            if (__instance.IsOwner)
            {
                if (PauseManager.Instance != null) PauseManager.Instance.startRound = false;
                GameManager.Instance?.StartCoroutine(KeepPlayerMovable(__instance, 4f));
            }
        }

        public static void PlayerManager_RpcLogic_CmdRespawn_Postfix(PlayerManager __instance)
        {
            if (!GunGamePlugin.Enabled.Value || __instance == null) return;

            var client = __instance.GetComponent<ClientInstance>();
            if (client == null) return;
            int playerId = client.PlayerId;
            if (playerId < 0) return;

            _waitingForSetStartTime.Add(playerId);

            if (InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.IsServer)
            {
                if (GunGameWeaponManager.HasPendingWeapon(playerId))
                    GunGameWeaponManager.ApplyPendingWeaponOnSpawn(playerId);
                else
                    GunGameWeaponManager.GiveWeaponToPlayer(playerId, GetCurrentWeaponIndex(playerId));
            }

            if (__instance.IsOwner)
            {
                if (PauseManager.Instance != null) PauseManager.Instance.startRound = false;
                var runner = GameManager.Instance;
                if (runner != null) runner.StartCoroutine(KeepPlayerMovable(__instance, 4f));
            }
        }

        public static void GameManager_SetStartTime_Prefix(ref float serverTimeTillStart)
        {
            if (!GunGamePlugin.Enabled.Value || GunGamePlugin.MatchOver) return;
            serverTimeTillStart = 0f;
        }

        public static bool PlayerManager_RpcLogic_SetPlayerMove_Prefix(PlayerManager __instance, bool state)
        {
            if (!GunGamePlugin.Enabled.Value || GunGamePlugin.MatchOver) return true;
            var client = __instance.GetComponent<ClientInstance>();
            if (client == null) return true;
            int playerId = client.PlayerId;
            if (playerId < 0) return true;

            if (!state && _waitingForSetStartTime.Contains(playerId))
            {
                _waitingForSetStartTime.Remove(playerId);
                try
                {
                    if (__instance.player != null) { __instance.player.sync___set_value_canMove(true, true); __instance.player.startOfRound = false; }
                    if (PauseManager.Instance != null) { PauseManager.Instance.startRound = false; PauseManager.Instance.pause = false; PauseManager.Instance.otherPauseBools = false; }
                }
                catch { }
                return false;
            }
            return true;
        }

        public static bool ItemBehaviour_Start_Prefix(ItemBehaviour __instance)
        {
            if (__instance == null) return true;
            __instance.dispenserStart = true;
            var rb = __instance.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            return true;
        }

        public static void PlayerPickup_SetObjectInHandObserver_Postfix(PlayerPickup __instance, GameObject obj)
        {
            if (obj == null) return;
            var ib = obj.GetComponent<ItemBehaviour>();
            if (ib != null) ib.dispenserStart = false;
        }

        public static bool ItemBehaviour_StickOnGround_Prefix(ItemBehaviour __instance)
        {
            if (__instance.cam == null) return false;
            return true;
        }

        public static bool Spawner_Update_Prefix() => !GunGamePlugin.Enabled.Value;

        public static bool PlayerPickup_RightHandFix_Prefix(PlayerPickup __instance)
        {
            if (!GunGamePlugin.Enabled.Value) return true;

            if (__instance.sync___get_value_hasObjectInHand() && __instance.sync___get_value_objInHand() == null)
            {
                __instance.sync___set_value_hasObjectInHand(false, true);
                return false;
            }

            var obj = __instance.sync___get_value_objInHand();
            if (obj == null) return true;

            if (obj.layer == 7 || obj.layer == 9)
            {
                var mi = GunGameWeaponManager.SetObjectInHandObserverLogic;
                if (mi != null)
                {
                    try { mi.Invoke(__instance, new object[] { obj, obj.transform.position, obj.transform.rotation, __instance.gameObject, true }); }
                    catch { }
                }
                return false;
            }

            return true;
        }

        public static bool PlayerPickup_RightHandDrop_Prefix(PlayerPickup __instance)
        {
            if (!GunGamePlugin.Enabled.Value) return true;

            var obj = __instance.sync___get_value_objInHand();

            if (__instance.sync___get_value_hasObjectInHand() &&
                (obj == null || obj.GetComponent<ItemBehaviour>() == null))
            {
                __instance.sync___set_value_hasObjectInHand(false, true);
                __instance.sync___set_value_objInHand(null, true);
                return false;
            }

            var ph = __instance.GetComponentInParent<PlayerHealth>();
            if (ph != null && ph.sync___get_value_isKilled()) return false;

            return true;
        }

        public static bool PlayerPickup_SwitchWeapons_Prefix() => !GunGamePlugin.Enabled.Value;

        public static bool PlayerPickup_LeftHandPickup_Prefix() => !GunGamePlugin.Enabled.Value;

        public static bool PlayerPickup_RpcLogic_DropObjectServer_Prefix(PlayerPickup __instance, GameObject obj, bool rightHand)
        {
            if (!GunGamePlugin.Enabled.Value) return true;
            if (!InstanceFinder.NetworkManager.IsServer) return true;

            string droppedName = obj != null ? obj.name.Replace("(Clone)", "").Trim() : "";
            bool isPlaceable = droppedName == "Claymore" || droppedName == "ProximityMine" ||
                               droppedName == "APMine" || droppedName == "HandGrenade" || droppedName == "GlandGrenade";

            if (isPlaceable && obj != null)
                obj.transform.SetParent(null);

            try
            {
                var client = __instance.playerValues?.sync___get_value_playerClient();
                int playerId = client?.PlayerId ?? -1;
                int weaponIndex = playerId >= 0 ? GetCurrentWeaponIndex(playerId) : 0;
                var runner = GameManager.Instance as MonoBehaviour ?? GunGamePlugin.Instance;
                runner?.StartCoroutine(DeferredDropCleanup(__instance, obj, isPlaceable, playerId, weaponIndex));
            }
            catch { }

            return false;
        }

        private static IEnumerator DeferredDropCleanup(PlayerPickup pickup, GameObject obj, bool isPlaceable, int playerId, int weaponIndex)
        {
            yield return null;

            try
            {
                pickup.sync___set_value_hasObjectInHand(false, true);
                pickup.sync___set_value_hasObjectInLeftHand(false, true);
                pickup.sync___set_value_objInHand(null, true);
                pickup.sync___set_value_objInLeftHand(null, true);
            }
            catch { }

            if (!isPlaceable && obj != null)
            {
                var netObj = obj.GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                    try { InstanceFinder.ServerManager.Despawn(netObj); } catch { }
                else
                    try { UnityEngine.Object.Destroy(obj); } catch { }
            }

            yield return new WaitForSeconds(0.1f);

            if (playerId < 0 || GunGamePlugin.MatchOver) yield break;
            if (isPlaceable)
            {
                if (!_pendingPlaceableReplace.Contains(playerId))
                {
                    _pendingPlaceableReplace.Add(playerId);
                    var runner = GameManager.Instance as MonoBehaviour ?? GunGamePlugin.Instance;
                    runner?.StartCoroutine(EnsurePlaceableReplaced(playerId));
                }
            }
            else
                GunGameWeaponManager.GiveWeaponToPlayer(playerId, weaponIndex);
        }

        private static IEnumerator DelayedKillWeaponGive(int killerId, int weaponIndex)
        {
            yield return new WaitForSeconds(3f);
            if (GunGamePlugin.MatchOver) yield break;
            try
            {
                var pickup = GunGamePlugin.FindPickupForPlayerId(killerId);
                if (pickup != null && !pickup.sync___get_value_hasObjectInHand())
                    GunGameWeaponManager.GiveWeaponToPlayer(killerId, weaponIndex);
            }
            catch { }
        }

        private static IEnumerator DelayedInitialSpawnFallback(int playerId)
        {
            yield return new WaitForSeconds(7f);
            if (GunGamePlugin.MatchOver) yield break;
            try
            {
                if (GunGameWeaponManager.IsGivingWeapon(playerId)) yield break;
                var pickup = GunGamePlugin.FindPickupForPlayerId(playerId);
                if (pickup != null && !pickup.sync___get_value_hasObjectInHand())
                    GunGameWeaponManager.GiveWeaponToPlayer(playerId, GetCurrentWeaponIndex(playerId));
            }
            catch { }
        }

        private static IEnumerator DelayedRespawnWeaponFallback(int playerId)
        {
            yield return new WaitForSeconds(3f);
            if (GunGamePlugin.MatchOver) yield break;
            try
            {
                if (GunGameWeaponManager.IsGivingWeapon(playerId)) yield break;
                var pickup = GunGamePlugin.FindPickupForPlayerId(playerId);
                if (pickup != null && !pickup.sync___get_value_hasObjectInHand())
                    GunGameWeaponManager.GiveWeaponToPlayer(playerId, GetCurrentWeaponIndex(playerId));
            }
            catch { }
        }

        private static IEnumerator EnsurePlaceableReplaced(int playerId)
        {
            bool gave = false;
            try
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (GunGamePlugin.MatchOver) yield break;
                    if (GunGameWeaponManager.IsGivingWeapon(playerId)) continue;
                    var pickup = GunGamePlugin.FindPickupForPlayerId(playerId);
                    if (pickup == null) continue;
                    if (!pickup.sync___get_value_hasObjectInHand())
                    {
                        int currentIndex = GetCurrentWeaponIndex(playerId);
                        GunGameWeaponManager.GiveWeaponToPlayer(playerId, currentIndex);
                        gave = true;
                    }
                    yield break;
                }

                if (!gave && !GunGamePlugin.MatchOver)
                {
                    var pickup = GunGamePlugin.FindPickupForPlayerId(playerId);
                    if (pickup != null && !pickup.sync___get_value_hasObjectInHand())
                        GunGameWeaponManager.GiveWeaponToPlayer(playerId, GetCurrentWeaponIndex(playerId));
                }
            }
            finally
            {
                _pendingPlaceableReplace.Remove(playerId);
            }
        }

        public static bool RoundManager_CmdEndRound_Prefix(int winningTeamId)
        {
            if (!GunGamePlugin.MatchOver)
                return false;
            return true;
        }

        private static IEnumerator WinSequenceCoroutine(int winnerPlayerId)
        {
            yield return new WaitForSeconds(1.5f);
            bool endRoundCalled = false;
            int winningTeamId = -1;
            try
            {
                winningTeamId = GetPlayerTeam(winnerPlayerId);
                BroadcastChat($"<b><color=yellow>[Gun Game]</color></b> <b><color=white>{GetPlayerName(winnerPlayerId)}</color></b> wins the round!");
                if (ScoreManager.Instance != null) ScoreManager.Instance.AddRoundScore(winningTeamId);
                if (_miRoundWon != null) _miRoundWon.Invoke(GameManager.Instance, new object[] { winningTeamId });
                if (RoundManager.Instance != null)
                {
                    RoundManager.Instance.CmdEndRound(winningTeamId);
                    endRoundCalled = true;
                }
            }
            catch { }

            yield return new WaitForSeconds(4f);
            if (endRoundCalled)
            {
                try { if (SceneMotor.Instance != null) SceneMotor.Instance.ChangeNetworkScene(); }
                catch { }
            }
            GunGamePlugin.MatchOver = false;
            ResetState();
        }

        private static Type _particleSystemType;

        private static Type GetParticleSystemType()
        {
            if (_particleSystemType != null) return _particleSystemType;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("UnityEngine.ParticleSystem");
                if (t != null) { _particleSystemType = t; break; }
            }
            return _particleSystemType;
        }

        private static void CleanupBloodAndHats()
        {
            try
            {
                var psType = GetParticleSystemType();
                if (psType != null)
                {
                    var particles = UnityEngine.Object.FindObjectsOfType(psType, true);
                    foreach (var p in particles)
                    {
                        try
                        {
                            var comp = p as Component;
                            if (comp != null && comp.transform.parent == null)
                                UnityEngine.Object.Destroy(comp.gameObject);
                        }
                        catch { }
                    }
                }

                foreach (var hp in UnityEngine.Object.FindObjectsOfType<HatPosition>(true))
                {
                    try
                    {
                        if (hp.transform.parent == null)
                            UnityEngine.Object.Destroy(hp.gameObject);
                    }
                    catch { }
                }
            }
            catch { }
        }

        internal static void CleanupWorldEffects()
        {
            try
            {
                foreach (var kvp in _playerRagdolls)
                    try { if (kvp.Value != null) UnityEngine.Object.Destroy(kvp.Value); } catch { }
                _playerRagdolls.Clear();

                foreach (var rd in UnityEngine.Object.FindObjectsOfType<RagdollDress>(true))
                    try { UnityEngine.Object.Destroy(rd.gameObject); } catch { }
            }
            catch { }

            CleanupBloodAndHats();
        }

        private static IEnumerator SinglePlayerRespawnCoroutine(int deadPlayerId)
        {
            yield return new WaitForSeconds(GunGamePlugin.RespawnDelay.Value);

            if (GunGamePlugin.MatchOver) yield break;
            if (GameManager.Instance == null || InstanceFinder.NetworkManager == null || !InstanceFinder.NetworkManager.IsServer)
                yield break;

            float gateTimeout = Time.time + 5f;
            while (_respawnInProgress.Contains(deadPlayerId) && Time.time < gateTimeout)
                yield return null;

            _respawnInProgress.Add(deadPlayerId);
            try
            {
                if (_playerRagdolls.TryGetValue(deadPlayerId, out var ragdoll) && ragdoll != null)
                {
                    try { UnityEngine.Object.Destroy(ragdoll); } catch { }
                    _playerRagdolls.Remove(deadPlayerId);
                }

                CleanupBloodAndHats();
            }
            catch { }

            bool spawned = false;
            try
            {
                if (_miCmdRespawn != null)
                {
                    var pm = FindPlayerManagerForId(deadPlayerId);
                    if (pm != null)
                    {
                        _waitingForSetStartTime.Add(deadPlayerId);
                        var args = BuildDefaultArgs(_miCmdRespawn);
                        _miCmdRespawn.Invoke(pm, args);
                        spawned = true;
                        if (GameManager.Instance != null) GameManager.Instance.SetStartTime(0f);
                        GameManager.Instance.StartCoroutine(KeepPlayerMovable(pm, 4f));
                    }
                }
            }
            catch { }

            yield return null;
            _respawnInProgress.Remove(deadPlayerId);

            if (spawned)
                GameManager.Instance?.StartCoroutine(DelayedRespawnWeaponFallback(deadPlayerId));
        }

        private static IEnumerator KeepPlayerMovable(PlayerManager pm, float duration)
        {
            float end = Time.time + duration;
            while (Time.time < end)
            {
                try
                {
                    if (pm != null)
                    {
                        pm.SetPlayerMove(true);
                        if (pm.player != null) { pm.player.sync___set_value_canMove(true, true); pm.player.startOfRound = false; }
                        if (PauseManager.Instance != null) PauseManager.Instance.startRound = false;
                    }
                }
                catch { }
                yield return null;
            }
        }

        private static object[] BuildDefaultArgs(MethodInfo mi)
        {
            if (mi == null) return null;
            var prms = mi.GetParameters();
            if (prms.Length == 0) return null;
            var args = new object[prms.Length];
            for (int i = 0; i < prms.Length; i++)
                args[i] = prms[i].ParameterType.IsValueType ? Activator.CreateInstance(prms[i].ParameterType) : null;
            return args;
        }

        private static int FindKillerId(int deadPlayerId)
        {
            var ph = FindPlayerHealth(deadPlayerId);
            if (ph == null) return -1;

            var killerT = ph.killer;
            if (killerT == null) return -1;

            var ci = killerT.GetComponentInParent<ClientInstance>(true);
            if (ci != null) return ci.PlayerId;

            var pv = killerT.GetComponentInParent<PlayerValues>(true) ?? killerT.GetComponentInChildren<PlayerValues>(true);
            if (pv?.sync___get_value_playerClient()?.PlayerId is int pvId && pvId >= 0) return pvId;

            GameObject mineRoot = null;
            var proxMine = killerT.GetComponentInParent<ProximityMine>(true) ?? killerT.GetComponentInChildren<ProximityMine>(true);
            if (proxMine != null) mineRoot = proxMine.sync___get_value__rootObject();
            else
            {
                var clay = killerT.GetComponentInParent<Claymore>(true) ?? killerT.GetComponentInChildren<Claymore>(true);
                if (clay != null) mineRoot = clay.sync___get_value__rootObject();
            }

            if (mineRoot != null)
            {
                int mineOwnerId = GetOwnerIdFromRootObject(mineRoot);
                if (mineOwnerId >= 0) return mineOwnerId;
            }

            return -1;
        }

        public static bool DamageZone_OnEnable_Prefix(DamageZone __instance)
        {
            if (!GunGamePlugin.Enabled.Value) return true;
            try
            {
                foreach (var col in __instance.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
                if (__instance.toDestroy != null)
                    UnityEngine.Object.Destroy(__instance.toDestroy);
                UnityEngine.Object.Destroy(__instance.gameObject);
            }
            catch { }
            return false;
        }

        public static bool FirstPersonController_OnTriggerStay_Prefix(Collider col)
        {
            if (GunGamePlugin.Enabled.Value && col.CompareTag("DamageZone"))
                return false;
            return true;
        }

        public static void PlayerHealth_ExplodeForAll_Postfix(PlayerHealth __instance)
        {
            if (!GunGamePlugin.Enabled.Value) return;
            try
            {
                int playerId = __instance.playerValues?.sync___get_value_playerClient()?.PlayerId ?? -1;
                if (playerId < 0) return;

                RagdollDress closest = null;
                float minDist = float.MaxValue;
                foreach (var rd in UnityEngine.Object.FindObjectsOfType<RagdollDress>(true))
                {
                    float d = Vector3.Distance(rd.transform.position, __instance.transform.position);
                    if (d < minDist) { minDist = d; closest = rd; }
                }

                if (closest != null)
                    _playerRagdolls[playerId] = closest.gameObject;
            }
            catch { }
        }

        public static void PlayerValues_SetPlayerClient_Postfix(ClientInstance __0)
        {
            if (!GunGamePlugin.Enabled.Value || __0 == null) return;
            try
            {
                int playerId = __0.PlayerId;
                if (playerId < 0) return;

                if (_playerRagdolls.TryGetValue(playerId, out var ragdoll) && ragdoll != null)
                {
                    UnityEngine.Object.Destroy(ragdoll);
                    _playerRagdolls.Remove(playerId);
                }

                CleanupBloodAndHats();
            }
            catch { }
        }

        private static PlayerHealth FindPlayerHealth(int deadPlayerId)
        {
            foreach (var ph in UnityEngine.Object.FindObjectsOfType<PlayerHealth>(true))
            {
                if (ph?.playerValues?.sync___get_value_playerClient()?.PlayerId == deadPlayerId) return ph;
            }
            return null;
        }

        private static PlayerManager FindPlayerManagerForId(int playerId)
        {
            if (ClientInstance.playerInstances.TryGetValue(playerId, out var ci) && ci != null)
                return ci.GetComponent<PlayerManager>();
            return null;
        }

        public static bool PauseManager_StartRoundDelay_Prefix(PauseManager __instance)
        {
            if (!GunGamePlugin.Enabled.Value) return true;
            try
            {
                __instance.InvokeRoundStarted();
                __instance.onStartRoundScreen = true;
                var runner = GunGamePlugin.Instance ?? (MonoBehaviour)GameManager.Instance;
                if (runner != null)
                    runner.StartCoroutine(ClearOnStartRoundScreen(__instance));
            }
            catch { }
            return false;
        }

        private static IEnumerator ClearOnStartRoundScreen(PauseManager pm)
        {
            yield return new WaitForSeconds(0.8f);
            if (pm != null) pm.onStartRoundScreen = false;
        }

        public static bool ProximityMine_KillShockWave_Prefix(ProximityMine __instance)
        {
            var root = __instance.sync___get_value__rootObject();
            if (root == null) return false;
            if (root.GetComponent<FirstPersonController>() == null) return false;
            return true;
        }

        public static bool Claymore_KillShockWave_Prefix(Claymore __instance)
        {
            var root = __instance.sync___get_value__rootObject();
            if (root == null) return false;
            if (root.GetComponent<FirstPersonController>() == null) return false;
            return true;
        }

        public static bool MatchLogs_RpcSendChatLine_Prefix() => !GunGamePlugin.Enabled.Value;

        private static int GetOwnerIdFromRootObject(GameObject root)
        {
            if (root == null) return -1;

            var ci = root.GetComponentInParent<ClientInstance>(true) ?? root.GetComponentInChildren<ClientInstance>(true);
            if (ci != null) return ci.PlayerId;

            var pv = root.GetComponentInParent<PlayerValues>(true) ?? root.GetComponentInChildren<PlayerValues>(true);
            int pvId = pv?.sync___get_value_playerClient()?.PlayerId ?? -1;
            if (pvId >= 0) return pvId;

            foreach (var kvp in ClientInstance.playerInstances)
            {
                try
                {
                    var pm = kvp.Value?.GetComponent<PlayerManager>();
                    if (pm == null) continue;
                    if (pm.player != null && (pm.player.gameObject == root || root.transform.IsChildOf(pm.player.transform)))
                        return kvp.Key;
                    var spawnedObj = _fiSpawnedObject?.GetValue(pm) as GameObject;
                    if (spawnedObj != null && (spawnedObj == root || root.transform.IsChildOf(spawnedObj.transform)))
                        return kvp.Key;
                }
                catch { }
            }

            return -1;
        }

        private static void ClearPlacedExplosivesForPlayer(int playerId)
        {
            try
            {
                foreach (var mine in UnityEngine.Object.FindObjectsOfType<ProximityMine>(true))
                {
                    try
                    {
                        var root = mine.sync___get_value__rootObject();
                        if (GetOwnerIdFromRootObject(root) != playerId) continue;
                        var netObj = mine.GetComponent<NetworkObject>();
                        if (netObj != null && netObj.IsSpawned)
                            InstanceFinder.ServerManager.Despawn(netObj);
                        else
                            UnityEngine.Object.Destroy(mine.gameObject);
                    }
                    catch { }
                }

                foreach (var clay in UnityEngine.Object.FindObjectsOfType<Claymore>(true))
                {
                    try
                    {
                        var root = clay.sync___get_value__rootObject();
                        if (GetOwnerIdFromRootObject(root) != playerId) continue;
                        var netObj = clay.GetComponent<NetworkObject>();
                        if (netObj != null && netObj.IsSpawned)
                            InstanceFinder.ServerManager.Despawn(netObj);
                        else
                            UnityEngine.Object.Destroy(clay.gameObject);
                    }
                    catch { }
                }
            }
            catch { }
        }

        internal static void BroadcastChat(string text)
        {
            if (MatchLogs.Instance == null || InstanceFinder.NetworkManager == null || !InstanceFinder.NetworkManager.IsServer) return;
            try { _miMatchLogsSendToAll?.Invoke(MatchLogs.Instance, new object[] { text }); }
            catch { }
        }

        private static string GetPlayerName(int playerId)
        {
            try { if (ClientInstance.playerInstances.TryGetValue(playerId, out var ci) && ci != null) return ci.PlayerNameTag; } catch { }
            return $"Player {playerId}";
        }

        private static int GetPlayerTeam(int playerId)
        {
            if (ScoreManager.Instance != null) try { return ScoreManager.Instance.GetTeamId(playerId); } catch { }
            return playerId;
        }
    }
}
