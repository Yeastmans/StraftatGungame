using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet;
using FishNet.Object;
using UnityEngine;

namespace GunGameMod
{
    public static class GunGameWeaponManager
    {
        private static Dictionary<int, int> _pendingWeaponIndex = new Dictionary<int, int>();

        public static void QueueWeaponUpgrade(int playerId, int weaponIndex)
        {
            _pendingWeaponIndex[playerId] = weaponIndex;
        }

        public static int GetAndClearPendingWeapon(int playerId)
        {
            if (_pendingWeaponIndex.TryGetValue(playerId, out int index))
            {
                _pendingWeaponIndex.Remove(playerId);
                return index;
            }
            return -1;
        }

        public static bool HasPendingWeapon(int playerId) => _pendingWeaponIndex.ContainsKey(playerId);

        public static void ClearPendingForPlayer(int playerId)
        {
            _pendingWeaponIndex.Remove(playerId);
            _giveSequence.Remove(playerId);
        }

        public static void ClearAllPending()
        {
            _pendingWeaponIndex.Clear();
            _giveSequence.Clear();
            _givingInProgress.Clear();
            _initialSpawnDelayDone = false;
        }

        private static MethodInfo _miSetObjectInHandRpcLogic;
        private static MethodInfo _miSetObjectInHandObserver;
        private static MethodInfo _miSetObjectInHandObserverLogic;

