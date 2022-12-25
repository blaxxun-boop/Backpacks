using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ItemManager;
using ServerSync;
using ItemDataManager;
using LocalizationManager;

namespace Backpacks;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class Backpacks : BaseUnityPlugin
{
	private const string ModName = "Backpacks";
	private const string ModVersion = "1.0.2";
	private const string ModGUID = "org.bepinex.plugins.backpacks";

	private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	public static ConfigEntry<Toggle> preventInventoryClosing = null!;
	public static ConfigEntry<int> backpackRows = null!;
	public static ConfigEntry<int> backpackColumns = null!;
	public static ConfigEntry<int> backpackWeightFactor = null!;
	public static ConfigEntry<Toggle> preventTeleportation = null!;
	public static ConfigEntry<Toggle> backpackCeption = null!;

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

	public void Awake()
	{
		Localizer.Load();

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		preventInventoryClosing = config("2 - Backpack", "Prevent Closing", Toggle.On, "If on, pressing the interact key will not close the inventory.", false);
		backpackRows = config("2 - Backpack", "Backpack Slot Rows", 3, new ConfigDescription("Rows in a Backpack. Changing this value does not affect existing Backpacks.", new AcceptableValueRange<int>(1, 4)));
		backpackColumns = config("2 - Backpack", "Backpack Slot Columns", 5, new ConfigDescription("Columns in a Backpack. Changing this value does not affect existing Backpacks.", new AcceptableValueRange<int>(1, 8)));
		backpackWeightFactor = config("2 - Backpack", "Backpack Weight", 100, new ConfigDescription("Weight of items inside a Backpack.", new AcceptableValueRange<int>(0, 100)));
		preventTeleportation = config("2 - Backpack", "Backpack Teleportation Check", Toggle.On, new ConfigDescription("If off, portals do not check the content of a backpack upon teleportation."));
		backpackCeption = config("2 - Backpack", "Backpacks in Backpacks", Toggle.Off, new ConfigDescription("If on, you can put backpacks into backpacks."));

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);

		Item backpack = new("bp_explorer", "bp_explorer");
		backpack.Crafting.Add(CraftingTable.Workbench, 2);
		backpack.RequiredItems.Add("BronzeNails", 5);
		backpack.RequiredItems.Add("DeerHide", 10);
		backpack.RequiredItems.Add("LeatherScraps", 10);

		backpack.Prefab.GetComponent<ItemDrop>().m_itemData.Data().Add<ItemContainer>();
	}
}
