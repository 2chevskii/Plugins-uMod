using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using System;
using UnityEngine;

//07.04.2019
//Plugin is not finished, and won't be finished until I release a custom extension to work with it.
//The problem is that without it I can't access the logs of the server to catch all the console output.

namespace Oxide.Plugins
{
    [Info("AdvancedRCON", "2CHEVSKII", "0.1.0")]
    [Description("Allows users with permission to see server console output")]
    class AdvancedRCON : RustPlugin
    {

        #region -Fields-


        private Configuration configuration;
        private RconData data;


        #endregion

        #region -Configuration and Data-


        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configuration = Config.ReadObject<Configuration>();
                if(configuration == null)
                    throw new JsonException();
            }
            catch
            {
                LoadDefaultConfig();
                SaveConfig();
                throw;
            }
        }

        protected override void LoadDefaultConfig() => configuration = GetDefaultConfiguration();

        protected override void SaveConfig() => Config.WriteObject(configuration, true);

        private void OnServerInitialized()
        {
            if(!Interface.Oxide.DataFileSystem.ExistsDatafile(Name))
            {
                data = GetDefaultRconData();
                Interface.Oxide.DataFileSystem.GetFile(Name).WriteObject(data);
            }
            else
            {
                try
                {
                    data = Interface.Oxide.DataFileSystem.GetFile(Name).ReadObject<RconData>();
                    if(data == null)
                        throw new JsonException();
                }
                catch
                {
                    data = GetDefaultRconData();
                    Interface.Oxide.DataFileSystem.GetFile(Name).WriteObject(data);
                }
            }
        }

        private Configuration GetDefaultConfiguration() => new Configuration
        {
            usePermissions = true,
            permissionUse = "use",
            logPlayerCommands = true,
            notMonitoredCommands = new List<string>
            {
                "serverinfo"
            },
            bannedCommands = new List<string>()
        };

        private RconData GetDefaultRconData() => new RconData
        {
            enabledPlayers = new List<ulong>(),
            commandLog = new Dictionary<ulong, List<string>>()
        };

        #region -Nested classes-


        #region -Config class-


        private class Configuration
        {
            [JsonProperty(PropertyName = "Use plugin permissions? (If set to false plugin functions are only available for admins)")]
            internal bool usePermissions { get; set; }
            [JsonProperty(PropertyName = "Permission to use plugin (advancedrcon.<permission>)")]
            internal string permissionUse { get; set; }
            [JsonProperty(PropertyName = "Log player executed commands")]
            internal bool logPlayerCommands { get; set; }
            [JsonProperty(PropertyName = "Not reflected commands (used to avoid spam from Rcon clients)")]
            internal List<string> notMonitoredCommands { get; set; }
            [JsonProperty("Banned commands (won't be executed when ran from in-game rcon)")]
            internal List<string> bannedCommands { get; set; }

        }


        #endregion

        #region -Data class-


        private class RconData
        {
            [JsonProperty(PropertyName = "Players with reflection enabled")]
            internal List<ulong> enabledPlayers { get; set; }
            [JsonProperty(PropertyName = "Log of player executed commands")]
            internal Dictionary<ulong, List<string>> commandLog { get; set; }
        }


        #endregion


        #endregion


        #endregion

        #region -Localization-


        const string mprefix = "Prefix";
        const string mstarted = "Started console monitor";
        const string mstopped = "Stopped console monitor";
        const string mnocmd = "No command specified";
        const string mnoperms = "No permissions";
        const string mhelpmessage = "Help message";
        const string mfromclient = "Command must be used from client (no formatting supported)";

        private Dictionary<string, string> defmessages = new Dictionary<string, string>
        {
            { mprefix, "<color=lime>[AdvancedRCON]</color>" },
            { mstarted, "Started monitoring console..." },
            { mstopped, "Stopped monitoring console..." },
            { mnocmd, "No command specified, try <color=orange>\"arcon help\"</color> to figure out how to use this plugin!" },
            { mnoperms, "<color=red>You have no access to that command!</color>" },
            { mhelpmessage, "Usage: \n<color=cyan>Console commands</color>\n<color=orange>arcon</color> <color=green>monitor</color> - start or stop monitoring console output\n<color=orange>arcon</color> <color=green><command></color> <color=red><arguments></color> - run command as server and see full output (for example: <color=orange>arcon</color> <color=green>find</color> <color=red>.</color> - will show all the server commands)" },
            { mfromclient, "This command is intended to be used from client!" }
        };

        protected override void LoadDefaultMessages() => lang.RegisterMessages(defmessages, this, "en");


        #endregion

        #region -Hooks-


        private void Loaded()
        {
            PermissionConverter();
            permission.RegisterPermission(configuration.permissionUse, this);
        }

        private void Unload() => Interface.Oxide.DataFileSystem.GetFile(Name).WriteObject(data);

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            if(arg == null || arg?.cmd == null) { return null; }
            foreach(var bannedcmd in configuration.bannedCommands)
                if(bannedcmd.ToLower() == arg.cmd.Name.ToLower() && (!arg.IsServerside || !arg.IsRcon)) { return false; }
            if(configuration.notMonitoredCommands.Contains(arg.cmd.Name)) { return null; }

            string _fullcommand = string.Empty;
            _fullcommand += arg.cmd.FullName;
            if(arg.Args != null)
                foreach(var argument in arg.Args)
                    _fullcommand += " " + argument;

            if((!arg.IsServerside || !arg.IsRcon) && configuration.logPlayerCommands)
            {
                if(data.commandLog.ContainsKey(arg.Connection.ownerid))
                    data.commandLog[arg.Connection.ownerid].Add(_fullcommand);
                else data.commandLog.Add(arg.Connection.ownerid, new List<string> { _fullcommand });
                Interface.Oxide.DataFileSystem.GetFile(Name).WriteObject(data);
            }

            foreach(var monitored in data.enabledPlayers)
            {
                if(BasePlayer.Find(monitored.ToString()) is BasePlayer)
                {
                    if((configuration.usePermissions && !CheckPermission(monitored.ToString())) || (!configuration.usePermissions && !BasePlayer.Find(monitored.ToString()).IsAdmin)) continue;
                    NextTick(() =>
                    {
                        BasePlayer.Find(monitored.ToString()).ConsoleMessage($"{(arg?.Connection?.ownerid != monitored ? _fullcommand : string.Empty)}\n{(arg?.Connection?.ownerid != monitored ? arg.Reply : string.Empty)}");
                    });
                }
            }

            return null;
        }


        #endregion

        #region -Commands-


        [ConsoleCommand("arcon")]
        private void CCmdArcon(ConsoleSystem.Arg arg)
        {
            if(arg.Args == null || arg.Args.Length < 1)
            {
                if(arg.Connection != null)
                    arg.ReplyWith(lang.GetMessage(mprefix, this, arg.Connection?.ownerid.ToString()) + " " + lang.GetMessage(mnocmd, this, arg.Connection?.ownerid.ToString()));
                else
                    arg.ReplyWith(lang.GetMessage(mprefix, this) + " " + lang.GetMessage(mfromclient, this));
                return;
            }
            else
            {
                var command = arg.Args[0];
                if(command.ToLower() == "help")
                {
                    if(arg.Connection != null)
                        arg.ReplyWith(lang.GetMessage(mprefix, this, arg.Connection?.ownerid.ToString()) + " " + lang.GetMessage(mhelpmessage, this, arg.Connection?.ownerid.ToString()));
                    else
                        arg.ReplyWith(lang.GetMessage(mprefix, this) + " " + lang.GetMessage(mfromclient, this));
                    return;
                }
                if(command.ToLower() == "monitor") { StartOrEndMonitor(arg); return; }
                var arguments = new string[arg.Args.Length - 1];
                for(int i = 0; i < arg.Args.Length; i++)
                {
                    if(i == 0) continue;
                    arguments[i - 1] = arg.Args[i];
                }
                Server.Command(command, arguments);
            }
        }


        #endregion

        #region -Helpers-


        private bool CheckPermission(string userIdString)
        {
            if(!configuration.usePermissions) { return true; }
            else
            {
                if(permission.UserHasPermission(userIdString, configuration.permissionUse)) { return true; }
                else { return false; }
            }
        }

        private void PermissionConverter()
        {
            if(configuration.permissionUse == null || configuration.permissionUse == "" || configuration.permissionUse == string.Empty) { configuration.permissionUse = "use"; SaveConfig(); }
            configuration.permissionUse = Name.ToLower() + "." + configuration.permissionUse;
        }

        private void OnUserPermissionRevoked(string id, string permName) { if(permName == configuration.permissionUse && data.enabledPlayers.Contains(Convert.ToUInt64(id))) data.enabledPlayers.Remove(Convert.ToUInt64(id)); }

        private void StartOrEndMonitor(ConsoleSystem.Arg console_arg)
        {
            if(console_arg == null || console_arg.Connection == null) return;

            if((configuration.usePermissions && !CheckPermission(console_arg.Connection.ownerid.ToString())) || (!configuration.usePermissions && console_arg.Player() != null && !console_arg.Player().IsAdmin)) { console_arg.ReplyWith(lang.GetMessage(mprefix, this, console_arg.Connection.ownerid.ToString()) + " " + lang.GetMessage(mnoperms, this, console_arg.Connection.ownerid.ToString())); return; }

            if(data.enabledPlayers.Contains(console_arg.Connection.ownerid))
            {
                data.enabledPlayers.Remove(console_arg.Connection.ownerid);
                console_arg.ReplyWith(lang.GetMessage(mprefix, this, console_arg.Connection.ownerid.ToString()) + " " + lang.GetMessage(mstopped, this, console_arg.Connection.ownerid.ToString()));
            }
            else
            {
                data.enabledPlayers.Add(console_arg.Connection.ownerid);
                console_arg.ReplyWith(lang.GetMessage(mprefix, this, console_arg.Connection.ownerid.ToString()) + " " + lang.GetMessage(mstarted, this, console_arg.Connection.ownerid.ToString()));
            }
        }


        #endregion

    }
}