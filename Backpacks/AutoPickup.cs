using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ItemDataManager;
using UnityEngine;

namespace Backpacks;

public static class AutoPickup
{
	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
	private static class AddItemToBackpack
	{
		[HarmonyPriority(Priority.LowerThanNormal)]
		private static bool Prefix(Humanoid __instance, GameObject go, bool autoPickupDelay, bool __runOriginal, ref bool __result)
		{
			if (Backpacks.autoFillBackpacks.Value == Backpacks.Toggle.Off || !__runOriginal || __instance is not Player player || go.GetComponent<ItemDrop>() is not { } itemDrop || player.IsTeleporting() || !itemDrop.CanPickup(autoPickupDelay) || itemDrop.m_nview.GetZDO() is null)
			{
				return true;
			}

			string itemName = itemDrop.m_itemData.m_shared.m_name;
			
			CheckAutoPickupActive.PickingUp = false;
			bool playerInventoryHasSpace = player.GetInventory().CanAddItem(itemDrop.m_itemData);

			// Short-circuit: stack into inventory if already in inventory
			if (player.GetInventory().ContainsItemByName(itemName) && playerInventoryHasSpace)
			{
				return true;
			}

			int originalAmount = itemDrop.m_itemData.m_stack;
			itemDrop.m_itemData.m_dropPrefab ??= ObjectDB.instance.GetItemPrefab(Utils.GetPrefabName(itemDrop.gameObject));

			IEnumerable<KeyValuePair<ItemContainer, long>> containers = player.GetInventory().m_inventory.Select(i => i.Data().Get<ItemContainer>()).Where(i => i is not null).Select(i =>
			{
				long pos = 1 + i!.Item.m_gridPos.x + i.Item.m_gridPos.y * 100;

				if (i.Inventory.ContainsItemByName(itemName))
				{
					pos += 10000;
				}

				if (i is not CustomBackpack && i.GetType() != typeof(ItemContainer))
				{
					pos *= 400000000;
				}
				else if (i is CustomBackpack && CustomBackpack.AllowedItems.TryGetValue(i.Item.m_shared.m_name, out List<string> allowedItems) && allowedItems.Count > 0)
				{
					pos *= 20000;
				}

				return new KeyValuePair<ItemContainer, long>(i, pos);
			}).OrderByDescending(kv => kv.Value);
			
			foreach (KeyValuePair<ItemContainer, long> kv in containers)
			{
				ItemContainer container = kv.Key;
				// prefer inventory instead of unrestricted backpacks
				if (kv.Value < 10000 && playerInventoryHasSpace)
				{
					return true;
				}
				
				if (container.Inventory.CanAddItem(itemDrop.m_itemData) && container.CanAddItem(itemDrop.m_itemData) && container.MayAutoPickup(itemDrop.m_itemData))
				{
					container.Inventory.AddItem(itemDrop.m_itemData);
					container.Save();
					
					ZNetScene.instance.Destroy(go);
            		player.m_pickupEffects.Create(player.transform.position, Quaternion.identity);
            		player.ShowPickupMessage(itemDrop.m_itemData, originalAmount);
		            __result = true;
            		return false;
				}
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.AutoPickup))]
	private static class CheckAutoPickupActive
	{
		public static bool PickingUp = false;
		private static void Prefix() => PickingUp = true;
		private static void Finalizer() => PickingUp = false;
	}

	[HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
	private static class AutoPickupItemsWithFullInventory
	{
		private static void Postfix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
		{
			if (!__result && CheckAutoPickupActive.PickingUp && Backpacks.autoFillBackpacks.Value == Backpacks.Toggle.On)
			{
				foreach (ItemDrop.ItemData inventoryItem in __instance.m_inventory)
				{
					if (inventoryItem.Data().Get<ItemContainer>() is { } container && container.Inventory.CanAddItem(item) && container.CanAddItem(item) && container.MayAutoPickup(item))
					{
						__result = true;
					}
				}
			}
		}
	}
}
