using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Backpacks;

public static class ConfigLoader
{
	public static readonly Loader[] loaders = { new CustomBackpackConfig.Loader() };

	public interface Loader
	{
		List<string> ErrorCheck(object? yaml);
		void ApplyConfig();
		List<string> ProcessConfig(string key, object? yaml);
		void Reset();
		string FilePattern { get; }
		string EditButtonName { get; }
		CustomSyncedValue<List<string>> FileData { get; }
		bool Enabled { get; }
	}

	[HarmonyPatch(typeof(Game), nameof(Game.RequestRespawn))]
	private class PatchConfigReading
	{
		[UsedImplicitly]
		[HarmonyPriority(Priority.VeryLow)]
		private static void Postfix(bool ___m_firstSpawn)
		{
			foreach (Loader loader in loaders)
			{
				if (___m_firstSpawn && loader.Enabled)
				{
					loader.ApplyConfig();
				}
			}
		}
	}
	
	[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
	private class ServerApplyConfig
	{
		[UsedImplicitly]
		[HarmonyPriority(Priority.VeryLow)]
		private static void Postfix()
		{
			if (ZNet.instance && ZNet.instance.IsDedicated())
			{
				foreach (Loader loader in loaders)
				{
					if (loader.Enabled)
					{
						loader.ApplyConfig();
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
	private class DelayedConfigLoading
	{
		[UsedImplicitly]
		private static void Postfix() => loadConfigFile();
	}

	[HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
	private static class RegisterRPCPatch
	{
		[UsedImplicitly]
		private static void Postfix(ZNet __instance)
		{
			bool IsDedicatedServer = __instance.IsDedicated() && __instance.IsServer();

			if (IsDedicatedServer)
			{
				loadConfigFile();
			}
		}
	}

	public static bool SkipSavingOfValueChange = false;
	private static bool initialized = false;

	public static void reloadConfigFile()
	{
		foreach (Loader loader in loaders)
		{
			loader.Reset();
			loadConfigFile(loader);

			if (ZNetScene.instance?.GetPrefab("_ZoneCtrl") != null)
			{
				loader.ApplyConfig();
			}
		}
	}

	private static void loadConfigFile()
	{
		if (initialized)
		{
			return;
		}

		foreach (Loader loader in loaders)
		{
			loadConfigFile(loader);
		}

		initialized = true;
	}

	private static bool isAcceptableConfigFile(string p) => !Path.GetFileName(p).StartsWith($"{Backpacks.ModName}.") || !Localization.instance.m_languages.Contains(Path.GetFileName(p).Split('.')[1]);

	private static void loadConfigFile(Loader loader)
	{
		List<string> paths = Backpacks.configFilePaths.SelectMany(path => Directory.GetFiles(path, loader.FilePattern)).Where(isAcceptableConfigFile).OrderBy(p => Path.GetFileName(p) != loader.FilePattern.Replace("*", "")).ThenBy(Path.GetFileName).ToList();
		if (paths.Count > 0 && Backpacks.useExternalYaml.Value == Backpacks.Toggle.On)
		{
			Dictionary<string, string> files = paths.ToDictionary(p => p, File.ReadAllText);

			foreach (KeyValuePair<string, string> file in files)
			{
				try
				{
					object configObj = new DeserializerBuilder().Build().Deserialize<object>(file.Value);
					List<string> errors = loader.ProcessConfig(file.Key, configObj);
					if (errors.Count > 0)
					{
						Debug.LogError($"Found {Backpacks.ModName} config errors in file at {file.Key}. Please review the syntax of your file:\n{string.Join("\n", errors)}");
						Debug.LogWarning($"Ignoring some configurations of {Backpacks.ModName} config file.");
					}
				}
				catch (YamlException e)
				{
					Debug.LogError($"Found a {Backpacks.ModName} config file at {file.Key}, but parsing failed with an error:\n{e.Message + (e.InnerException != null ? ": " + e.InnerException.Message : "")}");
					Debug.LogWarning($"Ignoring {Backpacks.ModName} config file.");
				}
			}

			loader.FileData.AssignLocalValue(files.SelectMany(kv => new[] { kv.Key, kv.Value }).ToList());
		}

		if (initialized)
		{
			return;
		}

		loader.FileData.ValueChanged += () =>
		{
			if (Backpacks.useExternalYaml.Value == Backpacks.Toggle.Off)
			{
				return;
			}

			loader.Reset();

			Dictionary<string, string> files = loader.FileData.Value.Select((f, index) => new { f, index }).GroupBy(g => g.index / 2).ToDictionary(g => g.First().f, g => g.Last().f);
			bool hasErrors = false;

			foreach (KeyValuePair<string, string> file in files)
			{
				try
				{
					object configObj = new DeserializerBuilder().Build().Deserialize<object>(file.Value);
					List<string> errors = loader.ProcessConfig(file.Key, configObj);
					if (errors.Count > 0)
					{
						Debug.LogError($"Received new {Backpacks.ModName} yaml config data, with errors in file at {file.Key}. Please review the syntax of your file:\n{string.Join("\n", errors)}");
						hasErrors = true;
					}
				}
				catch (YamlException e)
				{
					Debug.LogError($"Received new {Backpacks.ModName} yaml config data, but parsing of {file.Key} failed with an error:\n{e.Message + (e.InnerException != null ? ": " + e.InnerException.Message : "")}");
					hasErrors = true;
				}
			}

			if (hasErrors)
			{
				Debug.LogWarning($"Ignoring the changed {Backpacks.ModName} config data, retaining the originally received data. Dumping the config contents for inspection.");
				foreach (KeyValuePair<string, string> file in files)
				{
					Debug.LogWarning($"Contents of {file.Key}:");
					Debug.Log(file.Value);
				}
			}
			else
			{
				if (ZNetScene.instance?.GetPrefab("_ZoneCtrl") != null && loader.Enabled)
				{
					loader.ApplyConfig();
				}

				if (!SkipSavingOfValueChange && Backpacks.configSync.IsSourceOfTruth)
				{
					foreach (KeyValuePair<string, string> file in files)
					{
						try
						{
							string tempfile = file.Key + ".temp";
							File.WriteAllText(tempfile, file.Value);
							File.Replace(tempfile, file.Key, null);
						}
						catch (IOException e)
						{
							Debug.LogError($"Failed writing config back to {file.Key}, got error message: {e.Message}");
						}
					}
				}
			}
		};

		foreach (FileSystemWatcher watcher in Backpacks.configFilePaths.Select(path => new FileSystemWatcher(path, loader.FilePattern)))
		{
			watcher.Changed += consumeConfigFileEvent;
			watcher.Created += consumeConfigFileEvent;
			watcher.Renamed += consumeConfigFileEvent;
			watcher.IncludeSubdirectories = true;
			watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
			watcher.EnableRaisingEvents = true;
		}
	}

	private static void consumeConfigFileEvent(object sender, FileSystemEventArgs args)
	{
		if (((Backpacks.Toggle?)Backpacks.useExternalYaml.LocalBaseValue ?? Backpacks.useExternalYaml.Value) == Backpacks.Toggle.Off)
		{
			return;
		}

		foreach (Loader loader in loaders)
		{
			if (!Path.GetFileName(args.Name).StartsWith(Regex.Replace(loader.FilePattern, @"\*.*", "")))
			{
				continue;
			}

			List<string> paths = Backpacks.configFilePaths.SelectMany(path => Directory.GetFiles(path, loader.FilePattern)).Where(isAcceptableConfigFile).ToList();
			if (paths.Count > 0)
			{
				Dictionary<string, string> files = paths.ToDictionary(p => p, File.ReadAllText);
				bool hasErrors = false;

				loader.Reset();

				foreach (KeyValuePair<string, string> file in files)
				{
					try
					{
						object configObj = new DeserializerBuilder().Build().Deserialize<object>(file.Value);
						List<string> errors = loader.ProcessConfig(file.Key, configObj);
						if (errors.Count > 0)
						{
							Debug.LogError($"Found {Backpacks.ModName} config errors in file at {file.Key}. Please review the syntax of your file:\n{string.Join("\n", errors)}");
							hasErrors = true;
						}
					}
					catch (YamlException e)
					{
						Debug.LogError($"Found a {Backpacks.ModName} config file at {file.Key}, but parsing failed with an error:\n{e.Message + (e.InnerException != null ? ": " + e.InnerException.Message : "")}");
						hasErrors = true;
					}
				}

				if (hasErrors)
				{
					Debug.LogWarning("Ignoring the changed config file, retaining the originally loaded file.");
				}
				else
				{
					SkipSavingOfValueChange = true;
					loader.FileData.AssignLocalValue(files.SelectMany(kv => new[] { kv.Key, kv.Value }).ToList());
					SkipSavingOfValueChange = false;
					Debug.Log($"Successfully reloaded {Backpacks.ModName} {loader.FilePattern} yml config" + (Backpacks.configSync.IsSourceOfTruth ? "" : ", but skipped applying as remote configuration is active currently."));
				}
			}
		}
	}
}
