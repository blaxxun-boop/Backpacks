using System;
using System.Collections.Generic;

namespace Backpacks;

public class CustomBackpack : ItemContainer
{
	public static readonly Dictionary<string, List<Vector2i>> Sizes = new();
	public static readonly Dictionary<string, List<string>> AllowedItems = new();
	public static readonly Dictionary<string, bool?> MaySkipTeleport = new();
	public static readonly Dictionary<string, float?> ItemWeightFactor = new();
	public static readonly Dictionary<string, Unique?> UniqueStatus = new();

	public override Vector2i GetDefaultContainerSize() => Sizes[Item.m_shared.m_name].Count > 0 ? Sizes[Item.m_shared.m_name][0] : base.GetDefaultContainerSize();

	public override bool IgnoresTeleportable() => MaySkipTeleport[Item.m_shared.m_name] ?? base.IgnoresTeleportable();
	public override float WeightFactor() => ItemWeightFactor[Item.m_shared.m_name] ?? base.WeightFactor();
	public override bool CanAddItem(ItemDrop.ItemData item) => AllowedItems[Item.m_shared.m_name].Count != 0 ? AllowedItems[Item.m_shared.m_name].Contains(item.m_shared.m_name) && Item != item : base.CanAddItem(item);
	public override Unique Uniqueness() => UniqueStatus[Item.m_shared.m_name] ?? base.Uniqueness();

	public override void Upgraded()
	{
		if (Sizes[Item.m_shared.m_name].Count > 0)
		{
			Resize(Sizes[Item.m_shared.m_name][Math.Min(Sizes[Item.m_shared.m_name].Count, Item.m_quality) - 1]);
		}
		else
		{
			base.Upgraded();
		}
	}
}
