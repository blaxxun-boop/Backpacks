#if ! API
using System;
using System.Reflection;
using HarmonyLib;
#endif
using ItemDataManager;
using JetBrains.Annotations;

namespace Backpacks;

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

				Item.m_shared.m_teleportable = Inventory.IsTeleportable();
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
		return new Vector2i(Backpacks.backpackColumns.Value, Backpacks.backpackRows.Value);
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
		return Backpacks.backpackCeption.Value == Backpacks.Toggle.On ? item != Item : item.m_shared.m_name != "$item_explorer";
#endif
	}

	public virtual bool CanRemoveItem(ItemDrop.ItemData item) => true;

	public virtual bool RemoveItem(ItemDrop.ItemData item) => true;

	public virtual bool AllowStacking() => true;

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
