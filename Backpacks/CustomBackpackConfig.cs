using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using ItemDataManager;
using ItemManager;
using ServerSync;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backpacks;

public class CustomBackpackConfig
{
	public static readonly HashSet<string> BackpackParts = new(StringComparer.InvariantCultureIgnoreCase);

	private readonly List<Vector2i> Sizes = new();
	private readonly List<string> AllowedItems = new();
	private readonly List<Dictionary<string, int>> Costs = new();
	private readonly Dictionary<string, Appearance> appearance = new();
	private CraftingStationConfig CraftingStation = new();
	private string? Description = null;
	private bool? MaySkipTeleport = null;
	private float? ItemWeightFactor = null;
	private Unique? UniqueStatus = null;
	private string? statusEffect = null;

	private static Dictionary<string, Unique> uniqueMap = new(((Unique[])Enum.GetValues(typeof(Unique))).ToDictionary(u => u.ToString(), u => u), StringComparer.OrdinalIgnoreCase);

	private static Dictionary<string, object?> castDictToStringDict(Dictionary<object, object?> dict) => new(dict.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value), StringComparer.InvariantCultureIgnoreCase);

	private struct ParseResult
	{
		public Dictionary<string, CustomBackpackConfig> CustomBackpacks;
		public Dictionary<string, List<string>> Groups;
	}

	private class Appearance
	{
		public bool Visible;
		public Color Color = Color.clear;
	}

	private static ParseResult Parse(object? rootDictObj, out List<string> errors)
	{
		Dictionary<string, CustomBackpackConfig> customBackpacks = new();
		Dictionary<string, List<string>> groups = new();
		ParseResult configurationResult = new() { CustomBackpacks = customBackpacks, Groups = groups };
		errors = new List<string>();

		if (rootDictObj is not Dictionary<object, object?> rootDict)
		{
			if (rootDictObj is not null)
			{
				errors.Add($"All top-level keys must be a mapping. Got unexpected {rootDictObj.GetType()}.");
			}

			return configurationResult;
		}

		foreach (KeyValuePair<string, object?> rootDictKv in castDictToStringDict(rootDict))
		{
			if (rootDictKv.Key.Equals("groups", StringComparison.InvariantCultureIgnoreCase))
			{
				if (rootDictKv.Value is Dictionary<object, object?> groupsDict)
				{
					foreach (KeyValuePair<string, object?> groupKv in castDictToStringDict(groupsDict))
					{
						if (groupKv.Value is List<object?> prefabList)
						{
							List<string> group = new();
							foreach (object? prefabObj in prefabList)
							{
								if (prefabObj is string prefab)
								{
									group.Add(prefab);
								}
								else
								{
									errors.Add($"Values in groups must be prefab names. Got unexpected {prefabObj?.GetType().ToString() ?? "null"} in group definition '{groupKv.Key}'.");
								}
							}
							groups.Add(groupKv.Key, group);
						}
						else
						{
							errors.Add($"Each group must be a list of prefab names. Got unexpected {groupKv.Value?.GetType().ToString() ?? "null"} for group definition '{groupKv.Key}'.");
						}
					}
				}
				else
				{
					errors.Add($"The 'groups' section must be a mapping of group names to a list of prefab names. Got unexpected {rootDictKv.Value?.GetType().ToString() ?? "null"}.");
				}

				continue;
			}

			if (rootDictKv.Value is not Dictionary<object, object?> backpackDictObj)
			{
				errors.Add($"The backpack definitions must be all a mapping of backpack attribute names and values. Got unexpected {rootDictKv.Value?.GetType().ToString() ?? "null"}.");
				continue;
			}

			Dictionary<string, object?> backpackDict = castDictToStringDict(backpackDictObj);

			HashSet<string> knownKeys = new(StringComparer.InvariantCultureIgnoreCase);
			string errorLocation = $"in backpack definition for backpack '{rootDictKv.Key}'.";

			CustomBackpackConfig backpack = new();

			bool HasKey(string key)
			{
				knownKeys.Add(key);
				return backpackDict.ContainsKey(key);
			}

			string? parseBool(object? obj, out bool result)
			{
				result = false;
				if (obj is string input)
				{
					string[] falsy = { "0", "false", "off", "no", "nope", "nah", "-", "hell no", "pls dont", "lol no" };
					string[] truthy = { "1", "true", "on", "yes", "yep", "yeah", "+", "hell yeah", "ok", "okay", "k" };
					if (falsy.Contains(input.ToLower()))
					{
						result = false;
						return null;
					}

					if (truthy.Contains(input.ToLower()))
					{
						result = true;
						return null;
					}

					return $"Boolean values must be either true or false, found '{input}'";
				}

				return $"Boolean values must be either true or false, got unexpected {obj?.GetType().ToString() ?? "null"}";
			}

			string? parseSize(object? sizeObj, out Vector2i dimensions)
			{
				dimensions = new Vector2i();
				if (sizeObj is string size)
				{
					string[] split = size.Split('x');
					if (split.Length == 2 && int.TryParse(split[0], out int x) && int.TryParse(split[1], out int y))
					{
						if (x <= 8 && y > 0 && x > 0)
						{
							dimensions = new Vector2i(x, y);
						}
						else
						{
							return $"The backpack width must be between 1 and 8 and the height must be greater than zero. Got unexpected width {x} and height {y}";
						}
					}
					else
					{
						return $"The backpack size must be in [width]x[height] form. Got unexpected '{size}'";
					}
				}
				else
				{
					return $"The backpack size must be a string in [width]x[height] form. Got unexpected {sizeObj?.GetType().ToString() ?? "null"}";
				}

				return null;
			}

			if (HasKey("size"))
			{
				if (parseSize(backpackDict["size"], out Vector2i dimensions) is { } error)
				{
					errors.Add($"{error} {errorLocation}");
				}
				else
				{
					backpack.Sizes.Add(dimensions);
				}
			}

			if (HasKey("teleport"))
			{
				if (parseBool(backpackDict["teleport"], out bool teleport) is { } error)
				{
					errors.Add($"{error} for key teleport {errorLocation}");
				}
				else
				{
					backpack.MaySkipTeleport = teleport;
				}
			}

			if (HasKey("weight factor"))
			{
				if (backpackDict["weight factor"] is string factorString)
				{
					if (float.TryParse(factorString, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float weightFactor))
					{
						backpack.ItemWeightFactor = weightFactor;
					}
					else
					{
						errors.Add($"The weight factor must be a decimal number. Got unexpected '{factorString}' {errorLocation}.");
					}
				}
				else
				{
					errors.Add($"The weight factor must a decimal number. Got unexpected {backpackDict["weight factor"]?.GetType().ToString() ?? "null"} {errorLocation}");
				}
			}

			if (HasKey("unique"))
			{
				if (backpackDict["unique"] is string uniqueString)
				{
					if (uniqueMap.TryGetValue(uniqueString, out Unique unique))
					{
						backpack.UniqueStatus = unique;
					}
					else
					{
						errors.Add($"The weight factor must be one of {string.Join(", ", uniqueMap.Keys)}. Got unexpected '{uniqueString}' {errorLocation}.");
					}
				}
				else
				{
					errors.Add($"The uniqueness must be a string. Got unexpected {backpackDict["unique"]?.GetType().ToString() ?? "null"} {errorLocation}");
				}
			}

			if (HasKey("effect"))
			{
				if (backpackDict["effect"] is string effectString)
				{
					backpack.statusEffect = effectString;
				}
				else
				{
					errors.Add($"The effect must be a string. Got unexpected {backpackDict["effect"]?.GetType().ToString() ?? "null"} {errorLocation}");
				}
			}

			if (HasKey("description"))
			{
				if (backpackDict["description"] is string description)
				{
					backpack.Description = description;
				}
				else
				{
					errors.Add($"The description must be a string. Got unexpected {backpackDict["weight factor"]?.GetType().ToString() ?? "null"} {errorLocation}");
				}
			}

			if (HasKey("valid items"))
			{
				if (backpackDict["valid items"] is List<object?> prefabList)
				{
					foreach (object? prefabObj in prefabList)
					{
						if (prefabObj is string prefab)
						{
							backpack.AllowedItems.Add(prefab);
						}
						else
						{
							errors.Add($"Values in the valid items list must be prefab names. Got unexpected {prefabObj?.GetType().ToString() ?? "null"} {errorLocation}.");
						}
					}
				}
				else
				{
					errors.Add($"The valid items list must be a list of prefab names. Got unexpected {backpackDict["valid items"]?.GetType().ToString() ?? "null"} {errorLocation}");
				}
			}

			string? parseCost(KeyValuePair<string, object?> costsKv, out int count)
			{
				count = 0;
				if (costsKv.Value is string number)
				{
					if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out count))
					{
						return null;
					}
					return $"The amount of {costsKv.Key} must be a valid number. Got unexpected {number} {errorLocation}";
				}
				return $"Expecting a number while parsing the costs for '{costsKv.Key}'. Got unexpected {costsKv.Value?.GetType().ToString() ?? "null"}";
			}

			if (HasKey("appearance"))
			{
				if (backpackDict["appearance"] is Dictionary<object, object?> appearanceDict)
				{
					foreach (KeyValuePair<string, object?> kv in castDictToStringDict(appearanceDict))
					{
						if (kv.Value is string indicator)
						{
							if (BackpackParts.Contains(kv.Key))
							{
								if (string.Equals("hidden", indicator, StringComparison.InvariantCultureIgnoreCase))
								{
									backpack.appearance.Add(kv.Key, new Appearance { Visible = false });
								}
								else if (string.Equals("visible", indicator, StringComparison.InvariantCultureIgnoreCase))
								{
									backpack.appearance.Add(kv.Key, new Appearance { Visible = true });
								}
								else if (ColorUtility.TryParseHtmlString(indicator, out Color color) || ColorUtility.TryParseHtmlString("#" + indicator, out color))
								{
									backpack.appearance.Add(kv.Key, new Appearance { Color = color, Visible = true });
								}
								else
								{
									errors.Add($"Found invalid backpack part color '{indicator}' for its appearance {errorLocation} Valid backpack part colors are html colors and the values 'hidden' and 'visible'.");
								}
							}
							else
							{
								errors.Add($"Found invalid backpack part '{kv.Key}' for its appearance {errorLocation} Valid backpack parts are: {string.Join(", ", BackpackParts)}.");
							}
						}
						else
						{
							errors.Add($"Expecting a visibility or hex color code for the '{kv.Key}' backpack appearance. Got unexpected {kv.Value?.GetType().ToString() ?? "null"} {errorLocation}");
						}
					}
				}
				else
				{
					errors.Add($"Expecting a mapping of visibilities and colors to specific parts of the backpack for appearance. Got unexpected {backpackDict["appearance"]?.GetType().ToString() ?? "null"} {errorLocation}");
				}
			}

			if (HasKey("crafting"))
			{
				if (backpackDict["crafting"] is Dictionary<object, object?> craftingDictObj)
				{
					Dictionary<string, object?> craftingDict = castDictToStringDict(craftingDictObj);

					HashSet<string> knownCraftingKeys = new(StringComparer.InvariantCultureIgnoreCase);
					string craftingErrorLocation = $"in crafting section of backpack definition for backpack '{rootDictKv.Key}'.";

					bool HasCraftingKey(string key)
					{
						knownCraftingKeys.Add(key);
						return craftingDict.ContainsKey(key);
					}

					if (HasCraftingKey("costs"))
					{
						if (craftingDict["costs"] is Dictionary<object, object?> costsDict)
						{
							Dictionary<string, int> costs = new();
							foreach (KeyValuePair<string, object?> costsKv in castDictToStringDict(costsDict))
							{
								if (parseCost(costsKv, out int count) is { } error)
								{
									errors.Add($"{error} {errorLocation}");
								}
								else
								{
									costs.Add(costsKv.Key, count);
								}
							}
							backpack.Costs.Add(costs);
						}
						else
						{
							errors.Add($"The costs entry must be a mapping of prefab names to a number of items. Got unexpected {craftingDict["costs"]?.GetType().ToString() ?? "null"} {craftingErrorLocation}");
						}
					}

					if (HasCraftingKey("station"))
					{
						int level = 1;
						if (HasCraftingKey("level"))
						{
							if (craftingDict["level"] is string levelString)
							{
								if (!int.TryParse(levelString, NumberStyles.None, CultureInfo.InvariantCulture, out level))
								{
									level = 0;
									errors.Add($"The crafting station level must be a positive number. Got invalid '{levelString}' {craftingErrorLocation}");
								}
							}
							else
							{
								errors.Add($"The crafting station level must be a number. Got unexpected {craftingDict["level"]?.GetType().ToString() ?? "null"} {craftingErrorLocation}");
							}
						}

						if (craftingDict["station"] is string stationString)
						{
							if (((CraftingTable[])typeof(CraftingTable).GetEnumValues()).FirstOrDefault(c => c.ToString().Equals(stationString, StringComparison.InvariantCultureIgnoreCase)) is { } table && table != CraftingTable.Disabled)
							{
								backpack.CraftingStation = new CraftingStationConfig { Table = table, level = level };
							}
							else
							{
								backpack.CraftingStation = new CraftingStationConfig { Table = CraftingTable.Custom, level = level, custom = stationString };
							}
						}
						else
						{
							errors.Add($"The station entry must be the prefab name of a station. Got unexpected {craftingDict["station"]?.GetType().ToString() ?? "null"} {craftingErrorLocation}");
						}
					}

					errors.AddRange(from key in craftingDictObj.Keys where !knownCraftingKeys.Contains(key) select $"A backpack crafting definition may not contain a key '{key}'. Available keys are {string.Join(", ", knownCraftingKeys)} {craftingErrorLocation}");
				}
			}

			if (HasKey("upgrade"))
			{
				if (backpackDict["upgrade"] is Dictionary<object, object?> upgradesDict)
				{
					Dictionary<int, Vector2i> sizes = new();
					Dictionary<int, Dictionary<string, int>> allCosts = new();
					foreach (KeyValuePair<string, object?> upgradesKv in castDictToStringDict(upgradesDict))
					{
						if (int.TryParse(upgradesKv.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int upgradeLevel))
						{
							if (upgradesKv.Value is Dictionary<object, object?> upgradeDict)
							{
								Dictionary<string, int> costs = new();
								foreach (KeyValuePair<string, object?> upgradeKv in castDictToStringDict(upgradeDict))
								{
									if (upgradeKv.Key.Equals("size", StringComparison.InvariantCultureIgnoreCase))
									{
										if (backpack.Sizes.Count == 0)
										{
											errors.Add($"It's only allowed to either specify a size for all, basis ('size' key) and upgrade, or none (to use the default) {errorLocation}");
										}
										else if (parseSize(upgradeKv.Value, out Vector2i dimensions) is { } error)
										{
											errors.Add($"{error} in upgrade level {upgradeLevel} {errorLocation}");
										}
										else
										{
											sizes.Add(upgradeLevel, dimensions);
										}
									}
									else if (parseCost(upgradeKv, out int count) is { } error)
									{
										errors.Add($"{error} in upgrade level {upgradeLevel} {errorLocation}");
									}
									else
									{
										costs.Add(upgradeKv.Key, count);
									}
								}
								allCosts.Add(upgradeLevel, costs);
							}
							else if (upgradesKv.Value is null)
							{
								allCosts.Add(upgradeLevel, new Dictionary<string, int>());
							}
							else
							{
								errors.Add($"Each upgrades entry must be a mapping of upgrade level (starting at 1 for the first upgrade) to that upgrades configuration. Got unexpected {upgradesKv.Value.GetType()} in upgrade {upgradesKv.Key} {errorLocation}");
							}
						}
						else
						{
							errors.Add($"The keys in the upgrades entry must be an integer level starting at 1 for the first upgrade. Got unexpected {backpackDict["upgrade"]?.GetType().ToString() ?? "null"} in upgrade {upgradesKv.Key} {errorLocation}");
						}
					}

					for (int i = 1; i <= allCosts.Count; ++i)
					{
						if (allCosts.TryGetValue(i, out Dictionary<string, int> costs))
						{
							backpack.Costs.Add(costs);
						}
						else
						{
							errors.Add($"Every upgrade level starting from must be present, holes are not permitted. Missing upgrade level {i} {errorLocation}");
							break;
						}

						if (backpack.Sizes.Count > 0)
						{
							if (sizes.TryGetValue(i, out Vector2i size))
							{
								backpack.Sizes.Add(size);
							}
							else
							{
								backpack.Sizes.Add(backpack.Sizes.Last());
								errors.Add($"It's only allowed to either specify a size for all, basis ('size' key) and upgrade, or none (to use the default). Missing size for upgrade level {i} {errorLocation}");
							}
						}
					}
				}
				else
				{
					errors.Add($"The upgrades entry must be a mapping of upgrade level (starting at 1 for the first upgrade) to that upgrades configuration. Got unexpected {backpackDict["upgrade"]?.GetType().ToString() ?? "null"} {errorLocation}");
				}
			}

			customBackpacks.Add(rootDictKv.Key, backpack);

			errors.AddRange(from key in backpackDict.Keys where !knownKeys.Contains(key) select $"A backpack definition may not contain a key '{key}'. Available keys are {string.Join(", ", knownKeys)} {errorLocation}");
		}

		return configurationResult;
	}

	public class Loader : ConfigLoader.Loader
	{
		private readonly Dictionary<string, ParseResult> parsed = new();

		public List<string> ErrorCheck(object? yaml)
		{
			Parse(yaml, out List<string> errors);
			return errors;
		}

		public List<string> ProcessConfig(string key, object? yaml)
		{
			ParseResult result = Parse(yaml, out List<string> errors);

			parsed[key] = result;
			return errors;
		}

		public void Reset()
		{
			parsed.Clear();
		}

		private static readonly GameObject backpackPrefabs;
		private static readonly Dictionary<string, Item> loadedBackpacks = new();

		static Loader()
		{
			backpackPrefabs = new GameObject("Backpacks Custom Prefabs");
			backpackPrefabs.SetActive(false);
			Object.DontDestroyOnLoad(backpackPrefabs);
		}

		public void ApplyConfig()
		{
			Dictionary<string, List<string>> groups = parsed.Values.SelectMany(parse => parse.Groups).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.SelectMany(kv => kv.Value).ToList());

			HashSet<string> activeBackpacks = new();
			foreach (ParseResult parse in parsed.Values)
			{
				foreach (KeyValuePair<string, CustomBackpackConfig> kv in parse.CustomBackpacks)
				{
					activeBackpacks.Add(kv.Key);

					if (!loadedBackpacks.TryGetValue(kv.Key, out Item item))
					{
						if (ZNetScene.instance.GetPrefab(kv.Key))
						{
							Debug.LogError($"Could not add custom backpack {kv.Key} as an internal entity named such already exists.");
							continue;
						}

						item = loadedBackpacks[kv.Key] = new Item(Object.Instantiate(Backpacks.Backpack.Prefab, backpackPrefabs.transform))
						{
							Configurable = Configurability.Disabled,
							Prefab = { name = new string(kv.Key.Where(c => c is not ' ' and not '/' and not '(' and not ')').ToArray()) },
						};
						item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name = kv.Key;
						item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_description = "";
						_ = item.Name; // init name
					}
					item.Description.English(kv.Value.Description ?? "A random backpack.");

					foreach (SkinnedMeshRenderer renderer in item.Prefab.transform.Find("attach_skin/Mesh").GetComponentsInChildren<SkinnedMeshRenderer>(true))
					{
						SkinnedMeshRenderer defaultRenderer = Backpacks.Backpack.Prefab.transform.Find("attach_skin/Mesh/" + renderer.name).GetComponent<SkinnedMeshRenderer>();
						renderer.material.color = defaultRenderer.material.color;
						renderer.gameObject.SetActive(defaultRenderer.gameObject.activeSelf);

						if (kv.Value.appearance.TryGetValue(renderer.name, out Appearance appearance))
						{
							if (appearance.Color != Color.clear)
							{
								renderer.material.color = appearance.Color;
							}
							renderer.gameObject.SetActive(appearance.Visible);
						}

						SkinnedMeshRenderer mainRenderer = item.Prefab.transform.Find("Mesh/" + renderer.name).GetComponent<SkinnedMeshRenderer>();
						mainRenderer.material.color = renderer.material.color;
						mainRenderer.gameObject.SetActive(renderer.gameObject.activeSelf);
					}

					void ApplyAppearance(Transform attach_skin, VisEquipment? visEquipment = null)
					{
						foreach (SkinnedMeshRenderer renderer in attach_skin.Find("Mesh").GetComponentsInChildren<SkinnedMeshRenderer>(true))
						{
							SkinnedMeshRenderer prefabRenderer = item.Prefab.transform.Find("attach_skin/Mesh/" + renderer.name).GetComponent<SkinnedMeshRenderer>();
							renderer.material.color = prefabRenderer.material.color;
							renderer.gameObject.SetActive(prefabRenderer.gameObject.activeSelf);
							if (visEquipment is not null)
							{
								renderer.bones = visEquipment.m_bodyModel.bones;
								renderer.rootBone = visEquipment.m_bodyModel.rootBone;
							}
						}
					}

					foreach (Player player in Player.s_players)
					{
						if (Visual.visuals.TryGetValue(player.m_visEquipment, out Visual backpackVisual) && backpackVisual.currentBackpackItemHash == item.Prefab.name.GetStableHashCode())
						{
							foreach (GameObject backpackInstance in Visual.visuals[player.m_visEquipment].backpackItemInstances)
							{
								ApplyAppearance(backpackInstance.transform, player.m_visEquipment);
							}
						}
					}

					foreach (Object itemInstance in Resources.FindObjectsOfTypeAll(typeof(ItemDrop)))
					{
						if (Utils.GetPrefabName(((ItemDrop)itemInstance).gameObject) == item.Prefab.name)
						{
							ApplyAppearance(((ItemDrop)itemInstance).transform);
						}
					}

					List<string> allowedItems = new();
					foreach (string allowedItem in kv.Value.AllowedItems)
					{
						if (groups.TryGetValue(allowedItem, out List<string> groupItems) || ItemGroups.predefinedGroups.TryGetValue(allowedItem, out groupItems))
						{
							foreach (string groupItem in groupItems)
							{
								if (ObjectDB.instance.GetItemPrefab(groupItem) is { } prefab)
								{
									allowedItems.Add(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
								}
								else
								{
									Debug.LogWarning($"Could not find item {groupItem} while evaluating allowed items for custom backpack group {allowedItem}");
								}
							}
						}
						else if (ObjectDB.instance.GetItemPrefab(allowedItem) is { } prefab)
						{
							allowedItems.Add(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
						}
						else
						{
							Debug.LogWarning($"Could not find item {allowedItem} while evaluating allowed items for custom backpack {kv.Key}");
						}
					}

					string name = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
					if (kv.Value.statusEffect is null || ObjectDB.instance.GetStatusEffect(kv.Value.statusEffect.GetStableHashCode()) is not { } statusEffect)
					{
						statusEffect = null;
						if (kv.Value.statusEffect is not null)
						{
							Debug.LogWarning($"Could not find status effect {kv.Value.statusEffect} while evaluating status effect for custom backpack {kv.Key}");
						}
					}
					item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_equipStatusEffect = statusEffect;
					CustomBackpack.AllowedItems[name] = allowedItems;
					CustomBackpack.Sizes[name] = kv.Value.Sizes;
					CustomBackpack.ItemWeightFactor[name] = kv.Value.ItemWeightFactor;
					CustomBackpack.MaySkipTeleport[name] = kv.Value.MaySkipTeleport;
					CustomBackpack.UniqueStatus[name] = kv.Value.UniqueStatus;

					CustomBackpack backpack = item.Prefab.GetComponent<ItemDrop>().m_itemData.Data().GetOrCreate<CustomBackpack>();
					if (kv.Value.Sizes.Count > 0)
					{
						backpack.Inventory.m_width = kv.Value.Sizes[0].x;
						backpack.Inventory.m_height = kv.Value.Sizes[0].y;
					}

					item.Crafting.Stations.Clear();
					item.RequiredItems.Requirements.Clear();
					item.RequiredUpgradeItems.Requirements.Clear();

					if (kv.Value.Costs.Count > 0)
					{
						foreach (KeyValuePair<string, int> costKv in kv.Value.Costs[0])
						{
							item.RequiredItems.Add(costKv.Key, costKv.Value);
						}

						for (int i = 1; i < kv.Value.Costs.Count; ++i)
						{
							foreach (KeyValuePair<string, int> costKv in kv.Value.Costs[i])
							{
								item.RequiredUpgradeItems.Add(costKv.Key, costKv.Value, kv.Value.Costs.Count == 2 ? 0 : i + 1);
							}
						}

						item.Crafting.Stations.Add(kv.Value.CraftingStation);

						if (item.RecipeIsActive is null)
						{
							item.RecipeIsActive = (ConfigEntry<int>)FormatterServices.GetUninitializedObject(typeof(ConfigEntry<int>));
							AccessTools.Field(item.RecipeIsActive.GetType(), "<Description>k__BackingField").SetValue(item.RecipeIsActive, new ConfigDescription(""));
						}
						((ConfigEntry<int>)item.RecipeIsActive).Value = 0;
					}

					item.ReloadCraftingConfiguration();
					int maxQuality = Math.Max(CustomBackpack.Sizes[name].Count, kv.Value.Costs.Count);

					item.Snapshot(itemRotation: Quaternion.Euler(90, 90, 135));
					Sprite[] icons = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons;

					item.ApplyToAllInstances(item =>
					{
						item.m_shared.m_maxQuality = maxQuality;
						item.m_shared.m_icons = icons;
					});
				}
			}

			foreach (KeyValuePair<string, Item> kv in loadedBackpacks)
			{
				if (!activeBackpacks.Contains(kv.Key) && kv.Value.RecipeIsActive is ConfigEntry<int> cfg)
				{
					cfg.Value = 0;
				}
			}
		}

		public string FilePattern => "Backpacks*.yml";
		public string EditButtonName => Localization.instance.Localize("$bp_edit_socket_yaml_config");
		public CustomSyncedValue<List<string>> FileData => Backpacks.customBackpackDefinition;
		public bool Enabled => true;
	}
}