        private static void CacheMethods()
        {
            if (_miSetObjectInHandRpcLogic != null) return;
            _miSetObjectInHandRpcLogic = typeof(PlayerPickup).GetMethod(
                "RpcLogic___SetObjectInHandServer_46969756",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _miSetObjectInHandObserver = typeof(PlayerPickup).GetMethod(
                "SetObjectInHandObserver",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _miSetObjectInHandObserverLogic = typeof(PlayerPickup).GetMethod(
                "RpcLogic___SetObjectInHandObserver_46969756",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        internal static MethodInfo SetObjectInHandObserverLogic
        {
            get { CacheMethods(); return _miSetObjectInHandObserverLogic; }
        }

        private static bool _initialSpawnDelayDone = false;
        private static Dictionary<int, int> _giveSequence = new Dictionary<int, int>();
        private static HashSet<int> _givingInProgress = new HashSet<int>();

        public static bool IsGivingWeapon(int playerId) => _givingInProgress.Contains(playerId);

        public static int GetGiveSequence(int playerId) =>
            _giveSequence.TryGetValue(playerId, out int s) ? s : 0;

        public static void GiveWeaponToPlayer(int playerId, int weaponIndex)
        {
            if (InstanceFinder.NetworkManager == null || !InstanceFinder.NetworkManager.IsServer) return;
            if (GunGamePlugin.MatchOver) return;

            if (!_giveSequence.ContainsKey(playerId)) _giveSequence[playerId] = 0;
            int seq = ++_giveSequence[playerId];

            var runner = GameManager.Instance as MonoBehaviour ?? GunGamePlugin.Instance;
            if (runner != null)
                runner.StartCoroutine(GiveWeaponCoroutine(playerId, weaponIndex, seq));
        }

        private static IEnumerator GiveWeaponCoroutine(int playerId, int weaponIndex, int seq)
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm == null || !nm.IsServer) yield break;

            _givingInProgress.Add(playerId);
            bool done = false;
            try
            {
                if (GunGamePlugin.GetOrderedWeaponPrefab(weaponIndex) == null)
                    done = true;
            }
            finally { if (done) _givingInProgress.Remove(playerId); }
            if (done) yield break;

            float initialDelay = _initialSpawnDelayDone ? 0f : 2f;
            _initialSpawnDelayDone = true;
            yield return new WaitForSeconds(initialDelay);

            if (GunGamePlugin.MatchOver) { _givingInProgress.Remove(playerId); yield break; }

            if (_giveSequence.TryGetValue(playerId, out int currentSeq) && currentSeq != seq)
            {
                _givingInProgress.Remove(playerId);
                yield break;
            }

            PlayerPickup pickup = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                pickup = GunGamePlugin.FindPickupForPlayerId(playerId);
                if (pickup != null) break;
                yield return new WaitForSeconds(0.5f);
            }
            if (pickup == null)
            {
                _givingInProgress.Remove(playerId);
                yield break;
            }

            var heldRight = pickup.sync___get_value_objInHand();
            var heldLeft  = pickup.sync___get_value_hasObjectInLeftHand()
                            ? pickup.sync___get_value_objInLeftHand() : null;
            pickup.sync___set_value_hasObjectInHand(false, true);
            pickup.sync___set_value_hasObjectInLeftHand(false, true);
            pickup.sync___set_value_objInHand(null, true);
            pickup.sync___set_value_objInLeftHand(null, true);

            yield return new WaitForSeconds(0.3f);

            if (_giveSequence.TryGetValue(playerId, out int seqCheck) && seqCheck != seq)
            {
                _givingInProgress.Remove(playerId);
                yield break;
            }

            foreach (var held in new[] { heldRight, heldLeft })
            {
                if (held == null) continue;
                var no = held.GetComponentInParent<NetworkObject>();
                if (no != null)
                    try { InstanceFinder.ServerManager.Despawn(no); } catch { }
                else
                    try { UnityEngine.Object.Destroy(held); } catch { }
            }
            try
            {
                pickup = GunGamePlugin.FindPickupForPlayerId(playerId);
                if (pickup != null)
                    foreach (var ibFallback in pickup.transform.root.GetComponentsInChildren<ItemBehaviour>(true))
                    {
                        if (ibFallback == null) continue;
                        var no = ibFallback.GetComponent<NetworkObject>();
                        if (no != null && no.IsSpawned)
                            try { InstanceFinder.ServerManager.Despawn(no); } catch { }
                        else
                            try { UnityEngine.Object.Destroy(ibFallback.gameObject); } catch { }
                    }
            }
            catch { }

            yield return new WaitForSeconds(0.15f);

            pickup = GunGamePlugin.FindPickupForPlayerId(playerId);
            if (pickup == null)
            {
                _givingInProgress.Remove(playerId);
                yield break;
            }

            GameObject playerGO = null;
            try
            {
                if (ClientInstance.playerInstances.TryGetValue(playerId, out var ci))
                {
                    var pm = ci?.GetComponent<PlayerManager>();
                    playerGO = pm?.player?.gameObject;
                }
            }
            catch { }
            if (playerGO == null) playerGO = pickup.transform.root.gameObject;

            GameObject prefab = GunGamePlugin.GetOrderedWeaponPrefab(weaponIndex);

            GameObject weapon = null;
            NetworkObject netObj = null;
            ItemBehaviour ib = null;
            try
            {
                weapon = UnityEngine.Object.Instantiate(prefab, playerGO.transform.position, playerGO.transform.rotation);
                ib = weapon.GetComponent<ItemBehaviour>();
                netObj = weapon.GetComponent<NetworkObject>();
                if (ib != null) ib.dispenserStart = true;
                var rb = weapon.GetComponent<Rigidbody>();
                if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
                nm.ServerManager.Spawn(weapon);
            }
            catch (Exception)
            {
                _givingInProgress.Remove(playerId);
                yield break;
            }

            yield return new WaitForSeconds(0.1f);
            if (weapon == null || pickup == null)
            {
                _givingInProgress.Remove(playerId);
                yield break;
            }

            CacheMethods();

            try { _miSetObjectInHandRpcLogic?.Invoke(pickup, new object[] { weapon, weapon.transform.position, weapon.transform.rotation, playerGO, true }); }
            catch { }

            if (weapon == null)
            {
                _givingInProgress.Remove(playerId);
                yield break;
            }
            if (ib != null) ib.dispenserStart = false;

            try
            {
                pickup.sync___set_value_hasObjectInHand(true, true);
                pickup.sync___set_value_objInHand(weapon, true);
            }
            catch { }

            try { _miSetObjectInHandObserver?.Invoke(pickup, new object[] { weapon, weapon.transform.position, weapon.transform.rotation, playerGO, true }); }
            catch { }

            _givingInProgress.Remove(playerId);
        }

        internal static void DespawnHeldWeapon(PlayerPickup pickup)
        {
            if (pickup == null) return;

            var rightHeld = pickup.sync___get_value_objInHand();
            var leftHeld = pickup.sync___get_value_hasObjectInLeftHand() ? pickup.sync___get_value_objInLeftHand() : null;

            pickup.sync___set_value_hasObjectInHand(false, true);
            pickup.sync___set_value_hasObjectInLeftHand(false, true);
            pickup.sync___set_value_objInHand(null, true);
            pickup.sync___set_value_objInLeftHand(null, true);

            foreach (var held in new[] { rightHeld, leftHeld })
            {
                if (held == null) continue;
                var netObj = held.GetComponentInParent<NetworkObject>();
                if (netObj != null)
                    try { InstanceFinder.ServerManager.Despawn(netObj); } catch { }
                else
                    try { UnityEngine.Object.Destroy(held); } catch { }
            }

            try
            {
                foreach (var ib in pickup.transform.root.GetComponentsInChildren<ItemBehaviour>(true))
                {
                    if (ib == null) continue;
                    var no = ib.GetComponent<NetworkObject>();
                    if (no != null && no.IsSpawned)
                        try { InstanceFinder.ServerManager.Despawn(no); } catch { }
                    else
                        try { UnityEngine.Object.Destroy(ib.gameObject); } catch { }
                }
            }
            catch { }
        }

        public static void ApplyPendingWeaponOnSpawn(int playerId)
        {
            if (InstanceFinder.NetworkManager == null || !InstanceFinder.NetworkManager.IsServer) return;
            int pendingIndex = GetAndClearPendingWeapon(playerId);
            if (pendingIndex >= 0)
                GiveWeaponToPlayer(playerId, pendingIndex);
        }
    }
}
