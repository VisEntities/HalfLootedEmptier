/*
 * Copyright (C) 2026 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using Rust;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Half Looted Emptier", "VisEntities", "1.2.0")]
    [Description("Empties loot containers that players leave half-looted.")]
    public class HalfLootedEmptier : RustPlugin
    {
        #region Fields

        private static HalfLootedEmptier _plugin;
        private static Configuration _config;
        private static Dictionary<ulong, List<Item>> _originalContainerItems = new Dictionary<ulong, List<Item>>();
        private static Dictionary<ulong, Timer> _containerEmptyTimers = new Dictionary<ulong, Timer>();
        private static Dictionary<ulong, Timer> _junkpileDestroyTimers = new Dictionary<ulong, Timer>();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Empty After At Least This Many Items Are Looted (0 = off)")]
            public int EmptyAfterAtLeastThisManyItemsAreLooted { get; set; }

            [JsonProperty("Empty When At Most This Many Items Remain (0 = off)")]
            public int EmptyWhenAtMostThisManyItemsRemain { get; set; }

            [JsonProperty("Delay Before Emptying After Looting Stops (seconds)")]
            public float DelayBeforeEmptyingAfterLootingStops { get; set; }

            [JsonProperty("Remove Items Instead Of Dropping (true = delete, false = drop on ground)")]
            public bool RemoveItemsInsteadOfDropping { get; set; }

            [JsonProperty("Destroy Whole Junkpile Instead Of Just The Looted Container (only affects junkpile loot)")]
            public bool DestroyWholeJunkpileInsteadOfJustTheLootedContainer { get; set; }

            [JsonProperty("Junkpile Search Radius (meters)")]
            public float JunkpileSearchRadius { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            if (string.Compare(_config.Version, "1.2.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                EmptyAfterAtLeastThisManyItemsAreLooted = 1,
                EmptyWhenAtMostThisManyItemsRemain = 0,
                DelayBeforeEmptyingAfterLootingStops = 30f,
                RemoveItemsInsteadOfDropping = false,
                DestroyWholeJunkpileInsteadOfJustTheLootedContainer = false,
                JunkpileSearchRadius = 5f
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
        }

        private void Unload()
        {
            foreach (Timer timer in _containerEmptyTimers.Values)
            {
                if (timer != null)
                    timer.Destroy();
            }

            foreach (Timer timer in _junkpileDestroyTimers.Values)
            {
                if (timer != null)
                    timer.Destroy();
            }

            _config = null;
            _plugin = null;
        }

        private void OnLootEntity(BasePlayer player, LootContainer lootContainer)
        {
            if (player == null || lootContainer == null || lootContainer.net == null)
                return;

            ulong containerId = lootContainer.net.ID.Value;

            if (_containerEmptyTimers.TryGetValue(containerId, out Timer existingTimer))
            {
                existingTimer.Destroy();
                _containerEmptyTimers.Remove(containerId);
            }

            _originalContainerItems[containerId] = new List<Item>(lootContainer.inventory.itemList.ToArray());
        }

        private void OnLootEntityEnd(BasePlayer player, LootContainer lootContainer)
        {
            if (player == null || lootContainer == null || lootContainer.net == null)
                return;

            ulong containerId = lootContainer.net.ID.Value;
            if (!_originalContainerItems.TryGetValue(containerId, out List<Item> originalItems))
                return;

            List<Item> remainingItems = lootContainer.inventory.itemList;
            if (remainingItems == null)
                return;

            bool triggerEmpty = false;

            int lootedCount = originalItems.Count - remainingItems.Count;
            if (_config.EmptyAfterAtLeastThisManyItemsAreLooted > 0 && lootedCount >= _config.EmptyAfterAtLeastThisManyItemsAreLooted)
                triggerEmpty = true;

            if (_config.EmptyWhenAtMostThisManyItemsRemain > 0 && remainingItems.Count > 0 && remainingItems.Count <= _config.EmptyWhenAtMostThisManyItemsRemain)
                triggerEmpty = true;

            if (triggerEmpty)
            {
                bool junkpileDestroyScheduled = false;
                if (_config.DestroyWholeJunkpileInsteadOfJustTheLootedContainer)
                    junkpileDestroyScheduled = TryScheduleJunkpileDestruction(lootContainer);

                if (junkpileDestroyScheduled)
                {
                    _originalContainerItems.Remove(containerId);
                }
                else
                {
                    _containerEmptyTimers[containerId] = timer.Once(_config.DelayBeforeEmptyingAfterLootingStops, () =>
                    {
                        if (lootContainer == null)
                            return;

                        if (_config.RemoveItemsInsteadOfDropping)
                            lootContainer.inventory.Clear();
                        else
                            DropUtil.DropItems(lootContainer.inventory, lootContainer.GetDropPosition());

                        lootContainer.Kill(BaseNetworkable.DestroyMode.Gib);
                        _containerEmptyTimers.Remove(containerId);
                        _originalContainerItems.Remove(containerId);
                    });
                }
            }
            else
            {
                _originalContainerItems.Remove(containerId);
            }
        }

        private void OnEntityKill(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null)
                return;

            ulong containerId = lootContainer.net.ID.Value;

            if (_containerEmptyTimers.TryGetValue(containerId, out Timer existingTimer))
            {
                existingTimer.Destroy();
                _containerEmptyTimers.Remove(containerId);
            }

            _originalContainerItems.Remove(containerId);
        }

        #endregion Oxide Hooks

        #region Junkpile Destruction

        private bool TryScheduleJunkpileDestruction(LootContainer lootContainer)
        {
            SpawnPointInstance spawnPointInstance = lootContainer.GetComponent<SpawnPointInstance>();
            if (spawnPointInstance == null)
                return false;

            SpawnGroup spawnGroup = spawnPointInstance.parentSpawnPointUser as SpawnGroup;
            if (spawnGroup == null)
                return false;

            JunkPile junkPile = null;
            List<JunkPile> junkPiles = Pool.Get<List<JunkPile>>();
            Vis.Entities(lootContainer.transform.position, _config.JunkpileSearchRadius, junkPiles, Layers.Solid);
            foreach (JunkPile candidate in junkPiles)
            {
                if (candidate != null && candidate.spawngroups != null && candidate.spawngroups.Contains(spawnGroup))
                {
                    junkPile = candidate;
                    break;
                }
            }
            Pool.FreeUnmanaged(ref junkPiles);

            if (junkPile == null || junkPile.net == null)
                return false;

            ulong junkPileId = junkPile.net.ID.Value;
            if (_junkpileDestroyTimers.ContainsKey(junkPileId))
                return true;

            _junkpileDestroyTimers[junkPileId] = timer.Once(_config.DelayBeforeEmptyingAfterLootingStops, () =>
            {
                _junkpileDestroyTimers.Remove(junkPileId);

                if (junkPile == null || junkPile.IsDestroyed)
                    return;

                List<LootContainer> groupContainers = Pool.Get<List<LootContainer>>();
                Vis.Entities(junkPile.transform.position, _config.JunkpileSearchRadius, groupContainers, Layers.Solid);
                foreach (LootContainer loot in groupContainers)
                {
                    if (loot == null || loot.IsDestroyed || loot.inventory == null)
                        continue;

                    SpawnPointInstance lootSpawnPoint = loot.GetComponent<SpawnPointInstance>();
                    if (lootSpawnPoint == null)
                        continue;

                    SpawnGroup lootSpawnGroup = lootSpawnPoint.parentSpawnPointUser as SpawnGroup;
                    if (lootSpawnGroup == null || !junkPile.spawngroups.Contains(lootSpawnGroup))
                        continue;

                    if (_config.RemoveItemsInsteadOfDropping)
                        loot.inventory.Clear();
                    else
                        DropUtil.DropItems(loot.inventory, loot.GetDropPosition());
                }
                Pool.FreeUnmanaged(ref groupContainers);

                junkPile.SinkAndDestroy();
            });

            return true;
        }

        #endregion Junkpile Destruction
    }
}