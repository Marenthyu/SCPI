using System;
using System.Collections.Generic;
using Assets.Scripts.Unity;
using Assets.Scripts.Unity.UI_New.InGame;
using BloonsTD6_Mod_Helper.Extensions;
using MelonLoader;
using MelonLoader.TinyJSON;
using UnhollowerBaseLib;

namespace SCPI
{
    internal enum RewardType
    {
        HalfHP,
        HalfCash,
        FreeTack,
        SellRandom,
        UpgradeRandom,
        SendRoundAgain,
        DupliacteBloons,
        // Disabled for now as setting these attributes seems to be bugged at the moment.
 /*       FortifyBloons,
        CamoBloons,
        RegrowBloons,
        TenDamage,*/
        HealBloons,
/*        Debug*/
    }

    internal class ChannelPointReward
    {
        // Needs to be consistent in naming with the Twitch API to avoid conversion
        // ReSharper disable once InconsistentNaming
        public readonly string id;
        private static bool _stringsSetup;
        private static bool _actionsSetup;
        private static readonly Dictionary<string, RewardType> IDToRewardTypes = new Dictionary<string, RewardType>();
        private static readonly Dictionary<RewardType, Action> RewardToAction = new Dictionary<RewardType, Action>();

        private static readonly Dictionary<RewardType, string> RewardToDesctiption =
            new Dictionary<RewardType, string>();

        private static readonly Dictionary<RewardType, string> RewardToColorString =
            new Dictionary<RewardType, string>();

        public ChannelPointReward(Variant srcObj)
        {
            id = srcObj["id"];
        }

        public static void SetRewardTypeID(RewardType type, string id)
        {
            if (IDToRewardTypes.ContainsKey(id))
            {
                IDToRewardTypes[id] = type;
            }
            else
            {
                IDToRewardTypes.Add(id, type);
            }
        }

        private static void SetActionForRewardType(RewardType type, Action action)
        {
            RewardToAction[type] = action;
        }

        private static void SetUpStrings()
        {
            _stringsSetup = true;
            
            
            RewardToDesctiption[RewardType.HalfCash] = "Set to half Cash";
            RewardToDesctiption[RewardType.HalfHP] = "Set to half HP";
/*            RewardToDesctiption[RewardType.Debug] = "OwO whats this?";*/
            RewardToDesctiption[RewardType.FreeTack] = "Give a free Tack Shooter";
            RewardToDesctiption[RewardType.SellRandom] = "Sell a random Tower";
            RewardToDesctiption[RewardType.UpgradeRandom] = "Upgrade a random Tower";
            RewardToDesctiption[RewardType.SendRoundAgain] = "Send the current Round... again!";
            RewardToDesctiption[RewardType.DupliacteBloons] = "Duplicate all Bloons on Screen";
/*            RewardToDesctiption[RewardType.FortifyBloons] = "Fortify all Bloons on Screen";
            RewardToDesctiption[RewardType.RegrowBloons] = "Make all Bloons on Screen regrowing";
            RewardToDesctiption[RewardType.CamoBloons] = "Make all Bloons on Screen camo";*/
            RewardToDesctiption[RewardType.HealBloons] = "Heal all Bloons on Screen";
            
            RewardToColorString[RewardType.HalfCash] = "#E3CB02";
            RewardToColorString[RewardType.HalfHP] = "#ff4e00";
/*            RewardToColorString[RewardType.Debug] = "#FFFFFF";*/
            RewardToColorString[RewardType.FreeTack] = "#E3CB02";
            RewardToColorString[RewardType.SellRandom] = "#E33625";
            RewardToColorString[RewardType.UpgradeRandom] = "#34B8EF";
            RewardToColorString[RewardType.SendRoundAgain] = "#87D11E";
            RewardToColorString[RewardType.DupliacteBloons] = "#808080";
/*            RewardToColorString[RewardType.FortifyBloons] = "#1C1717";
            RewardToColorString[RewardType.RegrowBloons] = "#65074C";
            RewardToColorString[RewardType.CamoBloons] = "#2E6808";*/
            RewardToColorString[RewardType.HealBloons] = "#FF005A";

        }

