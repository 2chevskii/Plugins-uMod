using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
	[Info("NPC Drop Gun", "2CHEVSKII", "2.0.8")]
	[Description("Forces NPC to drop used gun and other items after death")]
	public class NPCDropGun : RustPlugin
	{
		#region Fields

		Settings settings;
		Dictionary<BasePlayer, List<Item>> delayedItems;

		#endregion

		#region Oxide hooks

		void Init() => delayedItems = new Dictionary<BasePlayer, List<Item>>();

		void OnServerInitialized()
		{
			// Warn if BotSpawn may cause unexpected behaviour
			if ((bool)Manager.GetPlugin("BotSpawn"))
				PrintWarning($"BotSpawn plugin found! Some ammo and loot might not be handled correctly!");
		}

		void OnEntityDeath(BasePlayer player)
		{
			if (player is NPCPlayer)
			{
				DoSpawns(player);
			}
		}

		void OnCorpsePopulate(BasePlayer player, PlayerCorpse corpse)
		{
			if (!player || !corpse || !delayedItems.ContainsKey(player))
			{
				return;
			}

			if (settings.RemoveDefault)
			{
				for (int i = 0; i < corpse.containers.Length; i++)
				{
					corpse.containers[i].Clear();
				}
			}

			var list = delayedItems[player];

			for (int i = 0; i < list.Count; i++)
			{
				var item = list[i];
				if (!item.MoveToContainer(corpse.containers[0]) && !item.MoveToContainer(corpse.containers[2]))
                {
                    if (settings.DropNearFull)
                        ApplyVelocity(DropNearPosition(item, corpse.transform.position + new Vector3(0, 0.3f)));
					else item.Remove();
				}
            }

			Pool.FreeList(ref list);

			delayedItems.Remove(player);
		}

		#endregion

		#region Core

		void DoSpawns(BasePlayer player)
		{
			delayedItems[player] = Pool.GetList<Item>();

			if (Random.Range(0.0f, 1.0f) <= settings.Meds.DropChance)
			{
				var meds = SpawnMeds();

				if (meds != null)
				{
					delayedItems[player].Add(meds);
				}
			}

			var definition = player.inventory?.containerBelt?.FindItemByUID(player.svActiveItemID)?.info;

			if (definition == null)
			{
				return;
			}

			var itemWeapon = ItemManager.Create(definition, 1, settings.Guns.RandomSkin ? GetRandomSkin(definition) : 0uL);

			if (itemWeapon == null)
			{
				return;
			}

			var condition = Random.Range(settings.Guns.Condition.Min, settings.Guns.Condition.Max);

			itemWeapon.conditionNormalized = condition / 100;

			var heldEnt = itemWeapon.GetHeldEntity();

			if (heldEnt is BaseProjectile && Random.Range(0.0f, 1.0f) <= settings.Ammo.DropChance)
			{
				var ammo = SpawnAmmo(heldEnt as BaseProjectile);

				if (ammo != null)
				{
					delayedItems[player].Add(ammo);
				}
			}

			if (Random.Range(0.0f, 1.0f) <= settings.Guns.DropChance)
			{
				SetAttachments(itemWeapon);

				if (settings.GunIntoCorpse || !player.eyes || !heldEnt)
				{
					delayedItems[player].Add(itemWeapon);
				}
				else
				{
					ApplyVelocity(DropNearPosition(itemWeapon, player.eyes.position));
				}
			}
		}

		Item SpawnMeds()
		{
			int amount = Random.Range((int)settings.Meds.Amount.Min, (int)settings.Meds.Amount.Max);

			return amount < 1 ? null : ItemManager.CreateByName("syringe.medical", amount);
		}

		Item SpawnAmmo(BaseProjectile weapon)
		{
			if (!weapon.primaryMagazine.ammoType)
			{
				return null;
			}

			int amount = Random.Range((int)settings.Ammo.Amount.Min, (int)settings.Ammo.Amount.Max);

			return amount < 1 ? null : ItemManager.Create(weapon.primaryMagazine.ammoType, amount);
		}

		Item SetAttachments(Item item)
		{
			if (settings.Guns.Attachments.Length < 1 || item.contents == null)
			{
				return item;
			}

			var attachmentCount = Random.Range(settings.Guns.AttachmentCount.Min, settings.Guns.AttachmentCount.Max);

			for (int i = 0; i < attachmentCount; i++)
			{
				var attachment = settings.Guns.Attachments.GetRandom();

				var attachmentItem = ItemManager.CreateByPartialName(attachment);

				if (attachmentItem == null || !item.contents.CanAccept(attachmentItem))
				{
					continue;
				}

				attachmentItem.MoveToContainer(item.contents);
			}

			return item;
		}

		BaseEntity DropNearPosition(Item item, Vector3 pos) => item.CreateWorldObject(pos);

		BaseEntity ApplyVelocity(BaseEntity entity)
		{
			entity.SetVelocity(new Vector3(Random.Range(-4f, 4f), Random.Range(-0.3f, 2f), Random.Range(-4f, 4f)));
			entity.SetAngularVelocity(
				new Vector3(Random.Range(-10f, 10f),
				Random.Range(-10f, 10f),
				Random.Range(-10f, 10f))
			);
			entity.SendNetworkUpdateImmediate();
			return entity;
		}

		ulong GetRandomSkin(ItemDefinition idef)
		{
			if (!idef)
				return 0;

			List<int> skins = Pool.GetList<int>();

			if (idef.skins != null && idef.skins.Length > 0)
			{
				skins.AddRange(from skin in idef.skins select skin.id);
			}

			if (idef.skins2 != null && idef.skins2.Length > 0)
			{
				skins.AddRange(from skin in idef.skins2 where skin != null select skin.DefinitionId);
			}

			var randomSkin = skins.GetRandom();

			Pool.FreeList(ref skins);

			return randomSkin == 0 ? 0 : ItemDefinition.FindSkin(idef.itemid, randomSkin);
		}

		#endregion

		#region Configuration

		protected override void LoadDefaultConfig()
		{
			settings = Settings.Default;
			SaveConfig();
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			try
			{
				settings = Config.ReadObject<Settings>();

				if (settings == null)
				{
					throw new Exception();
				}
			}
			catch
			{
				LoadDefaultConfig();
			}
		}

		protected override void SaveConfig() => Config.WriteObject(settings);

		class Settings
		{
			public GunSettings Guns { get; set; }
			public OtherSettings Ammo { get; set; }
			public OtherSettings Meds { get; set; }

			[JsonProperty("Put weapon into corpse")]
			public bool GunIntoCorpse { get; set; }
			[JsonProperty("Remove default loot from corpse")]
			public bool RemoveDefault { get; set; }
			[JsonProperty("Drop spawned items near corpse (otherwise just delete them)")]
			public bool DropNearFull { get; set; }

			public static Settings Default => new Settings
			{
				Guns = new GunSettings
				{
					DropChance = 1.0f,
					AttachmentCount = new RangeSettings
					{
						Min = 0,
						Max = 2
					},
					Condition = new RangeSettings
					{
						Min = 5,
						Max = 95
					},
					RandomSkin = true,
					Attachments = new[]
					{
						"weapon.mod.8x.scope",
						"weapon.mod.flashlight",
						"weapon.mod.holosight",
						"weapon.mod.lasersight",
						"weapon.mod.muzzleboost",
						"weapon.mod.muzzlebrake",
						"weapon.mod.silencer",
						"weapon.mod.simplesight",
						"weapon.mod.small.scope"
					}
				},
				Ammo = new OtherSettings
				{
					DropChance = 0.8f,
					Amount = new RangeSettings
					{
						Min = 10,
						Max = 55
					}
				},
				Meds = new OtherSettings
				{
					DropChance = 0.4f,
					Amount = new RangeSettings
					{
						Min = 1,
						Max = 3
					}
				},
				DropNearFull = true,
				GunIntoCorpse = false,
				RemoveDefault = false
			};

			public class GunSettings : DropChanceSettings
			{
				[JsonProperty("Gun condition")]
				public RangeSettings Condition { get; set; }
				[JsonProperty("Attachment count")]
				public RangeSettings AttachmentCount { get; set; }
				[JsonProperty("Attachment list")]
				public string[] Attachments { get; set; }
				[JsonProperty("Assign random skin")]
				public bool RandomSkin { get; set; }
			}

			public class DropChanceSettings
			{
				[JsonProperty("Drop chance")]
				public float DropChance { get; set; }
			}

			public class OtherSettings : DropChanceSettings
			{
				[JsonProperty("Amount to drop")]
				public RangeSettings Amount { get; set; }
			}

			public class RangeSettings
			{
				public uint Min { get; set; }
				public uint Max { get; set; }
			}
		}

		#endregion
	}
}
