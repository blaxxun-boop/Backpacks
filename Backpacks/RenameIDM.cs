using System;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.Build.Utilities;
using Mono.Cecil;

namespace Backpacks;

[RepackDrop]
// ReSharper disable once CheckNamespace
public class RepackDropAttribute : Attribute
{
}

[RepackDrop]
[PublicAPI]
public class RenameIDM : Task
{
	public string DLL { get; set; } = null!;

	public override bool Execute()
	{
		string target = Path.GetDirectoryName(DLL) + Path.DirectorySeparatorChar + "Pre-RenameIDM." + Path.GetFileName(DLL);
		File.Delete(target);
		File.Move(DLL, target);
		
		AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(target);
		TypeDefinition type = assembly.MainModule.GetType("ItemDataManager.ItemData");
		type.Name = "BackpacksItemData";

		if (DLL.EndsWith("API.dll"))
		{
			type.Attributes = (type.Attributes & ~TypeAttributes.VisibilityMask) | TypeAttributes.Public;

			type = assembly.MainModule.GetType("ItemDataManager.ItemInfo");
			type.Methods.Remove(type.Methods.Single(m => m.Name == ".cctor"));
		}

		assembly.Write(DLL);

		return true;
	}
}