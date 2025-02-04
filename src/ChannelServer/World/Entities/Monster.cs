﻿using System;
using System.Threading;
using Melia.Channel.Network;
using Melia.Channel.Skills;
using Melia.Channel.World.Entities.Components;
using Melia.Channel.World.Entities.Components.AI.Events;
using Melia.Shared.Const;
using Melia.Shared.Data.Database;
using Melia.Shared.EntityComponents;
using Melia.Shared.Util;
using Melia.Shared.World;
using Melia.Shared.World.ObjectProperties;

namespace Melia.Channel.World.Entities
{
	public class Monster : ICombatEntity, IUpdateable
	{
		private static int GenTypes = 1_000_000;

		/// <summary>
		/// Index in world collection?
		/// </summary>
		public int Handle { get; private set; }

		/// <summary>
		/// Returns the monster's entity type.
		/// </summary>
		public virtual EntityType Type => EntityType.Mob;

		/// <summary>
		/// Gets or sets the monster's faction.
		/// </summary>
		public virtual FactionType Faction { get; set; } = FactionType.Peaceful;

		private Map _map = Map.Limbo;
		/// <summary>
		/// The map the monster is currently on.
		/// </summary>
		public Map Map { get { return _map; } set { _map = value ?? Map.Limbo; } }

		/// <summary>
		/// Monster ID in database.
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// ?
		/// </summary>
		/// <remarks>
		/// Used by the anchors in the client files, with multiple anchors
		/// being able to use the same "gen type". This is also used to
		/// identify NPCs however, like in ZC_SET_NPC_STATE.
		/// </remarks>
		public int GenType { get; set; }

		/// <summary>
		/// What kind of NPC the monster is.
		/// </summary>
		public NpcType NpcType { get; set; }

		/// <summary>
		/// Monster's name, leave empty for default.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Name of dialog function to call on trigger,
		/// leave empty for no dialog hotkey display.
		/// </summary>
		public string DialogName { get; set; }

		/// <summary>
		/// Warp identifier?
		/// </summary>
		/// <remarks>
		/// Purpose unknown, doesn't seem to affect anything.
		/// Examples: WS_KLAPEDA_HIGHLANDER, WS_SIAULST1_KLAPEDA
		/// </remarks>
		public string WarpName { get; set; }

		/// <summary>
		/// Returns true if WarpName is not empty.
		/// </summary>
		public bool IsWarp => !string.IsNullOrWhiteSpace(this.WarpName);

		/// <summary>
		/// Location to warp to.
		/// </summary>
		public Location WarpLocation { get; set; }

		/// <summary>
		/// Level.
		/// </summary>
		public int Level { get; set; } = 1;

		/// <summary>
		/// Gets or sets the monster's current position.
		/// </summary>
		public Position Position { get; set; }

		/// <summary>
		/// Monster's direction.
		/// </summary>
		public Direction Direction { get; set; }

		/// <summary>
		/// AoE Defense Ratio
		/// </summary>
		public int SDR { get; set; } = 1;

		/// <summary>
		/// Gets or sets the monster's HP, capped to 0~MaxHp.
		/// </summary>
		public int Hp
		{
			get { return _hp; }
			private set { _hp = Math2.Clamp(0, this.MaxHp, value); }
		}
		private int _hp = 100;

		/// <summary>
		/// Maximum health points.
		/// </summary>
		public int MaxHp { get; private set; } = 100;

		/// <summary>
		/// Physical defense.
		/// </summary>
		public int Defense
		{
			get { return _defense; }
			private set { _defense = Math.Max(0, value); }
		}
		private int _defense;

		/// <summary>
		/// Raised when the monster died.
		/// </summary>
		public event Action<Monster, ICombatEntity> Died;

		/// <summary>
		/// At this time the monster will be removed from the map.
		/// </summary>
		public DateTime DisappearTime { get; set; } = DateTime.MaxValue;

		/// <summary>
		/// Data entry for this monster.
		/// </summary>
		public MonsterData Data { get; private set; }

		/// <summary>
		/// Returns whether the monster is dead.
		/// </summary>
		public bool IsDead => this.Hp == 0;

		/// <summary>
		/// Gets or sets whether the monster appears from inside the ground.
		/// </summary>
		/// <remarks>
		/// If this property is true, the monster will dig itself out of the
		/// ground when it appears. 
		/// </remarks>
		public bool FromGround { get; set; }

		/// <summary>
		/// Gets or sets the monster's state.
		/// </summary>
		public MonsterState State { get; set; }

		/// <summary>
		/// Returns the monster's property collection.
		/// </summary>
		public Properties Properties { get; private set; }

		/// <summary>
		/// Returns the monster's component collection.
		/// </summary>
		public ComponentCollection Components { get; } = new ComponentCollection();

		/// <summary>
		/// Monster's buffs.
		/// </summary>
		public BuffCollection Buffs { get; }

		/// <summary>
		/// Creates new NPC.
		/// </summary>
		public Monster(int id, NpcType type)
		{
			this.Handle = ChannelServer.Instance.World.CreateHandle();

			// The client files set the gen type manually, but that seems
			// bothersome. For now, we'll generate them automatically and
			// see for what purpose we would need them to be the same for
			// multiple monsters.
			this.GenType = Interlocked.Increment(ref GenTypes);

			this.Id = id;
			this.NpcType = type;

			this.Components.Add(this.Buffs = new BuffCollection(this));

			this.LoadData();
		}

