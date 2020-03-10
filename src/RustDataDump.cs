using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("Rust Data Dump", "2CHEVSKII", "1.0.0")]
	class RustDataDump : CovalencePlugin
	{
		IEnumerable<ItemInfo> items;

		IEnumerable<GameManifest.EffectCategory> effectCategories;

		IEnumerable<string> entities;

		IEnumerable<string> pooledStrings;

		List<string> bundleNames;

		List<string> fileNames;

		IEnumerable<string> worldTextures;

		IEnumerable<string> uiTextures;

		IEnumerable<string> entityPrefabs;

		IEnumerable<string> scenes;

		Dictionary<int, string> layers;

		void OnServerInitialized()
		{
			Puts("Starting data dump");

			Puts("Effect categories");
			effectCategories = GameManifest.Current.effectCategories;

			Puts("Entities");
			entities = GameManifest.Current.entities;

			Puts("PooledStrings");
			pooledStrings = GameManifest.Current.pooledStrings.Select(str => str.str);

			Puts("Extracting bundles...");
			var abb = FileSystem.Backend as AssetBundleBackend;

			bundleNames = AssetBundleBackend.bundles.Select(bundle => bundle.Key).ToList();

			fileNames = AssetBundleBackend.files.Select(file => file.Key).ToList();

			worldTextures = AssetBundleBackend.bundles[bundleNames.Find(f => f.EndsWith("monuments.bundle"))].GetAllAssetNames().Where(name => name.EndsWith(".png") || name.EndsWith(".tga"));

			uiTextures = AssetBundleBackend.bundles[bundleNames.Find(f => f.EndsWith("content.bundle"))].GetAllAssetNames().Where(name => name.EndsWith(".png"));

			entityPrefabs = AssetBundleBackend.bundles[bundleNames.Find(f => f.EndsWith("prefabs.bundle"))].GetAllAssetNames();

			scenes = AssetBundleBackend.bundles[bundleNames.Find(f => f.EndsWith("maps.bundle"))].GetAllScenePaths();

			Puts("Done!");

			layers = new Dictionary<int, string>();

			for(int i = 0; i < 32; i++)
			{
				var layer = LayerMask.LayerToName(i);

				layers.Add(i, layer.Length > 0 ? layer : null);
			}

			Puts("Now waiting for items dump");

			timer.Once(20, () =>
			{
				DumpItems();
				SaveDump();
				ConVar.Global.quit(null);
			});
		}

		void SaveDump()
		{
			Puts("Serializing and saving data");

			var json = JsonConvert.SerializeObject(new
			{
				items,
				effectCategories,
				entities,
				pooledStrings,
				bundleNames,
				fileNames,
				worldTextures,
				uiTextures,
				entityPrefabs,
				scenes,
				layers
			});

			LogToFile(DateTime.Now.ToString("dd-MM-yyyy_hh-mm"), json, this, false);

			Puts("Data saved, exiting...");
		}

		void DumpItems()
		{
			Puts("Dumping items...");
			var definitions = ItemManager.GetItemDefinitions();

			var blueprints = ItemManager.GetBlueprints();

			var itemList = new List<ItemInfo>();

			for(int i = 0; i < definitions.Count; i++)
			{
				try
				{
					var def = definitions[i];

					var item = new ItemInfo
					{
						amount_type = def.amountType.ToString(),
						category = def.category.ToString(),
						condition = new ItemInfo.ConditionProps
						{
							enabled = def.condition.enabled,
							max = def.condition.max,
							repairable = def.condition.repairable,
							repair_maxcondition = def.condition.maintainMaxCondition,
							worldcondition_max = def.condition.foundCondition.fractionMax,
							worldcondition_min = def.condition.foundCondition.fractionMin
						},
						description = def.displayDescription.english,
						flags = def.flags.ToString(),
						holdable = def.isHoldable,
						itemid = def.itemid,
						name = def.displayName.english,
						rarity = def.rarity.ToString(),
						shortname = def.shortname,
						stack = def.stackable,
						usable = def.isUsable,
						wearable = def.isWearable,
						blueprint = def.Blueprint ? new ItemInfo.BlueprintInfo
						{
							craft_time = def.Blueprint.time,
							create_amount = def.Blueprint.amountToCreate,
							default_bp = def.Blueprint.defaultBlueprint,
							itemid = def.itemid,
							rarity = def.Blueprint.rarity.ToString(),
							scrap_needed = def.Blueprint.scrapRequired,
							scrap_recycled = def.Blueprint.scrapFromRecycle,
							stack = def.Blueprint.blueprintStackSize,
							user_craftable = def.Blueprint.userCraftable,
							workbench_level = def.Blueprint.workbenchLevelRequired
						} : default(ItemInfo.BlueprintInfo)
					};

					var skins = new HashSet<ItemInfo.SkinInfo>();

					if(def.HasSkins)
					{
						for(int j = 0; j < def.skins.Length; j++)
						{
							var skin = def.skins[j];

							var skinInfo = new ItemInfo.SkinInfo
							{
								pointer_id = skin.id,
								workshop_id = skin.invItem.workshopID
							};

							skins.Add(skinInfo);
						}

						for(int j = 0; j < def.skins2.Length; j++)
						{
							var skin = def.skins2[j];

							var skinInfo = new ItemInfo.SkinInfo
							{
								pointer_id = skin.Id,
								icon_url = skin.IconUrlLarge,
								workshop_id = skin.GetProperty<ulong>("workshopdownload")
							};

							skins.Add(skinInfo);
						}
					}

					item.skins = skins;

					itemList.Add(item);
				}
				catch(Exception e)
				{
					PrintError("Error while extracting items: {0}", e);
				}
			}

			items = itemList;

			Puts("Items dump finished");
		}

		class ItemInfo
		{
			public string name;
			public string description;
			public string shortname;
			public int itemid;

			public string category;
			public string rarity;

			public bool wearable;
			public bool usable;
			public bool holdable;

			public int stack;

			public ConditionProps condition;

			public string amount_type;
			public string flags;

			public BlueprintInfo blueprint;

			public IEnumerable<SkinInfo> skins;

			public struct BlueprintInfo
			{
				public int create_amount;
				public bool default_bp;
				public int stack;
				public string rarity;
				public int scrap_needed;
				public int scrap_recycled;
				public float craft_time;
				public bool user_craftable;
				public int workbench_level;
				public int itemid;
			}

			public struct ConditionProps
			{
				public bool enabled;
				public float max;

				public bool repairable;

				public bool repair_maxcondition;

				public float worldcondition_max;
				public float worldcondition_min;
			}

			public struct SkinInfo
			{
				public ulong workshop_id;
				public int pointer_id;
				public string icon_url;
			}
		}
	}
}
