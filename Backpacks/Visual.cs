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
	public static readonly Dictionary<VisEquipment, Visual> visuals = new();

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
			if (__instance is Player player && visuals[player.m_visEquipment].equippedBackpackItem == item)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
	private static class AddVisual
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(Player __instance)
		{
			visuals.Add(__instance.GetComponent<VisEquipment>(), new Visual(__instance.GetComponent<VisEquipment>()));
		}
	}

	[HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.OnEnable))]
	private static class AddVisualOnEnable
	{
		private static void Postfix(VisEquipment __instance)
		{
			if (!visuals.ContainsKey(__instance) && __instance.m_isPlayer)
			{
				visuals[__instance] = new Visual(__instance);
			}
		}
	}

	[HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.OnDisable))]
	private static class RemoveVisualOnDisable
	{
		private static void Postfix(VisEquipment __instance)
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
				Visual visual = visuals[player.m_visEquipment];
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
				player.UnequipItem(visuals[player.m_visEquipment].equippedBackpackItem, false);
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
				visuals[__instance].updateEquipmentVisuals();
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
				player.UnequipItem(visuals[player.m_visEquipment].equippedBackpackItem, triggerEquipmentEffects);
				visuals[player.m_visEquipment].equippedBackpackItem = item;
			}
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionEnumerable)
		{
			MethodInfo itemEquipped = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.IsItemEquiped));
			List<CodeInstruction> instructions = instructionEnumerable.ToList();
			int index = instructions.FindLastIndex(instruction => instruction.Calls(itemEquipped));
			CodeInstruction labelInstruction = instructions[index - 2];
			instructions.InsertRange(index - 2, new[]
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
			if (humanoid is Player player && visuals[player.m_visEquipment].equippedBackpackItem == item)
			{
				visuals[player.m_visEquipment].equippedBackpackItem = null;
			}
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionEnumerable)
		{
			MethodInfo setupEquipment = AccessTools.DeclaredMethod(typeof(Humanoid), nameof(Humanoid.SetupEquipment));
			List<CodeInstruction> instructions = instructionEnumerable.ToList();
			int index = instructions.FindIndex(instruction => instruction.Calls(setupEquipment));
			instructions.InsertRange(index - 1, new[]
			{
				new CodeInstruction(OpCodes.Ldarg_0),
				new CodeInstruction(OpCodes.Ldarg_1),
				new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(UnequipItem), nameof(Unequip))),
			});
			return instructions;
		}
	}

	private readonly VisEquipment visEquipment;
	private ItemDrop.ItemData? equippedBackpackItem;

	private string backpackItem = "";
	public List<GameObject> backpackItemInstances = new();
	public int currentBackpackItemHash;

	private Visual(VisEquipment visEquipment)
	{
		this.visEquipment = visEquipment;
	}

	private void updateEquipmentVisuals()
	{
		int hash;
		if (visEquipment.m_nview.GetZDO() is { } zdo)
		{
			hash = zdo.GetInt("BackpackItem");
		}
		else
		{
			hash = string.IsNullOrEmpty(backpackItem) ? 0 : backpackItem.GetStableHashCode();
		}

		if (setBackpackEquipped(hash))
		{
			visEquipment.UpdateLodgroup();
		}
	}

	private bool setBackpackEquipped(int hash)
	{
		if (currentBackpackItemHash == hash)
		{
			return false;
		}
		foreach (GameObject backpackItemInstance in backpackItemInstances)
		{
			Object.Destroy(backpackItemInstance);
		}
		backpackItemInstances.Clear();
		currentBackpackItemHash = hash;
		if (hash != 0)
		{
			backpackItemInstances = visEquipment.AttachArmor(hash);
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
		if (visEquipment.m_nview.GetZDO() is { } zdo && visEquipment.m_nview.IsOwner())
		{
			zdo.Set("BackpackItem", string.IsNullOrEmpty(name) ? 0 : name.GetStableHashCode());
		}
	}
}
