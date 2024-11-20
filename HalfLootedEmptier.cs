/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Half Looted Emptier", "VisEntities", "1.0.0")]
    [Description(" ")]
    public class HalfLootedEmptier : RustPlugin
    {
        #region Fields

        private static HalfLootedEmptier _plugin;
        private static Configuration _config;
        private static Dictionary<ulong, List<Item>> _lootContainerItems = new Dictionary<ulong, List<Item>>();
        private static Dictionary<ulong, Timer> _containerEmptyingTimers = new Dictionary<ulong, Timer>();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }
            
            [JsonProperty("Number Of Unlooted Items For Container Emptying")]
            public int NumberOfUnlootedItemsForContainerEmptying { get; set; }

            [JsonProperty("Delay Before Emptying Container Seconds")]
            public float DelayBeforeEmptyingContainerSeconds { get; set; }

            [JsonProperty("Remove Items Instead Of Dropping")]
            public bool RemoveItemsInsteadOfDropping { get; set; }
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

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                NumberOfUnlootedItemsForContainerEmptying = 1,
                DelayBeforeEmptyingContainerSeconds = 30f,
                RemoveItemsInsteadOfDropping = false
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
            foreach (Timer timer in _containerEmptyingTimers.Values)
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

            if (_containerEmptyingTimers.TryGetValue(containerId, out Timer existingTimer))
            {
                existingTimer.Destroy();
                _containerEmptyingTimers.Remove(containerId);
            }

            _lootContainerItems[containerId] = new List<Item>(lootContainer.inventory.itemList.ToArray());
        }

        private void OnLootEntityEnd(BasePlayer player, LootContainer lootContainer)
        {   
            if (player == null || lootContainer == null || lootContainer.net == null)
                return;

            ulong containerId = lootContainer.net.ID.Value;
            if (!_lootContainerItems.TryGetValue(containerId, out List<Item> originalItems))
                return;

            List<Item> remainingItems = lootContainer.inventory.itemList;
            if (remainingItems == null)
                return;

            bool hasLeftoverItems = false;

            foreach (Item remainingItem in remainingItems)
            {
                if (remainingItem == null || remainingItem.info == null)
                    continue;

                bool isOriginal = false;
                foreach (Item originalItem in originalItems)
                {
                    if (originalItem == null || originalItem.info == null)
                        continue;

                    if (remainingItem.info.shortname == originalItem.info.shortname && remainingItem.amount <= originalItem.amount)
                    {
                        isOriginal = true;
                        break;
                    }
                }

                if (!isOriginal)
                {
                    hasLeftoverItems = true;
                    break;
                }
            }

            if (hasLeftoverItems || (remainingItems.Count > 0 && remainingItems.Count <= _config.NumberOfUnlootedItemsForContainerEmptying))
            {
                _containerEmptyingTimers[containerId] = timer.Once(_config.DelayBeforeEmptyingContainerSeconds, () =>
                {
                    if (lootContainer == null)
                        return;

                    if (_config.RemoveItemsInsteadOfDropping)
                        lootContainer.inventory.Clear();
                    else
                        DropUtil.DropItems(lootContainer.inventory, lootContainer.GetDropPosition() + new Vector3(0f, 0.5f, 0f));

                    lootContainer.Kill(BaseNetworkable.DestroyMode.Gib);
                    _containerEmptyingTimers.Remove(containerId);
                    _lootContainerItems.Remove(containerId);
                });
            }
            else
            {
                _lootContainerItems.Remove(containerId);
            }
        }

        private void OnEntityKill(LootContainer lootContainer)
        {
            if (lootContainer == null || lootContainer.net == null)
                return;

            ulong containerId = lootContainer.net.ID.Value;

            if (_containerEmptyingTimers.TryGetValue(containerId, out Timer existingTimer))
            {
                existingTimer.Destroy();
                _containerEmptyingTimers.Remove(containerId);
            }

            _lootContainerItems.Remove(containerId);
        }

        #endregion Oxide Hooks
    }
}