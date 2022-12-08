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
	public static ItemContainer? OpenContainer;

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
			AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.IsContainerOpen))
		};

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionsList, ILGenerator ilg)
		{
			MethodInfo containerInventory = AccessTools.DeclaredMethod(typeof(Container), nameof(Container.GetInventory));
			FieldInfo containerField = AccessTools.DeclaredField(typeof(InventoryGui), nameof(InventoryGui.m_currentContainer));
			MethodInfo objectInequality = AccessTools.DeclaredMethod(typeof(Object), "op_Inequality");
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
			AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.CloseContainer))
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
		public static bool Open(InventoryGui invGui, ItemDrop.ItemData? item)
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
