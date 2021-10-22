using System;
using System.Collections.Generic;
using System.Linq;

using Facepunch;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Wipe Data Cleaner", "2CHEVSKII", "1.4.0")]
    [Description("Cleans specified data files on new wipe.")]
    class WipeDataCleaner : CovalencePlugin
    {
        const string PERMISSIONUSE = "wipedatacleaner.wipe";

        OxideMod       Oxide = Interface.Oxide;
        PluginSettings settings;

        #region Oxide hooks

        void Init()
        {
            permission.RegisterPermission(PERMISSIONUSE, this);

            AddCovalenceCommand(settings.Command ?? "wipe", "Wipe", PERMISSIONUSE);
        }

        void OnNewSave(string filename) => Wipe(null);

        #endregion

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            settings = new PluginSettings {
                FileNames = new List<string> {
                    "somefile",
                    "AnotherFile"
                },
                Command = "wipe"
            };
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                settings = Config.ReadObject<PluginSettings>();

                if (settings == null || settings.FileNames == null)
                    throw new Exception("Configuration contains null value");

                SaveConfig();
            }
            catch (Exception e)
            {
                LogError("Failed to load configuration: {0}", e.Message);
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        #region Core

        void Wipe(IPlayer executer)
        {
            if (settings.EnableLogs)
            {
                if (executer != null && !executer.IsServer)
                {
                    Log("Wipe started by {0}", executer.Name);
                }
                else
                {
                    Log("Wipe started");
                }

                LogWarning("Be careful, wipe will reload all the plugins!");
            }

            HashSet<string> filesToWipe = DetermineFilesToWipe();

            if (settings.EnableLogs)
            {
                Log("Data files ready to wipe:\n{0}", JsonConvert.SerializeObject(filesToWipe, Formatting.Indented));
            }

            List<string> ignoreList = Pool.GetList<string>();

            ignoreList.Add(nameof(WipeDataCleaner));

            Oxide.UnloadAllPlugins(ignoreList);

            Pool.FreeList(ref ignoreList);

            foreach (string file in filesToWipe)
            {
                var message = WipeFile(file);

                if (settings.EnableLogs)
                {
                    Log(message);
                } else if (executer != null && !executer.IsServer)
                {
                    executer.Message(message);
                }
            }

            Oxide.LoadAllPlugins(false);

            if (settings.EnableLogs)
            {
                Log("Wipe completed");
            }
        }

        HashSet<string> DetermineFilesToWipe()
        {
            HashSet<string> files = new HashSet<string>();

            for (int i = 0; i < settings.FileNames.Count; i++)
            {
                string fileName = settings.FileNames[i];

                if (fileName == null || string.IsNullOrWhiteSpace(fileName))
                {
                    LogWarning("Configuration contains invalid filename!");
                    continue;
                }

                if (fileName.EndsWith("/"))
                {
                    string[] matchingFiles = SearchDirectory(fileName.Remove(fileName.Length - 1));

                    for (int j = 0; j < matchingFiles.Length; j++)
                    {
                        files.Add(SanitizeFileName(matchingFiles[j]));
                    }
                }
                else if (fileName.EndsWith("/*"))
                {
                    string[] matchingFiles = SearchDirectory(fileName.Remove(fileName.Length - 2));

                    for (int j = 0; j < matchingFiles.Length; j++)
                    {
                        files.Add(SanitizeFileName(matchingFiles[j]));
                    }
                }
                else
                {
                    string fn = fileName.EndsWith(".json") ? fileName : fileName + ".json";

                    string matchingFile = SearchFile(fn);

                    if (matchingFile != null)
                    {
                        files.Add(SanitizeFileName(matchingFile));
                    }
                }
            }

            return files;
        }

        string SanitizeFileName(string fileName)
        {
            return fileName.Substring(Oxide.DataDirectory.Length + 1).Replace("\\", "/");
        }

        string SearchFile(string name)
        {
            return Oxide.DataFileSystem.GetFiles(searchPattern: name).FirstOrDefault();
        }

        string[] SearchDirectory(string name)
        {
            return Oxide.DataFileSystem.GetFiles(name, "*");
        }

        string WipeFile(string file)
        {
            if (file.EndsWith(".json"))
            {
                file = file.Substring(0, file.Length - 5);
            }

            if (Interface.Oxide.DataFileSystem.ExistsDatafile(file))
            {
                DynamicConfigFile dataFile = Interface.Oxide.DataFileSystem.GetFile(file);

                dataFile.Clear();
                dataFile.Save();

                return $"Wiped '{file}.json'";
            }

            return $"Could not find '{file}.json'";
        }

        #endregion

        #region Configuration class

        class PluginSettings
        {
            [JsonProperty("Filenames, without .json")]
            public List<string> FileNames { get; set; }

            [JsonProperty("Command (default: 'wipe')")]
            public string Command { get; set; }

            [JsonProperty("Enable logs", DefaultValueHandling = DefaultValueHandling.Populate)]
            public bool EnableLogs { get; set; }
        }

        #endregion
    }
}
