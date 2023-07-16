using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ItemDataManager;
using UnityEngine;

namespace Backpacks;

public class Visual
{
	private static Dictionary<Player, Visual> visuals = new();

	[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.IsEquipable))]
	private static class IsEquipable
	{
		private static void Postfix(ItemDrop.ItemData __instance, ref bool __result)
		{
			if (__instance.Data().Get<ItemContainer>()?.IsEquipable() == true)
			{
				__result = true;
			}
		}
	}
	
	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsItemEquiped))]
	private static class IsItemEquiped
	{
		private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
		{
			if (__instance is Player player && visuals[player].equippedBackpackItem == item)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
	private static class AddVisual
	{
		[HarmonyPriority(Priority.First)]
		private static void Postfix(Player __instance)
		{
			visuals.Add(__instance, new Visual(__instance));
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.OnDestroy))]
	private static class RemoveVisual
	{
		private static void Postfix(Player __instance)
		{
			visuals.Remove(__instance);
		}
	}

	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.SetupVisEquipment))]
	private static class SetupVisEquipment
	{
		private static void Prefix(Humanoid __instance)
		{
			if (__instance is Player player)
			{
				Visual visual = visuals[player];
				visual.setBackpackItem(visual.equippedBackpackItem is null ? "" : visual.equippedBackpackItem.m_dropPrefab.name);
			}
		}
	}

	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipAllItems))]
	private static class UnequipAllItems
	{
		private static void Prefix(Humanoid __instance)
		{
			if (__instance is Player player)
			{
				player.UnequipItem(visuals[player].equippedBackpackItem, false);
			}
		}
	}

	[HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.UpdateEquipmentVisuals))]
	private static class UpdateEquipmentVisuals
	{
		private static void Postfix(VisEquipment __instance)
		{
			if (__instance.m_isPlayer)
			{
				visuals[__instance.GetComponent<Player>()].updateEquipmentVisuals();
			}
		}
	}

	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
	private static class EquipItem
	{
		private static void Equip(Humanoid humanoid, ItemDrop.ItemData item, bool triggerEquipmentEffects)
		{
			if (humanoid is Player player && item.Data().Get<ItemContainer>()?.IsEquipable() == true)
			{
				player.UnequipItem(visuals[player].equippedBackpackItem, triggerEquipmentEffects);
				visuals[player].equippedBackpackItem = item;
			}
		}
		
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionEnumerable)
		{
			MethodInfo itemEquipped = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.IsItemEquiped));
			List<CodeInstruction> instructions = instructionEnumerable.ToList();
			int index = instructions.FindLastIndex(instruction => instruction.Calls(itemEquipped));
			CodeInstruction labelInstruction = instructions[index - 2];
			instructions.InsertRange(index - 2, new []
			{
				new CodeInstruction(OpCodes.Ldarg_0) { labels = labelInstruction.labels },
				new CodeInstruction(OpCodes.Ldarg_1),
				new CodeInstruction(OpCodes.Ldarg_2),
				new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(EquipItem), nameof(Equip))),
			});
			labelInstruction.labels = new List<Label>();
			
			return instructions;
		}
	}

	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
	private static class UnequipItem
	{
		private static void Unequip(Humanoid humanoid, ItemDrop.ItemData item)
		{
			if (humanoid is Player player && visuals[player].equippedBackpackItem == item)
			{
				visuals[player].equippedBackpackItem = null;
			}
		}
		
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionEnumerable)
		{
			MethodInfo setupEquipment = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.SetupEquipment));
			List<CodeInstruction> instructions = instructionEnumerable.ToList();
			int index = instructions.FindIndex(instruction => instruction.Calls(setupEquipment));
			instructions.InsertRange(index - 1, new []
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldarg_1),
				new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(UnequipItem), nameof(Unequip))),
			});
			return instructions;
		}
	}

	private readonly Player player;
	private ItemDrop.ItemData? equippedBackpackItem;
	
	private string backpackItem = "";
	private readonly Transform backBackpack;
	private GameObject? backpackItemInstance;
	private int currentBackpackItemHash;

	private Visual(Player player)
	{
		this.player = player;
		backBackpack = Object.Instantiate(player.m_visEquipment.m_backTool, player.m_visEquipment.m_backTool.parent);
		backBackpack.name = "backpack attach";
	}

	private void updateEquipmentVisuals()
	{
		int hash;
		if (player.m_nview.GetZDO() is { } zdo)
		{
			hash = zdo.GetInt("BackpackItem");
		}
		else
		{
			hash = backpackItem.GetStableHashCode();
		}

		if (setBackpackEquipped(hash))
		{
			player.m_visEquipment.UpdateLodgroup();
		}
	}

	private bool setBackpackEquipped(int hash)
	{
		if (currentBackpackItemHash == hash)
		{
			return false;
		}
		if (backpackItemInstance)
		{
			Object.Destroy(backpackItemInstance);
			backpackItemInstance = null;
		}
		currentBackpackItemHash = hash;
		if (hash != 0)
		{
			backpackItemInstance = player.m_visEquipment.AttachItem(hash, 0, backBackpack);
		}
		return true;
	}
	
	private void setBackpackItem(string name)
	{
		if (backpackItem == name)
		{
			return;
		}
		backpackItem = name;
		if (player.m_nview.GetZDO() is { } zdo && player.m_nview.IsOwner())
		{
			zdo.Set("BackpackItem", string.IsNullOrEmpty(name) ? 0 : name.GetStableHashCode());
		}
	}
}
