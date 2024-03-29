﻿#if ! API
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
#endif
using ItemDataManager;
using JetBrains.Annotations;

namespace Backpacks;

public enum Unique
{
	None = 0,
	Global = 1,
	Restricted = 2,
	Type = 3,
	Bypass = 4,
}

[PublicAPI]
public class ItemContainer : ItemData
{
	public readonly Inventory Inventory;
#if ! API
	private bool hasClonedSharedData = false;

	private static readonly MethodInfo MemberwiseCloneMethod = AccessTools.DeclaredMethod(typeof(object), "MemberwiseClone");
#endif

	public ItemContainer()
	{
		// ReSharper disable once VirtualMemberCallInConstructor
		Vector2i dimensions = GetDefaultContainerSize();
		Inventory = new Inventory("Items", Player.m_localPlayer?.GetInventory().m_bkg, dimensions.x, dimensions.y);
#if ! API
		Inventory.m_onChanged += () =>
		{
			if (!IgnoresTeleportable())
			{
				if (!hasClonedSharedData)
				{
					Item.m_shared = (ItemDrop.ItemData.SharedData)MemberwiseCloneMethod.Invoke(Item.m_shared, Array.Empty<object>());
					hasClonedSharedData = true;
				}

				if (ZoneSystem.instance)
				{
					Item.m_shared.m_teleportable = Inventory.IsTeleportable();
				}
			}
			Player.m_localPlayer?.GetInventory().UpdateTotalWeight();
		};
#endif
	}

	public virtual Vector2i GetDefaultContainerSize()
	{
#if API
		return new Vector2i();
#else
		return new Vector2i(Backpacks.backpackColumnsByLevel[Math.Min(Backpacks.backpackColumnsByLevel.Count, Item.m_quality) - 1], Backpacks.backpackRowsByLevel[Math.Min(Backpacks.backpackColumnsByLevel.Count, Item.m_quality) - 1]);
#endif
	}

#if ! API
	public override void Upgraded()
	{
		Resize(GetDefaultContainerSize());
	}
#endif

	public bool Resize(Vector2i dimensions)
	{
#if API
		return false;
#else
		int oldSize = Inventory.m_width * Inventory.m_height;
		if (oldSize - Inventory.GetEmptySlots() > dimensions.x * dimensions.y)
		{
			return false;
		}

		IEnumerator<Vector2i> enumerate()
		{
			int y = 0;
			while (true)
			{
				for (int x = 0; x < dimensions.x; ++x)
				{
					if (Inventory.GetItemAt(x, y) is null)
					{
						yield return new Vector2i(x, y);
					}
				}
				++y;
			}
			// ReSharper disable once IteratorNeverReturns
		}
		IEnumerator<Vector2i> freePositions = enumerate();

		foreach (ItemDrop.ItemData item in Inventory.m_inventory)
		{
			if (item.m_gridPos.x >= dimensions.x || item.m_gridPos.y >= dimensions.y)
			{
				item.m_gridPos = freePositions.Current;
				freePositions.MoveNext();
			}
		}

		Inventory.m_width = dimensions.x;
		Inventory.m_height = dimensions.y;
		Save();

		return true;
#endif
	}

	public virtual bool IsEquipable() => true;
	
	public virtual Unique Uniqueness()
	{
#if API
		return Unique.None;
#else
		return typeof(ItemContainer) == GetType() ? Backpacks.uniqueBackpack.Value : Unique.Bypass;
#endif
	}

	public virtual bool ShowTakeAllButton() => true;
	public virtual bool AllowOpeningByKeypress() => true;

	public virtual string GetContainerTitle() => Localization.instance.Localize(Item.m_shared.m_name);
	public virtual string GetPressOpenText() => "Press [" + Localization.instance.Localize("<color=yellow><b>$KEY_Use</b></color>") + "] to open";

	public virtual bool IgnoresTeleportable()
	{
#if API
		return false;
#else
		return Backpacks.preventTeleportation.Value == Backpacks.Toggle.Off;
#endif
	}

	public virtual float WeightFactor()
	{
#if API
		return 1f;
#else
		return Backpacks.backpackWeightFactor.Value / 100f;
#endif
	}

	public virtual bool CanAddItem(ItemDrop.ItemData item)
	{
#if API
		return false;
#else
		return Backpacks.backpackCeption.Value switch
		{
			Backpacks.BackpackCeption.Allowed => item != Item,
			Backpacks.BackpackCeption.NotAllowedSameBackpack => item.m_shared.m_name != Item.m_shared.m_name,
			Backpacks.BackpackCeption.NotAllowedBackpacks => item.Data().Get<ItemContainer>() is not {} container || (container is not CustomBackpack && container.GetType() != typeof(ItemContainer)),
			Backpacks.BackpackCeption.NotAllowed => item.Data().Get<ItemContainer>() is null,
			_ => throw new ArgumentOutOfRangeException(),
		};
#endif
	}

	public virtual bool CanAddItemManually(ItemDrop.ItemData item) => CanAddItem(item);

	public virtual bool CanRemoveItem(ItemDrop.ItemData item) => true;

	public virtual bool RemoveItem(ItemDrop.ItemData item) => true;

	public virtual bool MayAutoPickup(ItemDrop.ItemData item)
	{
#if API
		return false;
#else
		return Backpacks.autoFillBackpacks.Value == Backpacks.Toggle.On;
#endif
	}

	public virtual bool AllowStacking() => true;

	public virtual bool CanBePutInContainer()
	{
#if API
		return false;
#else
		return Backpacks.backpackChests.Value == Backpacks.Toggle.On || Inventory.m_inventory.Count == 0;
#endif
	}

	public virtual bool Open() => true;

	public virtual void Close() { }

	public override void Save()
	{
#if ! API
		ZPackage pkg = new();
		Inventory.Save(pkg);
		Value = $"{Inventory.m_width};{Inventory.m_height};{Convert.ToBase64String(pkg.GetArray())}";
#endif
	}

	public override void Load()
	{
#if ! API
		string[] info = Value.Split(';');
		if (info.Length > 2 && int.TryParse(info[0], out int width) && int.TryParse(info[1], out int height))
		{
			Inventory.m_inventory.Clear();
			Inventory.m_width = width;
			Inventory.m_height = height;
			Inventory.Load(new ZPackage(Convert.FromBase64String(info[2])));
		}
#endif
	}
}
