using System;
using System.Reflection;
using HarmonyLib;
using ItemDataManager;
using JetBrains.Annotations;

namespace Backpacks;

[PublicAPI]
public class ItemContainer : ItemData
{
	public readonly Inventory Inventory;
	private bool hasClonedSharedData = false;

	private static readonly MethodInfo MemberwiseCloneMethod = AccessTools.DeclaredMethod(typeof(object), "MemberwiseClone");

	public ItemContainer()
	{
		// ReSharper disable once VirtualMemberCallInConstructor
		Vector2i dimensions = GetDefaultContainerSize();
		Inventory = new Inventory("Items", Player.m_localPlayer?.GetInventory().m_bkg, dimensions.x, dimensions.y);
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
			if (!IgnoresWeight())
			{
				Player.m_localPlayer?.GetInventory().UpdateTotalWeight();
			}
		};
	}

	public virtual Vector2i GetDefaultContainerSize()
	{
		return new Vector2i(Backpacks.backpackColumns.Value, Backpacks.backpackRows.Value);
	}

	public virtual bool ShowTakeAllButton() => true;
	public virtual bool AllowOpeningByKeypress() => true;

	public virtual string GetContainerTitle() => Localization.instance.Localize(Item.m_shared.m_name);
	public virtual string GetPressOpenText() => "Press [" + Localization.instance.Localize("<color=yellow><b>$KEY_Use</b></color>") + "] to open";

	public virtual bool IgnoresTeleportable()
	{
		return Backpacks.preventTeleportation.Value == Backpacks.Toggle.Off;
	}

	public virtual bool IgnoresWeight()
	{
		return Backpacks.backpackWeight.Value == Backpacks.Toggle.Off;
	}

	public virtual bool CanAddItem(ItemDrop.ItemData item)
	{
		return Backpacks.backpackCeption.Value == Backpacks.Toggle.On ? item != Item : item.m_shared.m_name != "$item_explorer";
	}

	public virtual bool CanRemoveItem(ItemDrop.ItemData item) => true;

	public virtual bool RemoveItem(ItemDrop.ItemData item) => true;

	public virtual bool AllowStacking() => true;

	public virtual bool Open() => true;

	public virtual void Close() { }

	public override void Save()
	{
		ZPackage pkg = new();
		Inventory.Save(pkg);
		Value = $"{Inventory.m_width};{Inventory.m_height};{Convert.ToBase64String(pkg.GetArray())}";
	}

	public override void Load()
	{
		string[] info = Value.Split(';');
		if (info.Length > 2 && int.TryParse(info[0], out int width) && int.TryParse(info[1], out int height))
		{
			Inventory.m_inventory.Clear();
			Inventory.m_width = width;
			Inventory.m_height = height;
			Inventory.Load(new ZPackage(Convert.FromBase64String(info[2])));
		}
	}
}
