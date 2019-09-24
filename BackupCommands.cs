﻿using NLog;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game.ModAPI;

namespace ALE_GridBackup {

    [Category("gridbackup")]
    public class TestCommands : CommandModule {

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public GridBackupPlugin Plugin => (GridBackupPlugin) Context.Plugin;

        [Command("list", "Lists all Backups for the given player and/or grid.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void List(string playernameOrSteamId, string gridNameOrEntityId = null) {

            MyIdentity player = PlayerUtils.GetIdentityByNameOrId(playernameOrSteamId);

            if (player == null) {
                Context.Respond("Player not found!");
                return;
            }

            string path = Plugin.CreatePath();
            path = Plugin.CreatePathForPlayer(path, player.IdentityId);

            DirectoryInfo gridDir = new DirectoryInfo(path);
            DirectoryInfo[] dirList = gridDir.GetDirectories("*", SearchOption.TopDirectoryOnly);

            StringBuilder sb = new StringBuilder();

            string gridname = null;

            if (gridNameOrEntityId == null) {

                foreach (var file in dirList)
                    sb.AppendLine(file.Name);

            } else {

                string folder = FindFolderName(dirList, gridNameOrEntityId);

                gridname = folder;

                if (gridname == null) {
                    Context.Respond("Grid not found!");
                    return;
                }

                path = Path.Combine(path, folder);
                gridDir = new DirectoryInfo(path);
                FileInfo[] fileList = gridDir.GetFiles("*.*", SearchOption.TopDirectoryOnly);

                var query = fileList.OrderByDescending(file => file.CreationTime);

                int i = 1;
                foreach (var file in query)
                    sb.AppendLine((i++) +"      "+file.Name+" "+(file.Length/1024.0).ToString("#,##0.00")+" kb");
            }

            if (Context.Player == null) {

                Context.Respond($"Backed up Grids for Player {player.DisplayName}");

                if(gridname != null)
                    Context.Respond($"Grid {gridname}");

                Context.Respond(sb.ToString());

            } else {

                if (gridname != null)
                    ModCommunication.SendMessageTo(new DialogMessage("Backed up Grids", $"Grid {gridname}", sb.ToString()), Context.Player.SteamUserId);
                else
                    ModCommunication.SendMessageTo(new DialogMessage("Backed up Grids", $"Player {player.DisplayName}", sb.ToString()), Context.Player.SteamUserId);
            }
        }

        public string FindFolderName(DirectoryInfo[] dirList, string gridNameOrEntityId) {

            foreach (var file in dirList) {

                var name = file.Name;
                var lastIndex = name.LastIndexOf("_");

                string gridName = name.Substring(0, lastIndex);
                string entityId = name.Substring(lastIndex + 1, name.Length - (lastIndex + 1));

                Log.Info(name);
                Log.Info(gridName);
                Log.Info(entityId);

                if (entityId == gridNameOrEntityId)
                    return name;

                if (Regex.IsMatch(gridName, WildCardToRegular(gridNameOrEntityId))) 
                    return name;
            }

            return null;
        }

        private static string WildCardToRegular(string value) {
            return "^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        }

        [Command("restore", "Restores the given grid from the backups.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Restore(string playernameOrSteamId, string gridNameOrEntityId, int backupNumber = 1, bool keepOriginalPosition = false) {

            MyIdentity player = PlayerUtils.GetIdentityByNameOrId(playernameOrSteamId);

            if (player == null) {
                Context.Respond("Player not found!");
                return;
            }
        }

        [Command("save", "Saves the grid defined by name, or you are looking at manually.")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void Save(string gridNameOrEntityId = null) {

            MyCharacter character = null;

            if (gridNameOrEntityId == null) {

                if (Context.Player == null) {
                    Context.Respond("You need to enter a Grid name where the grid will be spawned at.");
                    return;
                }

                var player = ((MyPlayer)Context.Player).Identity;

                if (player.Character == null) {
                    Context.Respond("Player has no character to spawn the grid close to!");
                    return;
                }

                character = player.Character;
            }

            List<MyCubeGrid> grids = GridFinder.FindGridList(gridNameOrEntityId, character, Plugin.Config.BackupConnections);

            if (grids == null) {
                Context.Respond("Multiple grids found. Try to rename them first or try a different subgrid for identification!");
                return;
            }

            if (grids.Count == 0) {
                Context.Respond("No grids found. Check your viewing angle or try the correct name!");
                return;
            }

            MyCubeGrid biggestGrid = null;

            foreach (var grid in grids)
                if (biggestGrid == null || biggestGrid.BlocksCount < grid.BlocksCount)
                    biggestGrid = grid;

            /* No biggest grid should not be possible, unless the gridgroup only had projections -.- just skip it. */
            if (biggestGrid == null)
                Context.Respond("Grid incompatible!");

            /* No owner at all? hard to believe. but okay skip it. */
            if (biggestGrid.BigOwners.Count == 0)
                Context.Respond("Grid has no ownership!");

            long playerId = biggestGrid.BigOwners[0];

            if (BackupQueue.BackupSignleGridStatic(playerId, grids, Plugin.CreatePath(), null, Plugin, false))
                Context.Respond("Export Complete!");
            else
                Context.Respond("Export Failed!");
        }

        [Command("run", "Starts the Backup task manually.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Run() {

            try {

                if(Plugin.StartBackupManually())
                    Context.Respond("Backup creation started!");
                else
                    Context.Respond("Backup already running!");

            } catch(Exception e) {
                Log.Error(e, "Error while starting Backup");
            }
        }
    }
}
