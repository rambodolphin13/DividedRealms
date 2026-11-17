/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using DOL.Database;
using DOL.Language;
using DOL.GS.PacketHandler;
using System;
using System.Collections.Generic;
using DOL.GS.Keeps;

namespace DOL.GS
{
	public class SiegeCrafting : AbstractProfession
	{
		public SiegeCrafting()
			: base()
		{
			Icon = 0x03;
			Name = LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "Crafting.Name.Siegecraft");
			eSkill = eCraftingSkill.SiegeCrafting;
		}
		public override string CRAFTER_TITLE_PREFIX
		{
			get
			{
				return "Siegecrafter";
			}
		}

        protected override String Profession
        {
            get
            {
                return "CraftersProfession.Siegecrafter";
            }
        }

		public override void GainCraftingSkillPoints(GamePlayer player, Recipe recipe)
		{
			if (Util.Chance(CalculateChanceToGainPoint(player, recipe.Level)))
			{
				player.GainCraftingSkill(eCraftingSkill.SiegeCrafting, 1);
				player.Out.SendUpdateCraftingSkills();
			}
		}

		/// <summary>
		/// A crafted siege engine is deployed where the crafter stands, so it gets the same place rules as the bought kits
		/// (SummonSiegeCatapult / SummonSiegeRam): frontier or battleground only, no dungeon or portal keep, one catapult
		/// or trebuchet per 500 units, two rams per 200. Checked before any material is used (crafting pass 2026-11-17).
		/// </summary>
		protected override bool CanPlayerStartToCraftItem(GamePlayer player, Recipe recipe)
		{
			if (!base.CanPlayerStartToCraftItem(player, recipe))
				return false;

			string refusal = DeployRefusal(player, (eObjectType)recipe.Product.Object_Type);
			if (refusal == null)
				return true;

			player.Out.SendMessage(refusal, eChatType.CT_System, eChatLoc.CL_SystemWindow);
			return false;
		}

		public static bool IsDeployedEngine(eObjectType type)
		{
			return type is eObjectType.SiegeBalista or eObjectType.SiegeCatapult or eObjectType.SiegeRam or eObjectType.SiegeTrebuchet;
		}

		public static string DeployRefusal(GameLiving crafter, eObjectType type)
		{
			if (!IsDeployedEngine(type))
				return null;

			if (!(crafter.CurrentZone.IsOF || crafter.CurrentZone.IsBG) || crafter.CurrentRegion.IsDungeon)
				return "You cannot deploy siege weapons here!";

			foreach (AbstractArea area in crafter.CurrentAreas)
			{
				if (area is KeepArea keepArea && keepArea.Keep.IsPortalKeep)
					return "You cannot deploy siege weapons at a portal keep!";
			}

			if (type is eObjectType.SiegeCatapult or eObjectType.SiegeTrebuchet)
			{
				foreach (GameNPC npc in crafter.GetNPCsInRadius(500))
				{
					if (npc is GameSiegeCatapult)
						return "You are too close to another trebuchet or catapult to deploy here!";
				}
			}
			else if (type is eObjectType.SiegeRam)
			{
				int rams = 0;
				foreach (GameNPC npc in crafter.GetNPCsInRadius(200))
				{
					if (npc is GameSiegeRam && npc.Realm == crafter.Realm)
						rams++;
				}

				if (rams >= 2)
					return "Too many rams in this area to deploy another one here!";
			}

			return null;
		}

		/// <summary>
		/// Ram size from its model (mini 2605, light 2600, medium 2601, heavy 2602). LoadFromDatabase copies only the name
		/// and model, so every crafted ram stayed a level 1 light ram.
		/// </summary>
		public static byte RamLevelFromModel(int model)
		{
			return model switch
			{
				2605 => 0,
				2600 => 1,
				2601 => 2,
				2602 => 3,
				_ => 1
			};
		}

		public override void BuildCraftedItem(GamePlayer player, Recipe recipe)
		{
			var product = recipe.Product;
			GameSiegeWeapon siegeweapon;
			switch ((eObjectType)product.Object_Type)
			{
				case eObjectType.SiegeBalista:
					{
						siegeweapon = new GameSiegeBallista();
					}
					break;
				case eObjectType.SiegeCatapult:
					{
						siegeweapon = new GameSiegeCatapult();
					}
					break;
				case eObjectType.SiegeCauldron:
					{
						siegeweapon = new GameSiegeCauldron();
					}
					break;
				case eObjectType.SiegeRam:
					{
						siegeweapon = new GameSiegeRam();
					}
					break;
				case eObjectType.SiegeTrebuchet:
					{
						siegeweapon = new GameSiegeTrebuchet();
					}
					break;
				default:
					{
						base.BuildCraftedItem(player, recipe);
						return;
					}
			}

			//actually stores the Id_nb of the siegeweapon
			siegeweapon.ItemId = product.Id_nb;

			siegeweapon.LoadFromDatabase(product);
			siegeweapon.Level = siegeweapon is GameSiegeRam ? RamLevelFromModel(product.Model) : (byte)3;
			siegeweapon.CurrentRegion = player.CurrentRegion;
			siegeweapon.Heading = player.Heading;
			siegeweapon.X = player.X;
			siegeweapon.Y = player.Y;
			siegeweapon.Z = player.Z;
			siegeweapon.Realm = player.Realm;
			siegeweapon.AddToWorld();
			player.Out.SendMessage($"You deploy the {product.Name}.", eChatType.CT_Missed, eChatLoc.CL_SystemWindow);
		}
	}
}
