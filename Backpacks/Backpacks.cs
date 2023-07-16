using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ItemManager;
using ServerSync;
using ItemDataManager;
using LocalizationManager;
using UnityEngine;

namespace Backpacks;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
public class Backpacks : BaseUnityPlugin
{
	private const string ModName = "Backpacks";
	private const string ModVersion = "1.1.0";
	private const string ModGUID = "org.bepinex.plugins.backpacks";

	private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	public static ConfigEntry<Toggle> preventInventoryClosing = null!;
	public static ConfigEntry<string> backpackRows = null!;
	public static ConfigEntry<string> backpackColumns = null!;
	public static ConfigEntry<int> backpackWeightFactor = null!;
	public static ConfigEntry<Toggle> preventTeleportation = null!;
	public static ConfigEntry<Toggle> backpackCeption = null!;
	public static ConfigEntry<Toggle> backpackChests = null!;

	public static List<int> backpackRowsByLevel = new();
	public static List<int> backpackColumnsByLevel = new();

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	public enum Toggle
	{
		On = 1,
		Off = 0
	}

	private static Item backpack = null!;

	public void Awake()
	{
		Localizer.Load();

		backpack = new Item("bp_explorer", "bp_explorer");

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		preventInventoryClosing = config("2 - Backpack", "Prevent Closing", Toggle.On, "If on, pressing the interact key will not close the inventory.", false);
		backpackRows = config("2 - Backpack", "Backpack Slot Rows", "3, 3, 4, 4, 3", new ConfigDescription("Rows in a Backpack. One number for each upgrade level. Adding more numbers adds more upgrades. Changing this value does not affect existing Backpacks."));
		backpackRows.SettingChanged += (_, _) => ParseBackpackSize();
		backpackColumns = config("2 - Backpack", "Backpack Slot Columns", "5, 6, 5, 6, 7", new ConfigDescription("Columns in a Backpack. One number for each upgrade level. Adding more numbers adds more upgrades. Changing this value does not affect existing Backpacks."));
		backpackWeightFactor = config("2 - Backpack", "Backpack Weight", 100, new ConfigDescription("Weight of items inside a Backpack.", new AcceptableValueRange<int>(0, 100)));
		preventTeleportation = config("2 - Backpack", "Backpack Teleportation Check", Toggle.On, new ConfigDescription("If off, portals do not check the content of a backpack upon teleportation."));
		backpackCeption = config("2 - Backpack", "Backpacks in Backpacks", Toggle.Off, new ConfigDescription("If on, you can put backpacks into backpacks."));
		backpackChests = config("2 - Backpack", "Backpacks in Chests", Toggle.Off, new ConfigDescription("If on, you can put backpacks that aren't empty into chests, to make the chests bigger on the inside."));

		ParseBackpackSize();

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);

		backpack.Crafting.Add(CraftingTable.Workbench, 2);
		backpack.RequiredItems.Add("BronzeNails", 5);
		backpack.RequiredItems.Add("DeerHide", 10);
		backpack.RequiredItems.Add("LeatherScraps", 10);
		backpack.RequiredUpgradeItems.Add("Iron", 5, 2);
		backpack.RequiredUpgradeItems.Add("ElderBark", 10, 2);
		backpack.RequiredUpgradeItems.Add("Guck", 2, 2);
		backpack.RequiredUpgradeItems.Add("Silver", 5, 3);
		backpack.RequiredUpgradeItems.Add("WolfPelt", 3, 3);
		backpack.RequiredUpgradeItems.Add("JuteRed", 2, 3);
		backpack.RequiredUpgradeItems.Add("BlackMetal", 5, 4);
		backpack.RequiredUpgradeItems.Add("LinenThread", 10, 4);
		backpack.RequiredUpgradeItems.Add("LoxPelt", 2, 4);
		backpack.RequiredUpgradeItems.Add("JuteBlue", 5, 5);
		backpack.RequiredUpgradeItems.Add("ScaleHide", 5, 5);
		backpack.RequiredUpgradeItems.Add("Carapace", 5, 5);

		backpack.Prefab.GetComponent<ItemDrop>().m_itemData.Data().Add<ItemContainer>();
	}

	private static void ParseBackpackSize()
	{
		backpackRowsByLevel.Clear();
		backpackColumnsByLevel.Clear();

		foreach (string s in backpackRows.Value.Split(','))
		{
			if (int.TryParse(s.Trim(), out int size))
			{
				if (size > 8)
				{
					Debug.LogWarning("Found row configuration that exceeds 8 for Backpack. You should avoid this. Limited size to 8.");
					size = 8;
				}

				backpackRowsByLevel.Add(size);
				continue;
			}

			Debug.LogError("Found invalid row configuration for Backpack. Falling back to default config.");
			backpackRowsByLevel = ((string)backpackRows.DefaultValue).Split(',').Select(s => int.Parse(s.Trim())).ToList();
		}

		foreach (string s in backpackColumns.Value.Split(','))
		{
			if (int.TryParse(s.Trim(), out int size))
			{
				if (size > 8)
				{
					Debug.LogWarning("Found column configuration that exceeds 8 for Backpack. You should avoid this. Limited size to 8.");
					size = 8;
				}

				backpackColumnsByLevel.Add(size);
				continue;
			}

			Debug.LogError("Found invalid column configuration for Backpack. Falling back to default config.");
			backpackColumnsByLevel = ((string)backpackRows.DefaultValue).Split(',').Select(s => int.Parse(s.Trim())).ToList();
		}

		int maxQuality = Math.Min(backpackRowsByLevel.Count, backpackColumnsByLevel.Count);
		backpack.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality = maxQuality;

		if (ObjectDB.instance)
		{
			Inventory[] inventories = Player.s_players.Select(p => p.GetInventory()).Concat(FindObjectsOfType<Container>().Select(c => c.GetInventory())).Where(c => c is not null).ToArray();
			foreach (ItemDrop.ItemData itemdata in ObjectDB.instance.m_items.Select(p => p.GetComponent<ItemDrop>()).Where(c => c && c.GetComponent<ZNetView>()).Concat(ItemDrop.s_instances).Select(i => i.m_itemData).Concat(inventories.SelectMany(i => i.GetAllItems())))
			{
				if (itemdata.m_shared.m_name == backpack.Prefab.name)
				{
					itemdata.m_shared.m_maxQuality = maxQuality;
				}
			}
		}
	}

	[HarmonyPatch]
	private static class PreventBackpacksInChests
	{
		private static IEnumerable<MethodInfo> TargetMethods() => typeof(Inventory).GetMethods().Where(m => m.Name == nameof(Inventory.MoveItemToThis));

		private static bool Prefix(Inventory __instance, ItemDrop.ItemData item)
		{
			if (__instance == InventoryGui.instance.m_currentContainer?.m_inventory && item.Data().Get<ItemContainer>()?.CanBePutInContainer() == false)
			{
				Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$bp_cant_put_in_chest"));
				return false;
			}

			return true;
		}
	}
}
