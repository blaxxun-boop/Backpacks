using System.Collections.Generic;
using System.Linq;
using ItemDataManager;
using JetBrains.Annotations;

namespace Backpacks;

[PublicAPI]
public static class API
{
	public static int CountItemsInBackpacks(Inventory inventory, string name, bool onlyRemoveable = true)
	{
		int count = 0;
#if ! API
		foreach (ItemDrop.ItemData item in inventory.m_inventory)
		{
			if (item.Data().Get<ItemContainer>() is { } backpack)
			{
				foreach (ItemDrop.ItemData backpackItem in backpack.Inventory.m_inventory)
				{
					if (backpackItem.m_shared.m_name == name && (!onlyRemoveable || backpack.CanRemoveItem(backpackItem)))
					{
						count += backpackItem.m_stack;
					}
				}
			}
		}
#endif
		return count;
	}

	public static bool DeleteItemsFromBackpacks(Inventory inventory, string name, int count = 1)
	{
#if API
		return false;
#else
		Dictionary<ItemDrop.ItemData, ItemContainer> items = new();
		foreach (ItemDrop.ItemData item in inventory.m_inventory)
		{
			if (item.Data().Get<ItemContainer>() is { } backpack)
			{
				foreach (ItemDrop.ItemData backpackItem in backpack.Inventory.m_inventory)
				{
					if (backpackItem.m_shared.m_name == name && backpack.CanRemoveItem(backpackItem))
					{
						items.Add(backpackItem, backpack);
					}
				}
			}
		}

		if (items.Sum(i => i.Key.m_stack) < count)
		{
			return false;
		}

		HashSet<ItemContainer> updatedContainers = new();
		try
		{
			foreach (KeyValuePair<ItemDrop.ItemData, ItemContainer> kv in items)
			{
				updatedContainers.Add(kv.Value);
				if (kv.Key.m_stack <= count)
				{
					if (kv.Value.RemoveItem(kv.Key))
					{
						kv.Value.Inventory.RemoveItem(kv.Key);
					}

					count -= kv.Key.m_stack;
					if (count == 0)
					{
						return true;
					}
				}
				else
				{
					kv.Key.m_stack -= count;
					return true;
				}
			}
		}
		finally
		{
			foreach (ItemContainer container in updatedContainers)
			{
				container.Save();
			}
		}

		return false;
#endif
	}
}
