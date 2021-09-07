using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("uMod Version Checker", "2CHEVSKII", "0.1.0")] // Rename to Oxide-version-checker
    [Description("Utility methods for getting uMod versions")]
    public class UModVersionChecker : CovalencePlugin
    {
        const string API_BASE_URL = "https://umod-versioner.herokuapp.com/";

        float                             updateFrequency;
        List<string>                      gamesAvailable;
        Dictionary<string, VersionNumber> gameVersions;
        bool                              isUpdatingLists;
        bool                              enableLog;
        Timer                             listUpdateTimer;
        Dictionary<string, VersionNumber> extensionVersions;

        event Action OnListsUpdated;

        #region Oxide hooks

        void OnServerInitialized()
        {
            var exts = Interface.Oxide.GetAllExtensions().Where(ext => ext.IsCoreExtension || ext.IsGameExtension);

            extensionVersions = new Dictionary<string, VersionNumber>();

            foreach (var ext in exts)
            {
                extensionVersions[ext.Name] = ext.Version;
            }

            Log("Update frequency was set to {0} min", updateFrequency);

            UpdateLists();
        }

        void Unload()
        {
            if (listUpdateTimer != null && !listUpdateTimer.Destroyed)
            {
                timer.Destroy(ref listUpdateTimer);
            }
        }

        #endregion

        #region Utility

        void UpdateLists()
        {
            isUpdatingLists = true;
            if (gamesAvailable == null)
                gamesAvailable = new List<string>();

            Log("Fetching game list...");
            FetchGameList(
                gamesAvailable,
                b => {
                    if (b)
                    {
                        Log("Game list fetched, total of {0} games loaded", gamesAvailable.Count);
                        int successfullGames = 0;
                        int failedGames = 0;

                        if (gameVersions == null)
                        {
                            gameVersions = new Dictionary<string, VersionNumber>();
                        }

                        foreach (string game in gamesAvailable)
                        {
                            FetchGameVersion(
                                game,
                                (s, v) => {
                                    if (s)
                                    {
                                        gameVersions[game] = v;
                                        successfullGames++;
                                        Log("Successfully loaded versions of {0}/{1} games", successfullGames, gamesAvailable.Count);
                                    }
                                    else
                                    {
                                        failedGames++;
                                        Log("Failed to get vesion for game {0}", game);
                                    }

                                    if (successfullGames + failedGames == gamesAvailable.Count)
                                    {
                                        Log(
                                            "Finished loading game versions: {0} of {1} games were loaded successfully",
                                            successfullGames,
                                            gamesAvailable.Count
                                        );
                                    }
                                }
                            );
                        }
                    }
                    else
                    {
                        LogWarning("Failed to fetch game list");
                    }

                    isUpdatingLists = false;

                    if (OnListsUpdated != null)
                    {
                        OnListsUpdated.Invoke();

                        OnListsUpdated = null;
                    }

                    if (listUpdateTimer != null && !listUpdateTimer.Destroyed)
                        timer.Destroy(ref listUpdateTimer);
                    listUpdateTimer = timer.Once(updateFrequency * 60f, UpdateLists);
                }
            );
        }

        string Slugify(string str)
        {
            return str.ToLower().Replace(" ", "-");
        }

        #endregion

        #region Plugin API

        [HookMethod("GetExtensionVersion")]
        public VersionNumber GetExtensionVersion(string name, bool @throw = false)
        {
            if (extensionVersions.ContainsKey(name))
            {
                return extensionVersions[name];
            }

            if (!@throw)
            {
                return default(VersionNumber);
            }

            throw new InvalidOperationException($"Extension '{name}' is not loaded");
        }

        [HookMethod("GetGameList")]
        public void GetGameList(Action<IReadOnlyCollection<string>> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (isUpdatingLists)
            {
                OnListsUpdated += () => GetGameList(callback);
            }
            else
            {
                callback(gamesAvailable.AsReadOnly());
            }
        }

        [HookMethod("GetLatestOxideVersion")]
        public void GetLatestOxideVersion(string game, Action<bool, VersionNumber> callback)
        {
            if (string.IsNullOrWhiteSpace(game))
            {
                throw new ArgumentNullException(nameof(game));
            }

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (isUpdatingLists)
            {
                OnListsUpdated += () => GetLatestOxideVersion(game, callback);
            }
            else
            {
                string slug = Slugify(game);

                if (gameVersions.ContainsKey(slug))
                {
                    callback(true, gameVersions[slug]);
                }
                else
                {
                    callback(false, default(VersionNumber));
                }
            }
        }

        #endregion

        #region WebAPI interaction

        void FetchGameList(List<string> list, Action<bool> callback)
        {
            webrequest.Enqueue(
                API_BASE_URL + "games",
                null,
                (code, json) => {
                    if (code != 200)
                    {
                        callback(false);
                    }
                    else
                    {
                        var data = JsonConvert.DeserializeObject<IEnumerable<string>>(json);

                        if (data == null)
                        {
                            callback(false);
                        }
                        else
                        {
                            list.Clear();
                            list.AddRange(data);
                            callback(true);
                        }
                    }
                },
                this
            );
        }

        void FetchGameVersion(string game, Action<bool, VersionNumber> callback)
        {
            webrequest.Enqueue(
                API_BASE_URL + game,
                null,
                (code, json) => {
                    if (code != 200)
                    {
                        callback(false, default(VersionNumber));
                    }
                    else
                    {
                        var obj = JSON.Object.Parse(json);

                        if (!obj.GetBoolean("success"))
                        {
                            callback(false, default(VersionNumber));
                        }
                        else
                        {
                            var version = obj.GetObject("version");
                            var major = version.GetInt("major");
                            var minor = version.GetInt("minor");
                            var patch = version.GetInt("patch");

                            callback(true, new VersionNumber(major, minor, patch));
                        }
                    }
                },
                this
            );
        }

        new void Log(string format, params object[] args)
        {
            if (enableLog)
            {
                base.Log(format, args);
            }
        }

        #endregion

        #region Configuration

        T GetConfigValue<T>(string key, T @default = default(T))
        {
            object obj = Config[key];

            if (obj is T)
            {
                return (T)obj;
            }

            try
            {
                return (T)Convert.ChangeType(obj, typeof(T));
            }
            catch
            {
                Config[key] = @default;
                Config.Save();
                return @default;
            }
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Config["Update frequency (minutes)"] = 10f;
            Config["Enable logging"] = false;
            Config.Save();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            updateFrequency = GetConfigValue("Update frequency (minutes)", 10f);
            enableLog = GetConfigValue("Enable logging", false);
        }

        #endregion
    }
}