		/// <summary>
		/// Loads data from data files.
		/// </summary>
		protected virtual void LoadData()
		{
			if (this.Id == 0)
				throw new InvalidOperationException("Id wasn't set before calling LoadData.");

			this.Data = ChannelServer.Instance.Data.MonsterDb.Find(this.Id);
			if (this.Data == null)
				throw new NullReferenceException("No data found for '" + this.Id + "'.");

			this.Hp = this.MaxHp = this.Data.Hp;
			this.Defense = this.Data.PhysicalDefense;
			this.Faction = this.Data.Faction;

			this.Properties = new MonsterProperties(this);
		}

		/// <summary>
		/// Makes monster take damage and kills it if the HP reach 0.
		/// Returns true if the monster is dead.
		/// </summary>
		/// <param name="damage"></param>
		/// <param name="attacker"></param>
		/// <returns></returns>
		public bool TakeDamage(int damage, Character attacker)
		{
			// Don't hit an already dead monster
			if (this.IsDead)
				return true;

			this.Hp -= damage;

			// Kill monster if it reached 0 HP.
			if (this.Hp == 0)
			{
				this.Kill(attacker);
				return true;
			}

			if (this.Components.TryGet<EntityAi>(out var ai))
				ai.QueueEvent(new HitEvent(attacker?.Handle ?? 0));

			return false;
		}

		/// <summary>
		/// Kills monster.
		/// </summary>
		/// <param name="killer"></param>
		public void Kill(Character killer)
		{
			this.Hp = 0;

			var expRate = ChannelServer.Instance.Conf.World.ExpRate / 100;
			var classExpRate = ChannelServer.Instance.Conf.World.ClassExpRate / 100;

			var exp = 0;
			var classExp = 0;

			if (this.Data.Exp > 0)
				exp = (int)Math.Max(1, this.Data.Exp * expRate);
			if (this.Data.ClassExp > 0)
				classExp = (int)Math.Max(1, this.Data.ClassExp * classExpRate);

			this.DisappearTime = DateTime.Now.AddSeconds(2);

			this.DropItems(killer);

			killer?.GiveExp(exp, classExp, this);
			this.Died?.Invoke(this, killer);

			Send.ZC_DEAD(this);
		}

		/// <summary>
		/// Drops random items from the monster's drop table.
		/// </summary>
		/// <param name="killer"></param>
		private void DropItems(Character killer)
		{
			if (this.Data.Drops == null)
				return;

			var rnd = RandomProvider.Get();
			var autoloot = killer?.Variables.Temp.Get("Autoloot", 0) ?? 0;

			foreach (var dropItemData in this.Data.Drops)
			{
				var dropChance = dropItemData.DropChance;
				dropChance *= ChannelServer.Instance.Conf.World.DropRate / 100f;

				if (rnd.NextDouble() > dropChance / 100f)
					continue;

				if (!ChannelServer.Instance.Data.ItemDb.TryFind(dropItemData.ItemId, out var itemData))
				{
					Log.Warning("Monster.Kill: Drop item '{0}' not found.", dropItemData.ItemId);
					continue;
				}

				var dropItem = new Item(itemData.Id);
				dropItem.Amount = rnd.Next(dropItemData.MinAmount, dropItemData.MaxAmount + 1);

				if (killer == null || dropChance > autoloot)
				{
					var direction = new Direction(rnd.Next(0, 360));
					var dropRadius = ChannelServer.Instance.Conf.World.DropRadius;
					var distance = rnd.Next(dropRadius / 2, dropRadius + 1);

					dropItem.SetLootProtection(killer, TimeSpan.FromSeconds(ChannelServer.Instance.Conf.World.LootPrectionSeconds));
					dropItem.Drop(this.Map, this.Position, direction, distance);
				}
				else
				{
					killer.Inventory.Add(dropItem, InventoryAddType.PickUp);
				}
			}
		}

		/// <summary>
		/// Returns true if the monster can attack the entity.
		/// </summary>
		/// <param name="entity"></param>
		/// <returns></returns>
		public bool CanAttack(ICombatEntity entity)
		{
			// For now, let's specify that monsters can attack characters.
			return (entity is Character);
		}

		/// <summary>
		/// Updates monster and its components.
		/// </summary>
		/// <param name="elapsed"></param>
		public void Update(TimeSpan elapsed)
		{
			this.Components.Update(elapsed);
		}

		/// <summary>
		/// Changes the monster's state and updates the clients in range
		/// of the monster.
		/// </summary>
		/// <param name="state"></param>
		public void SetState(MonsterState state)
		{
			this.State = state;
			Send.ZC_SET_NPC_STATE(this);
		}

		/// <summary>
		/// Heals the monster's HP and SP by the given amounts.
		/// </summary>
		/// <param name="hpAmount"></param>
		/// <param name="spAmount"></param>
		public void Heal(float hpAmount, float spAmount)
		{
			this.Properties.Set(PropertyId.PC.HP, this.Properties.GetFloat(PropertyId.PC.MHP));
			this.Properties.Set(PropertyId.PC.SP, this.Properties.GetFloat(PropertyId.PC.MSP));

			Send.ZC_UPDATE_ALL_STATUS(this);
		}
	}
}
