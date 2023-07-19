using System;
using System.Collections.Generic;

namespace Backpacks;

public class CustomBackpack : ItemContainer
{
	public static Dictionary<string, List<Vector2i>> Sizes = new();
	public static Dictionary<string, List<string>> AllowedItems = new();
	public static Dictionary<string, bool?> MaySkipTeleport = new();
	public static Dictionary<string, float?> ItemWeightFactor = new();

	public override Vector2i GetDefaultContainerSize() => Sizes[Item.m_shared.m_name].Count > 0 ? Sizes[Item.m_shared.m_name][0] : base.GetDefaultContainerSize();

	public override bool IgnoresTeleportable() => MaySkipTeleport[Item.m_shared.m_name] ?? base.IgnoresTeleportable();
	public override float WeightFactor() => ItemWeightFactor[Item.m_shared.m_name] ?? base.WeightFactor();
	public override bool CanAddItem(ItemDrop.ItemData item) => AllowedItems[Item.m_shared.m_name].Contains(item.m_shared.m_name) && base.CanAddItem(item);

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
