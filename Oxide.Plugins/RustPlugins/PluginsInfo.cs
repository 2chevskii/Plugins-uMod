using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Oxide.Core.Plugins;
//Get plugins commands (unfinished, research in progress)
namespace Oxide.Plugins
{
    [Info("Plugins Info", "2CHEVSKII", "0.1.0")]
    [Description("Shows list of plugins and their commands.")]
    class PluginsInfo : RustPlugin
    {
        List<PluginInfo> Infos { get; set; }
        private static PluginsInfo Instance { get; set; }
        private class PluginInfo
        {
            private Plugin Object { get; }
            public string Name { get; }
            private string[] Commands { get; }
            public PluginInfo(Plugin _object)
            {
                this.Object = _object;
                this.Name = Object.Name;
                this.Commands = Instance.GetCommands(this.Object);
            }

            public string Reply()
            {
                var reply = string.Empty;
                if(Commands == null || Commands.Length == 0)
                {
                    reply = $"No commands available for plugin {Name}";

                }
                else
                {
                    reply = $"{Name} plugin commands:\n";
                    foreach(var command in Commands)
                    {
                        reply += command + "\n";
                    }
                }
                return reply;
            }
        }

        
        void Init()
        {
            Instance = this;
            Infos = new List<PluginInfo>();
            foreach(var plug in GetPlugins())
            {
                Infos.Add(new PluginInfo(plug));
            }
        }


        private IEnumerable<Plugin> GetPlugins() => Manager.GetPlugins();

        

        private string[] GetCommands(Plugin input)
        {
            FieldInfo fieldInfo = typeof(Plugin).GetField("commandInfos", BindingFlags.Instance | BindingFlags.NonPublic);
            var dick = fieldInfo.GetValue(input);
            string[] reply;
            reply = new string[(dick as IDictionary<string, object>).Keys.Count];
            reply = (dick as IDictionary<string, object>).Keys.ToArray();
            if(reply == null || reply.Length == 0)
            {
                reply = new string[1] { $"No commands available for plugin {input.Name}" };
            }
            return reply;
        }

        [ConsoleCommand("pi.get")]
        private void CCmdGetplugins(ConsoleSystem.Arg arg)
        {
            string rep = string.Empty;
            foreach(var pl in Infos)
            {
                rep += pl.Reply() + "\n";
            }
            arg.ReplyWith(rep);
        }
    }
}
