using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using FishNet;
using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace GunGameMod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class GunGamePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.modder.gungame";
        public const string PluginName = "Gun Game";
        public const string PluginVersion = "1.0.0";
        public const uint ModId = 774411;

        internal static ManualLogSource Log;
        private static GunGamePlugin _instance;
        public static GunGamePlugin Instance => _instance;

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<int> KillsToWin;
        public static ConfigEntry<float> RespawnDelay;
        public static ConfigEntry<string> WeaponOrder;
        public static ConfigEntry<string>[] WeaponSlots;

        public static bool MatchOver = false;
        private bool _fishNetHooked = false;

        private static readonly string[] DefaultWeaponNames = new[]
        {
            "Gun", "Glock", "Revolver", "Silenzzio", "Webley", "Keso", "Bender", "BeamLoad",
            "Mac10", "SMG", "Bukanee", "Dispenser", "Yangtse", "Hill_H15", "Crisis", "DF_Torrent", "GlaiveGun",
            "Tromblonj", "SawedOff", "Shotgun", "Havoc", "AAA12",
            "Kusma", "AR15", "AK-K", "QCW05", "FG42", "HK_G11", "HK_Caws", "SmithCarbine", "Gust",
            "Warden", "Kanye", "Elephant", "M2000", "Bayshore", "HandCanon",
            "Minigun", "Nugget", "Mortini", "DualLauncher", "RocketLauncher", "Prophet", "Phoenix", "Gamma", "GammaGen2",
            "BlankState", "Bublee", "DF_Blister", "DF_Cyst",
            "HandGrenade", "GlandGrenade",
            "ProximityMine", "APMine", "Claymore",
            "BaseballBat", "Stylus", "Nizeh", "JahvalMahmaerd", "BigFattyBro", "CurvedKnife", "Couperet", "Katana", "Flamberge", "DF_GodSword", "Impetus"
        };

        private static string DefaultWeaponOrder => string.Join(",", DefaultWeaponNames);

        private void Awake()
        {
            Log = Logger;
            _instance = this;

            Enabled = Config.Bind("General", "Enabled", true, "Enable Gun Game mode.");
            KillsToWin = Config.Bind(
                "General",
                "Kills To Win",
                DefaultWeaponNames.Length,
                new ConfigDescription(
                    "Kills before round ends.",
                    new AcceptableValueRange<int>(1, DefaultWeaponNames.Length)
                )
            );
            RespawnDelay = Config.Bind("General", "Respawn Delay", 3f, "Seconds before a dead player respawns.");
            WeaponOrder = Config.Bind(
                "Legacy",
                "Weapon Order",
                DefaultWeaponOrder,
                "Legacy comma-separated weapon progression. New installs should use the 66 dropdown entries in the Weapon Order section."
            );
            BindWeaponSlots();

            GunGamePatches.Apply();
            USceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnEnable() { _instance = this; }
        private void OnDisable() { if (_instance == this) _instance = null; }

        private void OnDestroy()
        {
            USceneManager.sceneLoaded -= OnSceneLoaded;
            if (_fishNetHooked && InstanceFinder.SceneManager != null)
                InstanceFinder.SceneManager.OnLoadEnd -= OnFishNetSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ResetGunGameState();

        private void OnFishNetSceneLoaded(SceneLoadEndEventArgs args)
        {
            foreach (var scene in args.LoadedScenes)
                ResetGunGameState();
        }

        private void ResetGunGameState() => FullReset();

        internal static void FullReset()
        {
            MatchOver = false;
            GunGameWeaponManager.ClearAllPending();
            GunGamePatches.ResetState();
            GunGamePatches.CleanupWorldEffects();

            try
            {
                if (PauseManager.Instance != null)
                {
                    PauseManager.Instance.startRound = false;
                    PauseManager.Instance.onStartRoundScreen = false;
                }
            }
            catch { }
        }

        private void Update()
        {
            var sm = InstanceFinder.SceneManager;
            if (!_fishNetHooked && sm != null)
            {
                sm.OnLoadEnd += OnFishNetSceneLoaded;
                _fishNetHooked = true;
            }
            else if (_fishNetHooked && sm == null)
            {
                _fishNetHooked = false;
                FullReset();
            }

            if (!Enabled.Value || GameManager.Instance == null || MatchOver) return;
            if (InstanceFinder.NetworkManager == null || !InstanceFinder.NetworkManager.IsServer) return;

            foreach (var kvp in ClientInstance.playerInstances)
            {
                if (!GameManager.Instance.alivePlayers.Contains(kvp.Key))
                    GameManager.Instance.alivePlayers.Add(kvp.Key);
            }
        }

        public static int GetOrderedWeaponCount()
        {
            if (WeaponSlots != null && WeaponSlots.Length > 0)
                return WeaponSlots.Length;

            string orderStr = WeaponOrder?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(orderStr))
            {
                string[] names = orderStr.Split(',');
                return names.Length;
            }

            SpawnerManager.PopulateAllWeapons();
            return SpawnerManager.AllWeapons?.Length ?? 0;
        }

        public static GameObject GetOrderedWeaponPrefab(int index)
        {
            SpawnerManager.PopulateAllWeapons();

            if (WeaponSlots != null && index >= 0 && index < WeaponSlots.Length)
            {
                string slotWeaponName = WeaponSlots[index]?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(slotWeaponName) &&
                    SpawnerManager.NameToWeaponDict.TryGetValue(slotWeaponName, out var slotPrefab))
                {
                    return slotPrefab;
                }

                return null;
            }

            string orderStr = WeaponOrder?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(orderStr))
            {
                string[] names = orderStr.Split(',');
                if (index < 0 || index >= names.Length)
                    return null;

                string weaponName = names[index].Trim();
                if (SpawnerManager.NameToWeaponDict.TryGetValue(weaponName, out var prefab))
                    return prefab;

                return null;
            }

            if (SpawnerManager.AllWeapons != null && index >= 0 && index < SpawnerManager.AllWeapons.Length)
                return SpawnerManager.AllWeapons[index];

            return null;
        }

        private void BindWeaponSlots()
        {
            WeaponSlots = new ConfigEntry<string>[DefaultWeaponNames.Length];
            string[] migratedNames = ParseLegacyWeaponOrder();
            var acceptableWeapons = new AcceptableValueList<string>(DefaultWeaponNames);

            for (int i = 0; i < WeaponSlots.Length; i++)
            {
                string defaultWeapon = i < migratedNames.Length && IsKnownWeaponName(migratedNames[i])
                    ? migratedNames[i]
                    : DefaultWeaponNames[i];

                WeaponSlots[i] = Config.Bind(
                    "Weapon Order",
                    $"Slot {i + 1:00}",
                    defaultWeapon,
                    new ConfigDescription(
                        $"Weapon given at progression slot {i + 1}.",
                        acceptableWeapons
                    )
                );
            }
        }

        private static string[] ParseLegacyWeaponOrder()
        {
            string orderStr = WeaponOrder?.Value ?? "";
            if (string.IsNullOrWhiteSpace(orderStr))
                return Array.Empty<string>();

            string[] names = orderStr.Split(',');
            for (int i = 0; i < names.Length; i++)
                names[i] = names[i].Trim();

            return names;
        }

        private static bool IsKnownWeaponName(string weaponName)
        {
            if (string.IsNullOrWhiteSpace(weaponName))
                return false;

            for (int i = 0; i < DefaultWeaponNames.Length; i++)
                if (DefaultWeaponNames[i] == weaponName)
                    return true;

            return false;
        }

        internal static PlayerPickup FindPickupForPlayerId(int playerId)
        {
            try
            {
                if (ClientInstance.playerInstances.TryGetValue(playerId, out var clientInstance) && clientInstance != null)
                {
                    var pm = clientInstance.GetComponent<PlayerManager>();
                    if (pm?.player != null)
                    {
                        var pickup = pm.player.GetComponentInChildren<PlayerPickup>(true);
                        if (pickup != null) return pickup;
                    }
                    var pickupChild = clientInstance.GetComponentInChildren<PlayerPickup>(true);
                    if (pickupChild != null) return pickupChild;
                }
            }
            catch { }

            foreach (var pickup in FindObjectsOfType<PlayerPickup>(true))
            {
                if (pickup?.playerValues == null) continue;
                var client = pickup.playerValues.sync___get_value_playerClient();
                if (client?.PlayerId == playerId) return pickup;
            }
            return null;
        }
    }
}
