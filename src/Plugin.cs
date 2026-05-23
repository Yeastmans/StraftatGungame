using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Scened;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace GunGameMod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.koki.weapons", BepInDependency.DependencyFlags.SoftDependency)]
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
        public static ConfigEntry<string>[] WeaponSlots;

        public static bool MatchOver = false;
        private bool _fishNetHooked = false;
        private const float ModMenuDropdownWidth = 520f;
        private float _nextDropdownResizeTime = 0f;
        private static Dictionary<string, string[]> WeaponConfigValueToNames = new Dictionary<string, string[]>(StringComparer.Ordinal);

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

        private static readonly string[][] KnownCustomWeaponNameGroups = new[]
        {
            new[] { "Teleport Mine", "TPTrap", "tptrap" },
            new[] { "Repulsion Grenade", "RepulsionGrenade", "RepulsorGrenadeMerged", "KBGrenade", "repulsiongrenade" }
        };

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

            WidenModMenuDropdowns();

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
            _instance?.EnsureWeaponSlotsBound();

            if (WeaponSlots != null && WeaponSlots.Length > 0)
                return WeaponSlots.Length;

            SpawnerManager.PopulateAllWeapons();
            return SpawnerManager.AllWeapons?.Length ?? 0;
        }

        public static GameObject GetOrderedWeaponPrefab(int index)
        {
            _instance?.EnsureWeaponSlotsBound();
            SpawnerManager.PopulateAllWeapons();

            if (WeaponSlots != null && index >= 0 && index < WeaponSlots.Length)
            {
                string slotWeaponName = WeaponSlots[index]?.Value?.Trim();
                if (TryResolveConfiguredWeaponPrefab(slotWeaponName, out var slotPrefab))
                    return slotPrefab;

                return null;
            }

            if (SpawnerManager.AllWeapons != null && index >= 0 && index < SpawnerManager.AllWeapons.Length)
                return SpawnerManager.AllWeapons[index];

            return null;
        }

        private void EnsureWeaponSlotsBound()
        {
            if (WeaponSlots != null)
                return;

            BindWeaponSlots();
        }

        private void BindWeaponSlots()
        {
            WeaponSlots = new ConfigEntry<string>[DefaultWeaponNames.Length];
            string[] availableWeaponValues = GetAvailableWeaponConfigValues();
            var acceptableWeapons = new AcceptableValueList<string>(availableWeaponValues);

            for (int i = 0; i < WeaponSlots.Length; i++)
            {
                WeaponSlots[i] = Config.Bind(
                    "Weapon Order",
                    $"Slot {i + 1:00}",
                    DefaultWeaponNames[i],
                    new ConfigDescription(
                        $"Weapon given at progression slot {i + 1}.",
                        acceptableWeapons
                    )
                );
            }
        }

        private static string[] GetAvailableWeaponConfigValues()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < DefaultWeaponNames.Length; i++)
                AddWeaponName(DefaultWeaponNames[i], names, seen);

            foreach (var group in KnownCustomWeaponNameGroups)
                if (group.Length > 0)
                    AddWeaponName(group[0], names, seen);

            try
            {
                SpawnerManager.PopulateAllWeapons();

                if (SpawnerManager.NameToWeaponDict != null)
                {
                    foreach (string weaponName in SpawnerManager.NameToWeaponDict.Keys)
                        AddWeaponName(weaponName, names, seen);
                }

                if (SpawnerManager.AllWeapons != null)
                {
                    foreach (var weapon in SpawnerManager.AllWeapons)
                        if (weapon != null)
                            AddWeaponName(weapon.name, names, seen);
                }
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"Could not discover custom weapons for config dropdowns: {ex.GetType().Name}");
            }

            return BuildWeaponConfigValues(names);
        }

        private static void AddWeaponName(string weaponName, List<string> names, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(weaponName))
                return;

            weaponName = weaponName.Replace("(Clone)", "").Trim();
            if (seen.Add(weaponName))
                names.Add(weaponName);
        }

        private static string[] BuildWeaponConfigValues(List<string> weaponNames)
        {
            var values = new List<string>();
            var usedValues = new HashSet<string>(StringComparer.Ordinal);
            var valueToNames = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (string weaponName in weaponNames)
            {
                string configValue = weaponName;
                string[] candidates = GetWeaponNameCandidates(weaponName);

                configValue = MakeUniqueConfigValue(configValue, usedValues);
                values.Add(configValue);
                valueToNames[configValue] = candidates;

                foreach (string candidate in candidates)
                    if (!valueToNames.ContainsKey(candidate))
                        valueToNames[candidate] = candidates;
            }

            WeaponConfigValueToNames = valueToNames;
            return values.ToArray();
        }

        private static string MakeUniqueConfigValue(string configValue, HashSet<string> usedValues)
        {
            if (usedValues.Add(configValue))
                return configValue;

            int suffix = 2;
            while (true)
            {
                string suffixText = $" #{suffix}";
                string candidate = configValue + suffixText;

                if (usedValues.Add(candidate))
                    return candidate;

                suffix++;
            }
        }

        private static string[] GetWeaponNameCandidates(string weaponName)
        {
            foreach (var group in KnownCustomWeaponNameGroups)
            {
                foreach (string candidate in group)
                {
                    if (string.Equals(candidate, weaponName, StringComparison.Ordinal))
                        return group;
                }
            }

            return new[] { weaponName };
        }

        private static bool TryResolveConfiguredWeaponPrefab(string configValue, out GameObject prefab)
        {
            prefab = null;

            if (string.IsNullOrWhiteSpace(configValue))
                return false;

            configValue = configValue.Trim();
            if (WeaponConfigValueToNames.TryGetValue(configValue, out string[] weaponNames))
            {
                foreach (string weaponName in weaponNames)
                    if (!string.IsNullOrWhiteSpace(weaponName) && SpawnerManager.NameToWeaponDict.TryGetValue(weaponName, out prefab))
                        return true;
            }

            if (SpawnerManager.NameToWeaponDict.TryGetValue(configValue, out prefab))
                return true;

            return false;
        }

        private void WidenModMenuDropdowns()
        {
            if (Time.unscaledTime < _nextDropdownResizeTime)
                return;

            _nextDropdownResizeTime = Time.unscaledTime + 0.5f;

            try
            {
                foreach (var dropdown in FindObjectsOfType<TMP_Dropdown>(true))
                {
                    if (dropdown == null)
                        continue;

                    bool hasGunGameWeapon = false;
                    foreach (var option in dropdown.options)
                    {
                        if (option != null && WeaponConfigValueToNames.ContainsKey(option.text))
                        {
                            hasGunGameWeapon = true;
                            break;
                        }
                    }

                    if (!hasGunGameWeapon)
                        continue;

                    WidenRect(dropdown.GetComponent<RectTransform>());
                    WidenLayout(dropdown.GetComponent<LayoutElement>());
                    WidenText(dropdown.captionText);
                    WidenText(dropdown.itemText);

                    if (dropdown.template != null)
                    {
                        WidenRect(dropdown.template);
                        WidenLayout(dropdown.template.GetComponent<LayoutElement>());
                    }
                }
            }
            catch { }
        }

        private static void WidenRect(RectTransform rect)
        {
            if (rect == null || rect.rect.width >= ModMenuDropdownWidth)
                return;

            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ModMenuDropdownWidth);
        }

        private static void WidenLayout(LayoutElement layout)
        {
            if (layout == null)
                return;

            layout.minWidth = Mathf.Max(layout.minWidth, ModMenuDropdownWidth);
            layout.preferredWidth = Mathf.Max(layout.preferredWidth, ModMenuDropdownWidth);
            layout.flexibleWidth = Mathf.Max(layout.flexibleWidth, 1f);
        }

        private static void WidenText(TMP_Text text)
        {
            if (text == null)
                return;

            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
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
