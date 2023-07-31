using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ItemDataManager;
using UnityEngine;
using UnityEngine.UI;

namespace Backpacks;

internal static class CustomContainer
{
	private static ItemContainer? OpenContainer;

	private static void SaveItemContainer()
	{
		OpenContainer!.Save();
	}

	[HarmonyPatch]
	public class AddFakeItemContainer
	{
		private static Inventory? GetOpenInventory() => OpenContainer?.Inventory;
		private static bool IsOpenInventory() => OpenContainer is not null;

		// ReSharper disable once UnusedParameter.Local
		private static Inventory PopSecondValue(object _, Inventory inventory) => inventory;

		private static IEnumerable<MethodInfo> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.OnTakeAll)),
			AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem)),
			AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.IsContainerOpen)),
			AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.UpdateContainerWeight)),
		};

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionsList, ILGenerator ilg)
		{
			MethodInfo containerInventory = AccessTools.DeclaredMethod(typeof(Container), nameof(Container.GetInventory));
			FieldInfo containerField = AccessTools.DeclaredField(typeof(InventoryGui), nameof(InventoryGui.m_currentContainer));
			MethodInfo objectInequality = AccessTools.DeclaredMethod(typeof(Object), "op_Inequality");
			MethodInfo objectEquality = AccessTools.DeclaredMethod(typeof(Object), "op_Equality");
			MethodInfo objectImplicit = AccessTools.DeclaredMethod(typeof(Object), "op_Implicit");
			List<CodeInstruction> instructions = instructionsList.ToList();
			for (int i = 0; i < instructions.Count; ++i)
			{
				CodeInstruction instruction = instructions[i];
				if (instruction.opcode == OpCodes.Callvirt && instruction.OperandIs(containerInventory))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(AddFakeItemContainer), nameof(GetOpenInventory)));
					yield return new CodeInstruction(OpCodes.Dup);
					Label onWeaponInv = ilg.DefineLabel();
					yield return new CodeInstruction(OpCodes.Brtrue, onWeaponInv);
					yield return new CodeInstruction(OpCodes.Pop);
					yield return instruction;
					Label onInventoryFetch = ilg.DefineLabel();
					yield return new CodeInstruction(OpCodes.Br, onInventoryFetch);
					CodeInstruction pop = new(OpCodes.Call, AccessTools.DeclaredMethod(typeof(AddFakeItemContainer), nameof(PopSecondValue)));
					pop.labels.Add(onWeaponInv);
					yield return pop;
					instructions[i + 1].labels.Add(onInventoryFetch);
				}
				else if (instruction.opcode == OpCodes.Call && ((instruction.OperandIs(objectInequality) && instructions[i - 2].opcode == OpCodes.Ldfld && instructions[i - 2].OperandIs(containerField)) || (instruction.OperandIs(objectImplicit) && instructions[i - 1].opcode == OpCodes.Ldfld && instructions[i - 1].OperandIs(containerField))))
				{
					yield return instruction;
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(AddFakeItemContainer), nameof(IsOpenInventory)));
					yield return new CodeInstruction(OpCodes.Or);
				}
				else if (instruction.opcode == OpCodes.Call && instruction.OperandIs(objectEquality) && instructions[i - 2].opcode == OpCodes.Ldfld && instructions[i - 2].OperandIs(containerField))
				{
					yield return instruction;
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(AddFakeItemContainer), nameof(IsOpenInventory)));
					yield return new CodeInstruction(OpCodes.Not);
					yield return new CodeInstruction(OpCodes.And);
				}
				else
				{
					yield return instruction;
				}
			}
		}
	}

	[HarmonyPatch]
	private class CloseFakeItemContainer
	{
		private static IEnumerable<MethodInfo> TargetMethods() => new[]
		{
			AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.Hide)),
			AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.CloseContainer)),
		};

		private static void Prefix(InventoryGui __instance)
		{
			if (OpenContainer is { } container)
			{
				RectTransform takeAllButton = (RectTransform)__instance.m_takeAllButton.transform;
				Vector2 anchoredPosition = takeAllButton.anchoredPosition;
				anchoredPosition = new Vector2(anchoredPosition.x, -anchoredPosition.y);
				takeAllButton.anchoredPosition = anchoredPosition;
				takeAllButton.gameObject.SetActive(true);

				container.Close();
				container.Inventory.m_onChanged -= SaveItemContainer;

				OpenContainer = null;
			}
		}
	}

	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateContainer))]
	public class OpenFakeItemsContainer
	{
		private static bool Open(InventoryGui invGui, ItemDrop.ItemData? item)
		{
			ItemInfo? itemInfo = item?.Data();

			if (itemInfo?.Get<ItemContainer>() is { } container)
			{
				if (invGui.IsContainerOpen())
				{
					invGui.CloseContainer();
				}

				if (!container.Open())
				{
					return true;
				}

				OpenContainer = container;

				Inventory inv = container.Inventory;
				inv.m_onChanged += SaveItemContainer;

				RectTransform takeAllButton = (RectTransform)invGui.m_takeAllButton.transform;
				Vector2 anchoredPosition = takeAllButton.anchoredPosition;
				anchoredPosition = new Vector2(anchoredPosition.x, -anchoredPosition.y);
				takeAllButton.anchoredPosition = anchoredPosition;

				if (!container.ShowTakeAllButton())
				{
					takeAllButton.gameObject.SetActive(false);
				}
			}

			if (OpenContainer is not null)
			{
				ItemDrop.ItemData containerItem = OpenContainer.Item;
				if (invGui.m_playerGrid.GetInventory().GetItemAt(containerItem.m_gridPos.x, containerItem.m_gridPos.y) != containerItem)
				{
					invGui.CloseContainer();
					return true;
				}

				invGui.m_container.gameObject.SetActive(true);
				invGui.m_containerGrid.UpdateInventory(OpenContainer.Inventory, null, invGui.m_dragItem);
				invGui.m_containerName.text = OpenContainer.GetContainerTitle();
				if (invGui.m_firstContainerUpdate)
				{
					invGui.m_containerGrid.ResetView();
					invGui.m_firstContainerUpdate = false;
				}
				return false;
			}

			return true;
		}

		private static bool Prefix(InventoryGui __instance)
		{
			ItemDrop.ItemData? item = null;
			if (ZInput.GetButton("Use") || ZInput.GetButton("JoyUse"))
			{
				Vector2 pos = Input.mousePosition;
				item = __instance.m_playerGrid.GetItem(new Vector2i(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y)));
				if (item?.Data().Get<ItemContainer>() is not { } container || !container.AllowOpeningByKeypress())
				{
					return true;
				}
			}

			return Open(__instance, item);
		}
	}

	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
	private class CatchInventoryUseButton
	{
		private static bool ShallPreventInventoryClose(InventoryGui invGui)
		{
			if (ZInput.GetButton("Use") || ZInput.GetButton("JoyUse"))
			{
				if (Backpacks.preventInventoryClosing.Value == Backpacks.Toggle.On)
				{
					return RectTransformUtility.RectangleContainsScreenPoint(invGui.m_playerGrid.m_gridRoot, Input.mousePosition);
				}

				Vector2 pos = Input.mousePosition;
				return invGui.m_playerGrid.GetItem(new Vector2i(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y)))?.Data().Get<ItemContainer>() is not null;
			}
			return false;
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionsList)
		{
			List<CodeInstruction> instructions = instructionsList.ToList();
			MethodInfo buttonReset = AccessTools.DeclaredMethod(typeof(ZInput), nameof(ZInput.ResetButtonStatus));
			bool first = true;
			for (int i = 0; i < instructions.Count; ++i)
			{
				if (first && i + 1 < instructions.Count && instructions[i + 1].opcode == OpCodes.Call && instructions[i + 1].OperandIs(buttonReset))
				{
					first = false;
					int j = i;
					Label? target;
					while (!instructions[j].Branches(out target))
					{
						--j;
					}
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(CatchInventoryUseButton), nameof(ShallPreventInventoryClose)));
					yield return new CodeInstruction(OpCodes.Brtrue, target!.Value);
				}
				yield return instructions[i];
			}
		}
	}

	[HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
	private class RestrictItemContainerItems
	{
		private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, ref int amount, Vector2i pos, ref bool __result)
		{
			if (__instance.m_inventory != OpenContainer?.Inventory)
			{
				if (fromInventory == OpenContainer?.Inventory)
				{
					if (!OpenContainer.CanRemoveItem(item))
					{
						__result = false;
						return false;
					}

					ItemDrop.ItemData existingItem = __instance.m_inventory.GetItemAt(pos.x, pos.y);

					if (existingItem is not null && existingItem != item)
					{
						if (!OpenContainer.CanAddItem(existingItem))
						{
							__result = false;
							return false;
						}

						if (!OpenContainer.AllowStacking() && existingItem.m_stack > 1)
						{
							Vector2i emptySlot = __instance.m_inventory.FindEmptySlot(false);
							if (!__instance.m_inventory.AddItem(existingItem, existingItem.m_stack - 1, emptySlot.x, emptySlot.y))
							{
								__result = false;
								return false;
							}
						}
					}

					if (!OpenContainer.RemoveItem(item))
					{
						__result = false;
						return false;
					}
				}

				return true;
			}

			ItemDrop.ItemData? oldItem = __instance.m_inventory.GetItemAt(pos.x, pos.y);
			if (oldItem == item)
			{
				return true;
			}

			if (oldItem is not null && fromInventory != __instance.GetInventory() && !OpenContainer.CanRemoveItem(oldItem))
			{
				__result = false;
				return false;
			}

			if (!OpenContainer.CanAddItem(item))
			{
				__result = false;
				return false;
			}

			if (oldItem is not null && fromInventory != __instance.GetInventory())
			{
				OpenContainer.RemoveItem(oldItem);
			}

			if (!OpenContainer.AllowStacking() && amount > 1)
			{
				Vector2i emptySlot = fromInventory.FindEmptySlot(false);
				fromInventory.AddItem(item, item.m_stack - 1, emptySlot.x, emptySlot.y);
				amount = 1;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
	private class RestrictMovingItems
	{
		private static bool Prefix(InventoryGrid grid, ref ItemDrop.ItemData? item, InventoryGrid.Modifier mod)
		{
			if (item is not null && mod == InventoryGrid.Modifier.Move && OpenContainer?.Inventory == grid.m_inventory)
			{
				// Moving outside of container, into inventory
				if (!OpenContainer.CanRemoveItem(item))
				{
					return false;
				}

				if (!OpenContainer.RemoveItem(item))
				{
					grid.m_inventory.RemoveItem(item);
					item = null;
				}
			}
			else if (item is not null && mod == InventoryGrid.Modifier.Move && OpenContainer?.Inventory is { } inventory && inventory != grid.m_inventory)
			{
				// Moving into container
				if (!OpenContainer.CanAddItem(item))
				{
					return false;
				}

				if (!OpenContainer.AllowStacking() && item.m_stack > 1)
				{
					Vector2i emptySlot = grid.m_inventory.FindEmptySlot(false);
					return grid.m_inventory.AddItem(item, item.m_stack - 1, emptySlot.x, emptySlot.y);
				}
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
	private static class RestrictThrowingItemContainerItemsOnGround
	{
		private static bool Prefix(ref bool __result, Inventory inventory, ItemDrop.ItemData item)
		{
			if (inventory == OpenContainer?.Inventory && (!OpenContainer.CanRemoveItem(item) || !OpenContainer.RemoveItem(item)))
			{
				__result = false;
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight))]
	private static class IncreaseWeightByContainerContents
	{
		private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
		{
			if (__instance.Data().Get<ItemContainer>() is { } container)
			{
				__result += container.Inventory.GetTotalWeight() * container.WeightFactor();
			}
		}
	}

	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
	private static class AddPressKeyToOpenTooltip
	{
		private static bool Updated = false;

		private static void Postfix(InventoryGui __instance)
		{
			if (!Updated)
			{
				Updated = true;
				UITooltip tooltip = __instance.m_playerGrid.m_elementPrefab.GetComponent<UITooltip>();
				tooltip.gameObject.AddComponent<TooltipPressOpen>();
				Transform tooltipBkg = tooltip.m_tooltipPrefab.transform.Find("Bkg");
				Text templateText = tooltipBkg.transform.Find("Text").GetComponent<Text>();
				Outline templateOutline = tooltipBkg.transform.Find("Text").GetComponent<Outline>();
				GameObject pressText = new("PressToOpenItemContainer");
				pressText.transform.SetParent(tooltipBkg);
				Text text = pressText.AddComponent<Text>();
				text.font = templateText.font;
				text.fontSize = templateText.fontSize;
				text.alignment = TextAnchor.UpperCenter;
				ContentSizeFitter fitter = pressText.AddComponent<ContentSizeFitter>();
				fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
				fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
				Outline outline = pressText.AddComponent<Outline>();
				outline.effectColor = templateOutline.effectColor;
				outline.effectDistance = templateOutline.effectDistance;
				pressText.SetActive(false);
			}
		}
	}

	[HarmonyPatch(typeof(UITooltip), nameof(UITooltip.UpdateTextElements))]
	private static class SetPressKeyOnOpenTooltip
	{
		private static void Postfix(UITooltip __instance)
		{
			if (UITooltip.m_tooltip?.transform.Find("Bkg/PressToOpenItemContainer") is { } transform)
			{
				if (__instance.GetComponent<TooltipPressOpen>()?.text is { } pressOpen)
				{
					transform.GetComponent<Text>().text = pressOpen;
					transform.gameObject.SetActive(true);
				}
				else
				{
					transform.gameObject.SetActive(false);
				}
			}
		}
	}

	[HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.CreateItemTooltip))]
	private static class SetPressOpenTooltipText
	{
		private static void Prefix(ItemDrop.ItemData item, UITooltip tooltip)
		{
			if (tooltip.GetComponent<TooltipPressOpen>() is { } pressOpen)
			{
				pressOpen.text = item.Data().Get<ItemContainer>()?.GetPressOpenText();
			}
		}
	}

	private class TooltipPressOpen : MonoBehaviour
	{
		public string? text;
	}
}