        public static string GetColorFromType(RewardType type)
        {
            if (!_stringsSetup) SetUpStrings();
            return RewardToColorString[type];
        }

        public static string GetDescriptionFromType(RewardType type)
        {
            if (!_stringsSetup) SetUpStrings();
            return RewardToDesctiption[type];
        }

        public RewardType GetRewardType()
        {
            return IDToRewardTypes[id];
        }

        public void Trigger()
        {
            if (!_actionsSetup)
            {
                ActionsSetup();
            }

            Main.RewardActionsToRunInInGameThread.Enqueue(RewardToAction[GetRewardType()]);
        }

        private void ActionsSetup()
        {
            _actionsSetup = true;
            SetActionForRewardType(RewardType.HalfCash, () =>
            {
                double cash = InGame.instance.GetCash();
                InGame.instance.SetCash(Math.Ceiling(cash / 2));
                Game.instance.ShowMessage("Your Cash has been halfed...\nHow unfortunate!",
                    "Twitch Integration Reward Redeemed");
            });
            SetActionForRewardType(RewardType.HalfHP, () =>
            {
                double hp = InGame.instance.GetHealth();
                InGame.instance.SetHealth(Math.Ceiling(hp / 2));
                Game.instance.ShowMessage("Oh No! Your health has been halfed...\nHow unfortunate!",
                    "Twitch Integration Reward Redeemed");
            });
            SetActionForRewardType(RewardType.FreeTack, () =>
            {
                try
                {
                    InGame.instance.GetTowerInventory().freeTowers["TackShooter"] += 1;
                }
                catch (Exception)
                {
                    InGame.instance.GetTowerInventory().freeTowers.Add("TackShooter", 1);
                }

                Game.instance.ShowMessage(
                    "You've been given a free Tack Shooter!\n\n(You may not see it until you place another tower)", 10f,
                    "Twitch Integration Reward Redeemed");
            });
            SetActionForRewardType(RewardType.SellRandom, () =>
            {
                var towers = InGame.instance.bridge.GetAllTowers();
                if (towers.Count > 0)
                {
                    var random = new Random();
                    var selected = towers[random.Next(towers.Count)];
                    Game.instance.ShowMessage(
                        "Your " + selected.GetSimTower().model.name + " got sold!\n\nPress F in Chat", 5f,
                        "Twitch Integration Reward Redeemed");
                    InGame.instance.SellTower(selected);
                }
            });
            SetActionForRewardType(RewardType.UpgradeRandom, () =>
            {
                var towers = InGame.instance.bridge.GetAllTowers();
                if (towers.Count > 0)
                {
                    var random = new Random();
                    var selected = towers[random.Next(towers.Count)];
                    var beforeUpgradeName = selected.GetSimTower().model.name;
                    if (selected.hero == null)
                    {
                        var path = random.Next(3);
                        selected.Upgrade(path, null);
                    }
                    else
                    {
                        selected.hero.PurchaseLevelUp(null);
                    }

                    if (selected.GetSimTower().model.name.Equals(beforeUpgradeName))
                    {
                        Game.instance.ShowMessage(
                            "Your " + beforeUpgradeName + " was tried to be upgraded.\n\nBut that was impossible...", 5f,
                            "Twitch Integration Reward Redeemed");
                    }
                    else
                    {
                        Game.instance.ShowMessage(
                            "Your " + beforeUpgradeName + " was upgraded!\n\nLet's hope that works for you...", 5f,
                            "Twitch Integration Reward Redeemed");
                    }
                    
                    
                }
            });
            
            SetActionForRewardType(RewardType.SendRoundAgain, () =>
            {
                var round = InGame.instance.bridge.GetCurrentRound() + 1;
                InGame.instance.SpawnBloons(round);
                Game.instance.ShowMessage(
                    "Sent out the current round (" + round + ") again!\nLet's see if you're up to the task!", 5f,
                    "Twitch Integration Reward Redeemed");
            });
            
            SetActionForRewardType(RewardType.DupliacteBloons, () =>
            {
                var bloons = InGame.instance.bridge.GetAllBloons();
                foreach (var bloon in bloons)
                {
                    bloon.GetSimBloon().bloonModel.SpawnBloonModel();
                }
                Game.instance.ShowMessage(
                    "All Bloons have been duplicated...\nIn ONE SPOT!\nGood Luck! You'll need it...", 5f,
                    "Twitch Integration Reward Redeemed");
            });
            
            // Disabled for now as setting these attributes seems to be bugged at the moment.
            /*SetActionForRewardType(RewardType.FortifyBloons, () =>
            {
                var bloons = InGame.instance.bridge.GetAllBloons();
                foreach (var bloon in bloons)
                {
                    var simbloon = bloon.GetSimBloon();
                    simbloon.SetFortified(true);
                    simbloon.ResetHealth();
                    
                    MelonLogger.Msg("Is fortified? " + bloon.Def.isFortified); // Even when setting this value directly, the fortification gets stripped after a certain amount of damage.
                }
                Game.instance.ShowMessage(
                    "All Bloons have been fortified!\nShould make it harder :)", 5f,
                    "Twitch Integration Reward Redeemed");
            });
            
            SetActionForRewardType(RewardType.CamoBloons, () =>
            {
                var bloons = InGame.instance.bridge.GetAllBloons();
                foreach (var bloon in bloons)
                {
                    bloon.GetSimBloon().SetCamo(true);
                }
                Game.instance.ShowMessage(
                    "All Bloons have been made camo!\nCan you still see them?", 5f,
                    "Twitch Integration Reward Redeemed");
            });
            
            SetActionForRewardType(RewardType.RegrowBloons, () =>
            {
                var bloons = InGame.instance.bridge.GetAllBloons();
                foreach (var bloon in bloons)
                {
                    bloon.GetSimBloon().SetRegrow(true);
                }
                Game.instance.ShowMessage(
                    "All Bloons are now regrowing!\ndo you have the sustained damage?", 5f,
                    "Twitch Integration Reward Redeemed");
            });
            */
            SetActionForRewardType(RewardType.HealBloons, () =>
            {
                var bloons = InGame.instance.bridge.GetAllBloons();
                foreach (var bloon in bloons)
                {
                    bloon.GetSimBloon().ResetHealth();
                    
                }
                Game.instance.ShowMessage(
                    "All Bloons have been healed!\n#ouch", 5f,
                    "Twitch Integration Reward Redeemed");
            });
            
/*            SetActionForRewardType(RewardType.Debug, () =>
            {
                MelonLogger.Msg("DEBUG:");
                var bloons = InGame.instance.bridge.GetAllBloons();
                foreach (var bloon in bloons)
                {
                    bloon.GetSimBloon().RecieveDamage(10, new Il2CppStringArray(new string[]{}), null, false, false, true, null, false);
                }

            });*/
        }

        public void listAllTowerInventory_Debug()
        {
            var instance = InGame.instance;
            MelonLogger.Msg("Instance: " + instance.ToString());
            var towerinventory = instance.GetTowerInventory();
            MelonLogger.Msg("Tower Inventory: " + towerinventory.ToString());
            var counts = towerinventory.towerCounts;
            MelonLogger.Msg("counts: " + counts.ToString());
            var keys = counts.Keys;
            MelonLogger.Msg("keys: " + keys.ToString());
            foreach (string key in counts.Keys)
            {
                MelonLogger.Msg("Insite foreach");
                var val = counts[key];
                MelonLogger.Msg("Tower " + key + " is present " + val + " times.");
            }
        }
    }
}