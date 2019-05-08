using Newtonsoft.Json;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Wipe Data Cleaner", "2CHEVSKII", "1.0.0")]
    [Description("Cleans specified data files on new wipe.")]
    class WipeDataCleaner : CovalencePlugin
    {
        private class PluginSettings
        {
            [JsonProperty(PropertyName = "Filenames, without .json")]
            public List<string> FileNames { get; set; }
        }

        private PluginSettings Settings { get; set; }
        
        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Settings = new PluginSettings
            {
                FileNames = new List<string>
                {
                    "somefile",
                    "AnotherFile"
                }
            };
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject<PluginSettings>(Settings);

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Settings = Config.ReadObject<PluginSettings>();
                if(Settings == null || Settings.FileNames == null)
                    throw new JsonException();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        private void OnNewSave(string filename) => Wipe(null);
        
        [Command("wipe"), Permission("wipedatacleaner.wipe")]
        private void Wipe(IPlayer executer)
        {
            foreach(var file in Settings.FileNames)
            {
                if(Interface.Oxide.DataFileSystem.ExistsDatafile(file))
                {
                    Interface.Oxide.DataFileSystem.GetFile(file).WriteObject(string.Empty);
                    executer?.Message($"Wiped \"{file}.json\"");
                }
            }
        }
    }
}
