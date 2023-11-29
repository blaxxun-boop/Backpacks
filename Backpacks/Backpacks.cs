using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ItemManager;
using ServerSync;
using ItemDataManager;
using JetBrains.Annotations;
using LocalizationManager;
using UnityEngine;

namespace Backpacks;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
[BepInDependency("Azumatt.AzuExtendedPlayerInventory", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Backpacks : BaseUnityPlugin
{
	internal const string ModName = "Backpacks";
	private const string ModVersion = "1.3.1";
	private const string ModGUID = "org.bepinex.plugins.backpacks";

	internal static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	internal static SyncedConfigEntry<Toggle> useExternalYaml = null!;
	public static ConfigEntry<Toggle> preventInventoryClosing = null!;
	private static ConfigEntry<string> backpackRows = null!;
	private static ConfigEntry<string> backpackColumns = null!;
	public static ConfigEntry<int> backpackWeightFactor = null!;
	public static ConfigEntry<Toggle> preventTeleportation = null!;
	public static ConfigEntry<Toggle> backpackCeption = null!;
	public static ConfigEntry<Toggle> backpackChests = null!;
	public static ConfigEntry<Unique> uniqueBackpack = null!;
	public static ConfigEntry<Toggle> hiddenBackpack = null!;
	private static ConfigEntry<Toggle> autoOpenBackpack = null!;
	private static ConfigEntry<string> equipStatusEffect = null!;
	public static ConfigEntry<Toggle> autoFillBackpacks = null!;

	public static List<int> backpackRowsByLevel = new();
	public static List<int> backpackColumnsByLevel = new();

	internal static List<string> configFilePaths = null!;
	internal static readonly CustomSyncedValue<List<string>> customBackpackDefinition = new(configSync, "custom backpacks", new List<string>());

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
		Off = 0,
	}

	public static Item Backpack = null!;

	[PublicAPI]
	public class ConfigurationManagerAttributes
	{
		public bool? HideSettingName;
		public bool? HideDefaultButton;
		public Action<ConfigEntryBase>? CustomDrawer;
	}

	public void Awake()
	{
		APIManager.Patcher.Patch(new[] { typeof(ItemData).Namespace });
		Localizer.Load();
		configFilePaths = new List<string> { Path.GetDirectoryName(Config.ConfigFilePath), Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) };

		Backpack = new Item("bp_explorer", "bp_explorer");
		
		foreach (SkinnedMeshRenderer renderer in Backpack.Prefab.transform.Find("attach_skin/Mesh").GetComponentsInChildren<SkinnedMeshRenderer>(true))
		{
			CustomBackpackConfig.BackpackParts.Add(renderer.name);
		}

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		useExternalYaml = configSync.AddConfigEntry(Config.Bind("2 - Backpack", "Use External YAML", Toggle.Off, "If set to on, the YAML file from your config folder will be used, to implement custom Backpacks inside of that file."));
		useExternalYaml.SourceConfig.SettingChanged += (_, _) => ConfigLoader.reloadConfigFile();
		config("2 - Backpack", "YAML Editor Anchor", 0, new ConfigDescription("Just ignore this.", null, new ConfigurationManagerAttributes { HideSettingName = true, HideDefaultButton = true, CustomDrawer = DrawYamlEditorButton }), false);
		preventInventoryClosing = config("2 - Backpack", "Prevent Closing", Toggle.On, new ConfigDescription("If on, pressing the interact key will not close the inventory."), false);
		backpackRows = config("2 - Backpack", "Backpack Slot Rows", "3, 3, 4, 4, 4", new ConfigDescription("Rows in a Backpack. One number for each upgrade level. Adding more numbers adds more upgrades. Changing this value does not affect existing Backpacks."));
		backpackRows.SettingChanged += (_, _) => ParseBackpackSize();
		backpackColumns = config("2 - Backpack", "Backpack Slot Columns", "5, 6, 5, 6, 7", new ConfigDescription("Columns in a Backpack. One number for each upgrade level. Adding more numbers adds more upgrades. Changing this value does not affect existing Backpacks."));
		backpackColumns.SettingChanged += (_, _) => ParseBackpackSize();
		backpackWeightFactor = config("2 - Backpack", "Backpack Weight", 100, new ConfigDescription("Weight of items inside a Backpack.", new AcceptableValueRange<int>(0, 100)));
		preventTeleportation = config("2 - Backpack", "Backpack Teleportation Check", Toggle.On, new ConfigDescription("If off, portals do not check the content of a backpack upon teleportation."));
		backpackCeption = config("2 - Backpack", "Backpacks in Backpacks", Toggle.Off, new ConfigDescription("If on, you can put backpacks into backpacks."));
		backpackChests = config("2 - Backpack", "Backpacks in Chests", Toggle.Off, new ConfigDescription("If on, you can put backpacks that aren't empty into chests, to make the chests bigger on the inside."));
		uniqueBackpack = config("2 - Backpack", "Unique Backpack", Unique.Global, new ConfigDescription("Can be used to restrict the number of backpacks a player can have in their inventory.\nGlobal: Only one backpack.\nRestricted: No other backpacks without item restrictions allowed.\nType: Only one backpack of each type.\nBypass: As many backpacks as you want.\nNone: Same as bypass, but doesn't overrule every other flag."));
		hiddenBackpack = config("2 - Backpack", "Hide Backpack", Toggle.Off, new ConfigDescription("If on, the backpack visual is hidden."), false);
		hiddenBackpack.SettingChanged += (_, _) =>
		{
			foreach (Visual visual in Visual.visuals.Values)
			{
				visual.forceSetBackpackEquipped(visual.currentBackpackItemHash);
			}
		};
		autoOpenBackpack = config("2 - Backpack", "Auto Open Backpack", Toggle.On, new ConfigDescription("If on, the backpack in your backpack slot is opened automatically, if the inventory is opened."), false);
		equipStatusEffect = config("2 - Backpack", "Equip Status Effect", "", new ConfigDescription("Name of a status effect that should be applied to the player, if the backpack is equipped."));
		equipStatusEffect.SettingChanged += (_, _) => AddStatusEffectToBackpack.Postfix(ObjectDB.instance);
		autoFillBackpacks = config("2 - Backpack", "Auto Fill Backpack", Toggle.On, new ConfigDescription("If on, items you pick up are added to your backpack. Conditions apply."), false);

		ParseBackpackSize();

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);

		Backpack.Crafting.Add(CraftingTable.Workbench, 2);
		Backpack.RequiredItems.Add("BronzeNails", 5);
		Backpack.RequiredItems.Add("DeerHide", 10);
		Backpack.RequiredItems.Add("LeatherScraps", 10);
		Backpack.RequiredUpgradeItems.Add("Iron", 5, 2);
		Backpack.RequiredUpgradeItems.Add("ElderBark", 10, 2);
		Backpack.RequiredUpgradeItems.Add("Guck", 2, 2);
		Backpack.RequiredUpgradeItems.Add("Silver", 5, 3);
		Backpack.RequiredUpgradeItems.Add("WolfPelt", 3, 3);
		Backpack.RequiredUpgradeItems.Add("JuteRed", 2, 3);
		Backpack.RequiredUpgradeItems.Add("BlackMetal", 5, 4);
		Backpack.RequiredUpgradeItems.Add("LinenThread", 10, 4);
		Backpack.RequiredUpgradeItems.Add("LoxPelt", 2, 4);
		Backpack.RequiredUpgradeItems.Add("JuteBlue", 5, 5);
		Backpack.RequiredUpgradeItems.Add("ScaleHide", 5, 5);
		Backpack.RequiredUpgradeItems.Add("Carapace", 5, 5);

		Backpack.Prefab.GetComponent<ItemDrop>().m_itemData.Data().Add<ItemContainer>();

		if (AzuExtendedPlayerInventory.API.IsLoaded())
		{
			AzuExtendedPlayerInventory.API.AddSlot("Backpack", player => Visual.visuals.TryGetValue(player.m_visEquipment, out Visual visual) ? visual.equippedBackpackItem : null, validateBackpack);
		}
	}

	private static bool validateBackpack(ItemDrop.ItemData item) => item.Data().Get<ItemContainer>() is { } backpack && backpack.IsEquipable();

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
		Backpack.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality = maxQuality;

		if (ObjectDB.instance)
		{
			Inventory[] inventories = Player.s_players.Select(p => p.GetInventory()).Concat(FindObjectsOfType<Container>().Select(c => c.GetInventory())).Where(c => c is not null).ToArray();
			foreach (ItemDrop.ItemData itemdata in ObjectDB.instance.m_items.Select(p => p.GetComponent<ItemDrop>()).Where(c => c && c.GetComponent<ZNetView>()).Concat(ItemDrop.s_instances).Select(i => i.m_itemData).Concat(inventories.SelectMany(i => i.GetAllItems())))
			{
				if (itemdata.m_shared.m_name == Backpack.Prefab.name)
				{
					itemdata.m_shared.m_maxQuality = maxQuality;
				}
			}
		}
	}

	[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
	private static class AddStatusEffectToBackpack
	{
		[HarmonyPriority(Priority.Low)]
		public static void Postfix(ObjectDB __instance)
		{
			if (__instance)
			{
				Backpack.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_equipStatusEffect = __instance.GetStatusEffect(equipStatusEffect.Value.GetStableHashCode());
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

	private static bool CheckUniqueConflict(Unique unique, ItemDrop.ItemData item_a, ItemDrop.ItemData item_b)
	{
		switch (unique)
		{
			case Unique.Global:
				return true;
			case Unique.Restricted:
				ItemContainer? container = item_b.Data().Get<ItemContainer>();
				return container is CustomBackpack ? CustomBackpack.AllowedItems.TryGetValue(item_b.m_shared.m_name, out List<string> allowList) && allowList.Count > 0 : container?.GetType() == typeof(ItemContainer);
			case Unique.Type:
				return item_a.m_shared.m_name == item_b.m_shared.m_name;
			default:
				return false;
		}
	}

	[HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
	private static class PreventMultipleBackpacks
	{
		[HarmonyPriority(Priority.Last)]
		public static void Postfix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
		{
			if (__result && __instance == Player.m_localPlayer?.GetInventory() && item.Data().Get<ItemContainer>()?.Uniqueness() is { } unique and not Unique.Bypass)
			{
				foreach (ItemDrop.ItemData inventoryItem in __instance.m_inventory)
				{
					if (inventoryItem.Data().Get<ItemContainer>()?.Uniqueness() is { } otherUnique and not Unique.Bypass && inventoryItem != item)
					{
						if (CheckUniqueConflict(unique, item, inventoryItem) || (unique != otherUnique && CheckUniqueConflict(otherUnique, inventoryItem, item)))
						{
							__result = false;
							return;
						}
					}
				}
			}
		}
	}

	[HarmonyPatch]
	private static class PreventMultipleBackpacksAddItem
	{
		private static IEnumerable<MethodInfo> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) }),
			AccessTools.DeclaredMethod(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) }),
		};

		private class SkipAddItemException : Exception;

		[HarmonyPriority(Priority.VeryLow)]
		private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, bool __runOriginal, ref bool __result)
		{
			if (!__runOriginal)
			{
				return true;
			}
			
			bool canAdd = true;
			PreventMultipleBackpacks.Postfix(__instance, item, ref canAdd);
			if (!canAdd)
			{
				Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$bp_cannot_pickup_backpack"));
				
				Transform transform = Player.m_localPlayer.transform;
				ItemDrop.DropItem(item, 1, transform.position + transform.forward + transform.up, Quaternion.identity);
				__result = true;
				throw new SkipAddItemException();
			}
			return canAdd;
		}

		private static Exception? Finalizer(Exception __exception) => __exception is SkipAddItemException ? null : __exception;
	}

	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
	private static class OpenBackpackAutomatically
	{
		private static void Postfix(InventoryGui __instance, Container? container)
		{
			if (container is null && autoOpenBackpack.Value == Toggle.On && Player.m_localPlayer && Visual.visuals.TryGetValue(Player.m_localPlayer.m_visEquipment, out Visual visual) && visual.equippedBackpackItem is {} backpack)
			{
				if (__instance.m_playerGrid.GetInventory() is null)
				{
					__instance.m_playerGrid.m_inventory = Player.m_localPlayer.GetInventory();
				}

				CustomContainer.OpenFakeItemsContainer.Open(__instance, backpack);
			}
		}
	}

	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
	private static class PreventCrafting
	{
		private static void Postfix(InventoryGui __instance)
		{
			if (__instance.m_selectedRecipe.Value is null && __instance.m_selectedRecipe.Key?.m_item?.m_itemData.Data().Get<ItemContainer>() is { } backpack)
			{
				bool canAdd = true;
				PreventMultipleBackpacks.Postfix(Player.m_localPlayer.GetInventory(), backpack.Item, ref canAdd);
				if (!canAdd)
				{
					__instance.m_craftButton.interactable = false;
					__instance.m_craftButton.GetComponent<UITooltip>().m_text = Localization.instance.Localize("$bp_cannot_pickup_backpack");
				}
			}
		}
	}
}
