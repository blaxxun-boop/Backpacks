using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Backpacks;

public static class ItemGroups
{
	public static readonly Dictionary<string, List<string>> predefinedGroups = new();

	private static IEnumerable<string> itemGroups(ItemDrop.ItemData.SharedData itemData)
	{
		if (itemData is { m_food: > 0, m_foodStamina: > 0 })
		{
			yield return "food";
		}

		if (itemData is { m_food: > 0, m_foodStamina: 0 })
		{
			yield return "potion";
		}

		switch (itemData.m_itemType)
		{
			case ItemDrop.ItemData.ItemType.Ammo:
				switch (itemData.m_ammoType)
				{
					case "$ammo_bolts":
						yield return "bolts";
						break;
					case "$ammo_arrows":
						yield return "arrows";
						break;
				}
				yield return "ammo";
				break;
			case ItemDrop.ItemData.ItemType.OneHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeapon or ItemDrop.ItemData.ItemType.Bow:
				yield return "equipment";
				bool isWeapon = true;
				switch (itemData.m_skillType)
				{
					case Skills.SkillType.Swords:
						yield return "swords";
						break;
					case Skills.SkillType.Bows:
						yield return "bows";
						break;
					case Skills.SkillType.Crossbows:
						yield return "crossbows";
						break;
					case Skills.SkillType.Axes:
						yield return "axes";
						break;
					case Skills.SkillType.Clubs:
						yield return "clubs";
						break;
					case Skills.SkillType.Knives:
						yield return "knives";
						break;
					case Skills.SkillType.Pickaxes:
						yield return "pickaxes";
						break;
					case Skills.SkillType.Polearms:
						yield return "polearms";
						break;
					case Skills.SkillType.Spears:
						yield return "spears";
						break;
					default:
						isWeapon = false;
						break;
				}
				if (isWeapon)
				{
					yield return "weapon";
				}
				break;
			case ItemDrop.ItemData.ItemType.Torch:
				yield return "equipment";
				break;
			case ItemDrop.ItemData.ItemType.Trophy:
				string[] bossTrophies = { "eikthyr", "elder", "bonemass", "dragonqueen", "goblinking", "SeekerQueen" };
				yield return bossTrophies.Any(itemData.m_name.EndsWith) ? "boss trophy" : "trophy";
				break;
			case ItemDrop.ItemData.ItemType.Material:
				if (ObjectDB.instance.GetItemPrefab("Cultivator").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces.FirstOrDefault(p =>
				    {
					    Piece.Requirement[] requirements = p.GetComponent<Piece>().m_resources;
					    return requirements.Length == 1 && requirements[0].m_resItem.m_itemData.m_shared.m_name == itemData.m_name;
				    }) is { } piece)
				{
					yield return piece.GetComponent<Plant>()?.m_grownPrefabs[0].GetComponent<Pickable>()?.m_amount > 1 ? "crop" : "seed";
				}
				if (ZNetScene.instance.GetPrefab("smelter").GetComponent<Smelter>().m_conversion.Any(c => c.m_from.m_itemData.m_shared.m_name == itemData.m_name))
				{
					yield return "ore";
				}
				if (ZNetScene.instance.GetPrefab("smelter").GetComponent<Smelter>().m_conversion.Any(c => c.m_to.m_itemData.m_shared.m_name == itemData.m_name))
				{
					yield return "metal";
				}
				if (ZNetScene.instance.GetPrefab("blastfurnace").GetComponent<Smelter>().m_conversion.Any(c => c.m_from.m_itemData.m_shared.m_name == itemData.m_name))
				{
					yield return "ore";
				}
				if (ZNetScene.instance.GetPrefab("blastfurnace").GetComponent<Smelter>().m_conversion.Any(c => c.m_to.m_itemData.m_shared.m_name == itemData.m_name))
				{
					yield return "metal";
				}
				if (ZNetScene.instance.GetPrefab("charcoal_kiln").GetComponent<Smelter>().m_conversion.Any(c => c.m_from.m_itemData.m_shared.m_name == itemData.m_name))
				{
					yield return "woods";
				}
				break;
			case ItemDrop.ItemData.ItemType.Shield when itemData.m_timedBlockBonus > 0:
				yield return "equipment";
				yield return "shield";
				yield return "round shield";
				break;
			case ItemDrop.ItemData.ItemType.Shield when itemData.m_timedBlockBonus == 0:
				yield return "equipment";
				yield return "shield";
				yield return "tower shield";
				break;
			case ItemDrop.ItemData.ItemType.Chest:
				yield return "equipment";
				yield return "armor";
				yield return "chest";
				break;
			case ItemDrop.ItemData.ItemType.Shoulder:
				yield return "equipment";
				yield return "armor";
				yield return "cape";
				break;
			case ItemDrop.ItemData.ItemType.Helmet:
				yield return "equipment";
				yield return "armor";
				yield return "helmet";
				break;
			case ItemDrop.ItemData.ItemType.Legs:
				yield return "equipment";
				yield return "armor";
				yield return "pants";
				break;
			case ItemDrop.ItemData.ItemType.Tool:
				yield return "equipment";
				yield return "tool";
				break;
			case ItemDrop.ItemData.ItemType.Utility:
				yield return "equipment";
				yield return "special";
				break;
		}

		if (itemData.m_value > 0)
		{
			yield return "valuable";
		}

		if (itemData.m_maxStackSize > 1)
		{
			yield return "stackable";
		}

		if (itemData.m_name == "$item_elderbark")
		{
			yield return "woods";
		}
	}

	[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
	public class LoadPredefinedGroups
	{
		[HarmonyPriority(Priority.Last)]
		private static void Postfix()
		{
			if (predefinedGroups.Count > 0 || !ZNetScene.instance)
			{
				return;
			}

			foreach (GameObject item in ObjectDB.instance.m_items)
			{
				if (item.GetComponent<ItemDrop>() is { } itemDrop)
				{
					foreach (string group in itemGroups(itemDrop.m_itemData.m_shared))
					{
						if (!predefinedGroups.TryGetValue(group, out List<string> items))
						{
							items = predefinedGroups[group] = new List<string>();
						}
						items.Add(item.name);
					}
				}
			}
		}
	}
}
