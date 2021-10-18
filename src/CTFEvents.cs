#define DEBUG
#define UNITY_ASSERTIONS
#define PRE_RELEASE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Facepunch;

using Network;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

using UnityEngine;
using UnityEngine.Assertions;

using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("CTFEvents", "2CHEVSKII", "0.3.0-alpha.1")]
    [Description("Adds 'capture the flag' type events to your server")]
    class CTFEvents : CovalencePlugin
    {
        const string PERMISSION_MANAGE     = "ctfevents.manage";
        const string PREFAB_MARKER_GENERIC = "assets/prefabs/tools/map/genericradiusmarker.prefab";

        const string M_PREFIX                  = "Chat prefix",
                     M_NO_PERMISSION           = "No permission",
                     M_WRONG_USAGE             = "Wrong usage",
                     M_HELP                    = "Help",
                     M_EVENT_INFO              = "Event info",
                     M_EVENT_NOT_FOUND         = "Event not found",
                     M_NO_EVENTS               = "No events running",
                     M_EVENT_STARTED           = "Event started",
                     M_EVENT_STARTED_BY        = "Event started by player/plugin",
                     M_EVENT_FINISHED_TIME     = "Event time expired",
                     M_EVENT_FINISHED_FORCE    = "Event finished forcefully",
                     M_EVENT_FINISHED_CAPTURED = "Event finished by capturing flag",
                     M_INVALID_POS             = "Invalid position for event",
                     M_EVENT_EXISTS            = "Event already exists",
                     M_EVENT_LIMIT             = "Event limit reached",
                     M_EVENT_TICK_CAPTURING    = "Event tick capture",
                     M_EVENT_TICK_CONTESTED    = "Event tick contested";

        const string COLOR_CLOSE   = "</color>",
                     COLOR_DEFOCUS = "#a2ad9c",
                     COLOR_SUCCESS = "#72c93c",
                     COLOR_INFO    = "#309bd9",
                     COLOR_WARN    = "#f5cd2c",
                     COLOR_DANGER  = "#ff5526";

        static CTFEvents Instance;

        PluginSettings    settings;
        int               maxEventsPerMap;
        readonly string[] hereSynonyms = { "here", "there", "sight", "place", "point", "atme" };

        [Conditional("DEBUG")]
        static void LogDebug(string format, params object[] args)
        {
            Interface.Oxide.LogDebug("[CTFEvents] " + format, args);
        }

        #region Command handlers

        void CommandHandler(IPlayer player, string _, string[] args)
        {
            if (args.Length == 0)
            {
                InfoCommand(player, null);
            }
            else
            {
                string arg = args.Skip(1).FirstOrDefault();

                switch (args[0])
                {
                    case "info":
                        InfoCommand(player, arg);
                        break;

                    case "start":
                        StartCommand(player, arg);
                        break;

                    case "end":
                        EndCommand(player, arg);
                        break;

                    case "help":
                        Message(player, M_HELP);
                        break;

                    default:
                        Message(player, M_WRONG_USAGE);
                        break;
                }
            }
        }

        void StartCommand(IPlayer player, string arg)
        {
            if (!player.HasPermission(PERMISSION_MANAGE))
            {
                Message(player, M_NO_PERMISSION);
            }
            else if (arg == null)
            {
                Vector3 pos = GetRandomPos();

                if (pos == Vector3.zero)
                {
                    Message(player, M_EVENT_LIMIT);
                }
                else
                {
                    Flag.CreateNewEvent(pos, player);
                }
            }
            else if (hereSynonyms.Contains(arg.ToLower()))
            {
                if (Flag.IsEventLimitReached)
                {
                    Message(player, M_EVENT_LIMIT);
                }
                else
                {
                    Vector3 pos = GetViewPos(player);

                    if (pos == Vector3.zero || !CanSpawnEvent(pos))
                    {
                        Message(player, M_INVALID_POS);
                    }
                    else if (Flag.FindByGridCell(PhoneController.PositionToGridCoord(pos)) != null)
                    {
                        Message(player, M_EVENT_EXISTS);
                    }
                    else
                    {
                        Flag.CreateNewEvent(pos, player);
                    }
                }
            }
            else
            {
                Message(player, M_WRONG_USAGE);
            }
        }

        void EndCommand(IPlayer player, string arg)
        {
            if (!player.HasPermission(PERMISSION_MANAGE))
            {
                Message(player, M_NO_PERMISSION);
            }
            else if (Flag.EventCount == 0)
            {
                Message(player, M_NO_EVENTS);
            }
            else if (arg == null)
            {
                if (Flag.EventCount == 1)
                {
                    Flag.FindById(1).ForceFinish(player);
                }
                else
                {
                    Message(player, M_WRONG_USAGE);
                }
            }
            else
            {
                int id;
                Flag flag;
                if (int.TryParse(arg, out id))
                {
                    flag = Flag.FindById(id);
                }
                else
                {
                    flag = Flag.FindByGridCell(arg);
                }

                if (!flag)
                {
                    Message(player, M_EVENT_NOT_FOUND);
                }
                else
                {
                    flag.ForceFinish(player);
                }
            }
        }

        void InfoCommand(IPlayer player, string arg)
        {
            int id;
            if (arg == null)
            {
                Flag[] allFlags = Flag.GetAll();

                if (allFlags.Length == 0)
                {
                    Message(player, M_NO_EVENTS);
                }
                else
                {
                    StringBuilder builder = Pool.Get<StringBuilder>();

                    for (int i = 0; i < allFlags.Length; i++)
                    {
                        Flag flag = allFlags[i];

                        string info = GetFlagInfo(player, flag);

                        builder.AppendLine(info);
                    }

                    MessageRaw(player, builder.ToString(), true);

                    builder.Clear();
                    Pool.Free(ref builder);
                }
            }
            else if (int.TryParse(arg, out id))
            {
                Flag flag = Flag.FindById(id);

                if (!flag)
                {
                    Message(player, M_EVENT_NOT_FOUND);
                }
                else
                {
                    MessageRaw(player, GetFlagInfo(player, flag));
                }
            }
            else
            {
                Flag flag = Flag.FindByGridCell(arg);

                if (!flag)
                {
                    Message(player, M_EVENT_NOT_FOUND);
                }
                else
                {
                    MessageRaw(player, GetFlagInfo(player, flag));
                }
            }
        }

        #endregion

        #region Utility

        Vector3 GetRandomPos()
        {
            if (Flag.IsEventLimitReached)
            {
                return Vector3.zero;
            }

            Vector3 position;
            string gridCell;
            var filter = SpawnHandler.Instance.AllSpawnPopulations.GetRandom().Filter;

            do
            {
                var v2 = Random.insideUnitCircle * (TerrainMeta.Size.x / 2);
                position = new Vector3(v2.x, 0, v2.y);
                gridCell = PhoneController.PositionToGridCoord(position);
            } while (!filter.Test(position) || Flag.FindByGridCell(gridCell));

            return position;
        }

        bool CanSpawnEvent(Vector3 pos)
        {
            var heightRaw = TerrainMeta.HeightMap.GetHeight(pos);

            if (heightRaw <= TerrainMeta.WaterMap.GetHeight(pos))
            {
                return false;
            }

            RaycastHit hit;
            if (Physics.Raycast(
                pos,
                Vector3.down,
                out hit,
                float.PositiveInfinity,
                LayerMask.GetMask("Construction")
            ))
            {
                return false;
            }

            return true;
        }

        Vector3 GetViewPos(IPlayer player)
        {
            var basePlayer = (BasePlayer)player.Object;

            RaycastHit hit;

            if (!Physics.Raycast(basePlayer.eyes.HeadRay(), out hit, 100f, LayerMask.GetMask("Terrain")))
            {
                return Vector3.zero;
            }

            return hit.point;
        }

        #endregion

        #region Oxide hooks

        void Init()
        {
            Instance = this;

            permission.RegisterPermission(PERMISSION_MANAGE, this);
            AddCovalenceCommand("ctf", nameof(CommandHandler));

            LogDebug("Debug messages enabled");

#if PRE_RELEASE
            LogDebug("This is an unstable version of the plugin ({0}). It contains debug messages, assertions and other stuff which affects not only logs, but also performance. It is not recommended to use this version on a production server", "0.3.0-alpha.1");
#endif
        }

        void OnServerInitialized()
        {
            maxEventsPerMap = Mathf.FloorToInt((float)Math.Pow(TerrainMeta.Size.x / 250, 2));

            LogDebug("Max events per map: {0}", maxEventsPerMap);

            if (settings.EventLimit > maxEventsPerMap)
            {
                LogWarning(
                    "Configured event limit exceeds maximum event count dictated by the map size. The latter will be used instead"
                );
            }

            Flag.Initialize();

            if (settings.EventAutoCreateFrequency > 0f)
            {
                timer.Every(
                    settings.EventAutoCreateFrequency,
                    () => {
                        int c = Flag.EventCount;

                        if (c != 0 && !settings.AutoCreateIfAnyExists)
                        {
                            return;
                        }

                        if (Flag.IsEventLimitReached)
                        {
                            return;
                        }

                        Flag.CreateNewEvent(GetRandomPos());
                    }
                );
            }
        }

        void OnUserConnected(IPlayer player)
        {
            Flag.OnPlayerConnected((BasePlayer)player.Object);
        }

        void Unload()
        {
            Flag.Unload();

            Instance = null;
        }

        #endregion

        #region External API calls

        void OnEventStarted(Flag flag)
        {
            if (Interface.Oxide.CallHook("OnCtfEventStarted", flag.GridCell) == null)
            {
                Announce(M_EVENT_STARTED, flag.GridCell);
            }
        }

        void OnEventStarted(Flag flag, IPlayer manager)
        {
            if (Interface.Oxide.CallHook("OnCtfEventStarted", flag.GridCell, manager) == null)
            {
                Announce(M_EVENT_STARTED_BY, flag.GridCell, manager.Name);
            }
        }

        void OnEventStarted(Flag flag, Plugin caller)
        {
            if (Interface.Oxide.CallHook("OnCtfEventStarted", flag.GridCell, caller) == null)
            {
                Announce(M_EVENT_STARTED_BY, flag.GridCell, caller.Name);
            }
        }

        void OnEventFinished(Flag flag)
        {
            if (Interface.Oxide.CallHook("OnCtfEventFinished", flag.GridCell) == null)
            {
                Announce(M_EVENT_FINISHED_TIME, flag.GridCell);
            }
        }

        void OnEventFinished(Flag flag, BasePlayer winner)
        {
            if (Interface.Oxide.CallHook("OnCtfEventFinished", flag.GridCell, winner.displayName) == null)
            {
                Announce(M_EVENT_FINISHED_CAPTURED, flag.GridCell, WrapInColor(winner.displayName, COLOR_INFO));
            }
        }

        void OnEventFinished(Flag flag, List<BasePlayer> winners)
        {
            if (Interface.Oxide.CallHook("OnCtfEventFinished", flag.GridCell, winners) == null)
            {
                Announce(
                    M_EVENT_FINISHED_CAPTURED,
                    flag.GridCell,
                    string.Join(", ", winners.Select(w => WrapInColor(w.displayName, COLOR_INFO)))
                );
            }
        }

        void OnEventFinished(Flag flag, IPlayer manager)
        {
            if (Interface.Oxide.CallHook("OnCtfEventFinished", flag.GridCell, manager) == null)
            {
                Announce(M_EVENT_FINISHED_FORCE, flag.GridCell, manager.Name);
                LogDebug("Event {0} finished by player {1}", flag.GridCell, manager);
            }
        }

        void OnEventFinished(Flag flag, Plugin caller)
        {
            if (Interface.Oxide.CallHook("OnCtfEventFinished", flag.GridCell, caller) == null)
            {
                Announce(M_EVENT_FINISHED_FORCE, flag.GridCell, caller.Name);
            }

            LogDebug("Event {0} finished by plugin {1}", flag.GridCell, caller.Name);
        }

        void OnEventTick(Flag flag)
        {
            Interface.Oxide.CallHook(
                "OnCtfEventTick",
                flag.GridCell,
                flag.State,
                flag.TimeLeft,
                flag.CaptureProgress
            );

            if (flag.State == Flag.EventState.Stale)
            {
                return;
            }

            List<BasePlayer> list = Pool.GetList<BasePlayer>();

            flag.GetAlivePlayers(list);

            for (int i = 0; i < list.Count; i++)
            {
                BasePlayer player = list[i];

                if (!flag.ShouldNotify(player.IPlayer))
                {
                    continue;
                }

                if (flag.State == Flag.EventState.Capturing)
                {
                    Message(player.IPlayer, M_EVENT_TICK_CAPTURING, flag.GridCell, flag.CaptureTimeLeft);
                }
                else
                {
                    Message(player.IPlayer, M_EVENT_TICK_CONTESTED, flag.GridCell, list.Count - flag.CapturerCount);
                }
            }

            Pool.FreeList(ref list);
        }

        void OnEventStateChange(Flag flag, Flag.EventState prevState, Flag.EventState newState)
        {
            Interface.Oxide.CallHook("OnCtfEventStateChange", flag.GridCell, prevState, newState);
        }

        #endregion

        #region Public API

        [HookMethod("CTFEvents::GetInGridCell")]
        public Flag GetEventInGridCell(string gridCell)
        {
            return Flag.FindByGridCell(gridCell);
        }

        #endregion

        #region LangAPI

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string> {
                    [M_PREFIX] = "[CTF] ",
                    [M_NO_PERMISSION] = WrapInColor("You have no access to this command", COLOR_DANGER),
                    [M_WRONG_USAGE] = WrapInColor(
                        $"Syntax error. Use {WrapInColor("/ctf help", COLOR_INFO)} to get help",
                        COLOR_WARN
                    ),
                    [M_HELP] =
                        $"Usage: {WrapInColor("/ctf", COLOR_INFO)} [{WrapInColor("command", COLOR_SUCCESS)}] <{WrapInColor("arguments", COLOR_DEFOCUS)}>\n" +
                        "Commands:\n" +
                        $"no command | {WrapInColor("info", COLOR_SUCCESS)} - Shows info about all current events. Can be provided with an id or grid cell name to get info about specific event\n" +
                        $"{WrapInColor("start", COLOR_SUCCESS)} - Starts event at random position. Additional argument - {WrapInColor("here", COLOR_DEFOCUS)} might be specified to create event on the point you are currently looking at\n" +
                        $"{WrapInColor("end", COLOR_SUCCESS)} - Stops an event, needs to be provided with either id or grid cell as an argument",
                    [M_EVENT_INFO] =
                        $"#{WrapInColor("{0}", COLOR_INFO)}: [{WrapInColor("{1}", COLOR_WARN)}], Time left: {WrapInColor("{2}", COLOR_DEFOCUS)}",
                    [M_EVENT_NOT_FOUND] = WrapInColor("Event not found", COLOR_DANGER),
                    [M_NO_EVENTS] = WrapInColor("No events currently in process", COLOR_WARN),
                    [M_EVENT_STARTED] =
                        $"Capture the flag event started at [{WrapInColor("{0}", COLOR_WARN)}], check your map!",
                    [M_EVENT_STARTED_BY] =
                        $"Capture the flag event started by {WrapInColor("{1}", COLOR_INFO)} at [{WrapInColor("{0}", COLOR_WARN)}], check your map!",
                    [M_EVENT_FINISHED_TIME] = WrapInColor(
                        $"Capture the flag event at [{WrapInColor("{0}", COLOR_WARN)}] has ran out of time, no winner chosen",
                        COLOR_DEFOCUS
                    ),
                    [M_EVENT_FINISHED_FORCE] =
                        $"Capture the flag event at [{WrapInColor("{0}", COLOR_DEFOCUS)}] was stopped by {WrapInColor("{1}", COLOR_WARN)}",
                    [M_EVENT_FINISHED_CAPTURED] =
                        $"Capture the flag event at [{WrapInColor("{0}", COLOR_SUCCESS)}] was won by {{1}}",
                    [M_INVALID_POS] = WrapInColor("Event cannot be spawned here", COLOR_DANGER),
                    [M_EVENT_EXISTS] = WrapInColor(
                        "Event already exists near that position, try choosing another place",
                        COLOR_WARN
                    ),
                    [M_EVENT_LIMIT] = WrapInColor(
                        "Event limit reached, wait for other event to end, or stop it manually",
                        COLOR_DANGER
                    ),
                    [M_EVENT_TICK_CAPTURING] =
                        $"You are capturing flag at [{WrapInColor("{0}", COLOR_SUCCESS)}], stay in the zone for {WrapInColor("{1}", COLOR_INFO)} more seconds",
                    [M_EVENT_TICK_CONTESTED] =
                        $"The objective at [{WrapInColor("{0}", COLOR_WARN)}] is being contested, make other {WrapInColor("{1}", COLOR_DANGER)} players leave the zone or put a bullet in their heads!"
                },
                this
            );
        }

        void Message(IPlayer player, string langKey, params object[] args)
        {
            string prefix = GetMessage(player, M_PREFIX);
            string format = GetMessage(player, langKey);
            string message = string.Format(format, args);

            player.Message(prefix + message);
        }

        void MessageRaw(IPlayer player, string message, bool newLine = false)
        {
            string prefix = GetMessage(player, M_PREFIX);

            if (newLine)
            {
                player.Message(prefix + '\n' + message);
            }
            else
            {
                player.Message(prefix + message);
            }
        }

        void Announce(string langKey, params object[] args)
        {
            foreach (IPlayer player in players.Connected)
            {
                Message(player, langKey, args);
            }
        }

        void AnnounceRaw(string message, bool newLine = false)
        {
            foreach (IPlayer player in players.Connected)
            {
                MessageRaw(player, message, newLine);
            }
        }

        string GetMessage(IPlayer player, string langKey)
        {
            return lang.GetMessage(langKey, this, player.Id);
        }

        string WrapInColor(string str, string color)
        {
            return "<color=" + color + ">" + str + COLOR_CLOSE;
        }

        string GetFlagInfo(IPlayer player, Flag flag)
        {
            string format = GetMessage(player, M_EVENT_INFO);

            string info = string.Format(format, flag.Id, flag.GridCell, flag.TimeLeft);

            return info;
        }

        #endregion

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            settings = PluginSettings.Default;
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                settings = Config.ReadObject<PluginSettings>();

                if (settings == null)
                {
                    throw new Exception();
                }
            }
            catch (Exception e)
            {
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        #region Nested types

        public class Flag : MonoBehaviour
        {
            static List<Flag>     allEvents;
            static ItemDefinition captureItemDef;

            PluginSettings             settings;
            DroppedItem                bindObject;
            float                      startTime;
            float                      endTime;
            float                      capturePercentage;
            float                      lastCaptureTickTime;
            List<BasePlayer>           currentCapturers;
            List<BasePlayer>           allPlayersInZone;
            List<BasePlayer>           alivePlayersInZone;
            string                     gridCell;
            MapMarkerGenericRadius     mapMarker;
            EventState                 state;
            object                     initiator;
            Dictionary<IPlayer, float> lastNotificationTime;

            public static int EventCount => allEvents.Count;

            public static bool IsEventLimitReached => EventCount == Instance.maxEventsPerMap ||
                                                      Instance.settings.EventLimit > -1 &&
                                                      EventCount == Instance.settings.EventLimit;

            public string GridCell => gridCell;
            public int Id => allEvents.IndexOf(this) + 1;
            public int TimeLeft => Mathf.FloorToInt(endTime - Time.realtimeSinceStartup);
            public float CaptureProgress => capturePercentage;

            public int CaptureTimeLeft =>
                Mathf.FloorToInt(settings.CaptureTime - settings.CaptureTime * capturePercentage);

            public int CapturerCount => currentCapturers.Count;

            public EventState State
            {
                get { return state; }

                private set
                {
                    Assert.IsFalse(state == value, "Updating state with the same value!");

                    Instance.OnEventStateChange(this, state, value);
                    state = value;
                    UpdateMarkerColors();
                    UpdateMarkerForAll();
                }
            }

            public static void Initialize()
            {
                captureItemDef = ItemManager.FindItemDefinition(Instance.settings.CaptureItemShortName);

                if (!captureItemDef)
                {
                    throw new ArgumentException(
                        $"Could not find item definition for shortname '{Instance.settings.CaptureItemShortName}'"
                    );
                }

                allEvents = new List<Flag>();
            }

            public static void Unload()
            {
                if (allEvents == null)
                {
                    return; // Plugin was unloaded before server fully initialized
                }

                foreach (Flag allEvent in allEvents)
                {
                    allEvent.TimedDispose();
                }

                captureItemDef = null;
                allEvents = null;
            }

            public static Flag FindByGridCell(string gridCell)
            {
                return allEvents.Find(e => e.gridCell == gridCell.ToUpper());
            }

            public static Flag FindById(int id)
            {
                if (id < 1 || id > allEvents.Count)
                {
                    return null;
                }

                return allEvents[id - 1];
            }

            public static Flag[] GetAll()
            {
                return allEvents.ToArray();
            }

            public static void OnPlayerConnected(BasePlayer player)
            {
                for (int i = 0; i < allEvents.Count; i++)
                {
                    Flag flag = allEvents[i];
                    flag.SendMarkerUpdate(player);
                }
            }

            public static void CreateNewEvent(Vector3 pos)
            {
                CreateEvent(pos);
            }

            public static void CreateNewEvent(Vector3 pos, object initiator)
            {
                Flag flag = CreateEvent(pos);
                flag.SetInitiator(initiator);
            }

            static Flag CreateEvent(Vector3 pos)
            {
                Item item = ItemManager.Create(captureItemDef, skin: Instance.settings.CaptureItemSkinId);

                Assert.IsNotNull(item, "Item is null in CreateEvent");

                BaseEntity bindObject = item.CreateWorldObject(pos);

                Assert.IsNotNull(bindObject, "Bind object is null in CreateEvent");

                Flag flag = bindObject.gameObject.AddComponent<Flag>();

                return flag;
            }

            public Dictionary<string, object> ToSerializable()
            {
                var dictionary = new Dictionary<string, object>();

                dictionary["id"] = Id;
                dictionary["gridcell"] = gridCell;
                dictionary["starttime"] = startTime;
                dictionary["endtime"] = endTime;
                dictionary["bindobj"] = bindObject;
                dictionary["capturepercent"] = capturePercentage;
                dictionary["capturers"] = currentCapturers.ToArray();
                dictionary["state"] = (int)state;

                return dictionary;
            }

            public void ForceFinish(IPlayer manager)
            {
                ForceDispose(manager);
            }

            public void ForceFinish(Plugin caller)
            {
                ForceDispose(caller);
            }

            public void SetInitiator(object initiator)
            {
                Assert.IsTrue(initiator is IPlayer || initiator is Plugin, "Initiator is not a player nor a plugin!");

                this.initiator = initiator;
            }

            public bool ShouldNotify(IPlayer player)
            {
                if (player == null)
                {
                    return false;
                }

                bool b = false;
                if (!lastNotificationTime.ContainsKey(player))
                {
                    b = true;
                }
                else if (Time.realtimeSinceStartup - lastNotificationTime[player] >
                         settings.EventTickNotificationFrequency)
                {
                    b = true;
                }

                if (b)
                {
                    lastNotificationTime[player] = Time.realtimeSinceStartup;
                }

                return b;
            }

            public void GetAlivePlayers(List<BasePlayer> list)
            {
                list.AddRange(alivePlayersInZone);
            }

            void Awake()
            {
                settings = Instance.settings;
                bindObject = GetComponent<DroppedItem>();

                currentCapturers = new List<BasePlayer>();
                allPlayersInZone = new List<BasePlayer>();
                alivePlayersInZone = new List<BasePlayer>();
                lastNotificationTime = new Dictionary<IPlayer, float>();

                Assert.IsNotNull(bindObject, "Bind object is null in Awake!");

                SetupCollider();
                SetupRotation();

                startTime = Time.realtimeSinceStartup;
                endTime = startTime + settings.TotalEventTime;

                gridCell = PhoneController.PositionToGridCoord(bindObject.transform.position);

                allEvents.Add(this);

                bindObject.allowPickup = false;

                InvokeRepeating(nameof(EventTick), 1f, 1f);

                if (settings.EnableMapMarkers)
                {
                    SetupMarker();
                    UpdateMarkerColors();
                    UpdateMarkerForAll();
                }

                bindObject.SendNetworkUpdate();
            }

            void Start()
            {
                if (initiator == null)
                {
                    OnEventStart();
                }
                else if (initiator is IPlayer)
                {
                    OnEventStart((IPlayer)initiator);
                }
                else
                {
                    OnEventStart((Plugin)initiator);
                }
            }

            void OnTriggerEnter(Collider other)
            {
                if (other.gameObject.layer != 17)
                {
                    return;
                }

                var player = other.GetComponentInParent<BasePlayer>();

                Assert.IsNotNull(player, "Player is null in OnTriggerEnter");
                LogDebug("OnTriggerEnter: {0}", player.displayName);

                allPlayersInZone.Add(player);

                if (!player.IsDead() && !player.IsSleeping())
                {
                    OnPlayerEnterCaptureZone(player);
                }
            }

            void OnTriggerExit(Collider other)
            {
                if (other.gameObject.layer != 17)
                {
                    return;
                }

                var player = other.GetComponentInParent<BasePlayer>();

                Assert.IsNotNull(player, "Player is null on OnTriggerExit");
                LogDebug("OnTriggerExit: {0}", player.displayName);

                allPlayersInZone.Remove(player);

                if (alivePlayersInZone.Contains(player))
                {
                    OnPlayerLeaveCaptureZone(player);
                }
            }

            void OnDestroy()
            {
                CancelInvoke(nameof(Rotate));
                CancelInvoke(nameof(EventTick));

                mapMarker?.Kill();

                allEvents?.Remove(this);
            }

            #region Collider, rotation, markers

            void SetupCollider()
            {
                Destroy(GetComponent<PhysicsEffects>());
                Destroy(GetComponent<EntityCollisionMessage>());

                var rb = GetComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.detectCollisions = true;

                Assert.IsNotNull(rb, "Rigidbody is null!");

                var collider = gameObject.AddComponent<SphereCollider>();

                collider.enabled = true;
                collider.isTrigger = true;
                collider.radius = settings.CaptureRadius;

                gameObject.layer = 3;
            }

            void SetupRotation()
            {
                var height =
                    TerrainMeta.HeightMap.GetHeight(
                        transform.position
                    ); // TODO: get actual height considering buildings

                var targetHeight = height + 1.5f;

                transform.position = new Vector3(transform.position.x, targetHeight, transform.position.z);

                transform.Rotate(90, 0, 0);
                InvokeRepeating(nameof(Rotate), 0.1f, 0.1f);
            }

            void SetupMarker()
            {
                mapMarker = (MapMarkerGenericRadius)GameManager.server.CreateEntity(
                    PREFAB_MARKER_GENERIC,
                    transform.position
                );
                mapMarker.Spawn();

                mapMarker.alpha = 0.65f;
                mapMarker.radius = settings.CaptureRadius * 0.0178f;
                mapMarker.color2 = Color.black;
            }

            void UpdateMarkerColors()
            {
                if (!mapMarker)
                {
                    return;
                }

                switch (State)
                {
                    case EventState.Stale:
                        mapMarker.color1 = Color.red;
                        break;

                    case EventState.Capturing:
                        mapMarker.color1 = Color.green;
                        break;

                    case EventState.Contested:
                        mapMarker.color1 = Color.yellow;
                        break;
                }
            }

            void SendMarkerUpdate(BasePlayer player)
            {
                if (!mapMarker)
                {
                    return;
                }

                mapMarker.ClientRPCEx(
                    new SendInfo(player.Connection),
                    null,
                    "MarkerUpdate",
                    new Vector3(mapMarker.color1.r, mapMarker.color1.g, mapMarker.color1.b),
                    mapMarker.color1.a,
                    mapMarker.alpha,
                    mapMarker.radius
                );
            }

            void UpdateMarkerForAll()
            {
                if (!mapMarker)
                {
                    return;
                }

                List<Connection> list = Pool.GetList<Connection>();

                list.AddRange(BasePlayer.activePlayerList.Select(p => p.Connection));

                mapMarker.ClientRPCEx(
                    new SendInfo(list),
                    null,
                    "MarkerUpdate",
                    new Vector3(mapMarker.color1.r, mapMarker.color1.g, mapMarker.color1.b),
                    mapMarker.color1.a,
                    mapMarker.alpha,
                    mapMarker.radius
                );

                Pool.FreeList(ref list);
            }

            void Rotate()
            {
                transform.Rotate(0, 0, 5);
            }

            #endregion

            void EventTick()
            {
                for (int i = 0; i < allPlayersInZone.Count; i++)
                {
                    var player = allPlayersInZone[i];

                    if ((player.IsDead() || player.IsSleeping()) && alivePlayersInZone.Contains(player))
                    {
                        OnPlayerLeaveCaptureZone(player);
                    }
                    else if (!player.IsDead() && !player.IsSleeping() && !alivePlayersInZone.Contains(player))
                    {
                        OnPlayerEnterCaptureZone(player);
                    }
                }

                if (State == EventState.Stale && Time.realtimeSinceStartup >= endTime)
                {
                    TimedDispose();
                }
                else
                {
                    if (State == EventState.Capturing)
                    {
                        OnCaptureTick(Time.realtimeSinceStartup - lastCaptureTickTime, currentCapturers.Count);
                    }

                    Instance.OnEventTick(this);
                }
            }

            void OnPlayerEnterCaptureZone(BasePlayer player)
            {
                alivePlayersInZone.Add(player);

                LogDebug("OnPlayerEnterCaptureZone {0}", player.displayName);

                switch (State)
                {
                    case EventState.Stale:
                        currentCapturers.Add(player);
                        State = EventState.Capturing;
                        OnCaptureStart(player);
                        break;
                    case EventState.Capturing:
                        var isTeamMate = player.Team != null &&
                                         player.Team.teamID != 0 &&
                                         player.Team.teamID == currentCapturers[0].Team.teamID;

                        if (isTeamMate && settings.AllowTeamCapture)
                        {
                            currentCapturers.Add(player);
                        }
                        else
                        {
                            State = EventState.Contested;
                            OnContestedStart(player);
                        }

                        break;
                }
            }

            void OnPlayerLeaveCaptureZone(BasePlayer player)
            {
                alivePlayersInZone.Remove(player);

                LogDebug("OnPlayerLeaveCaptureZone {0}", player.displayName);

                switch (State)
                {
                    case EventState.Capturing:
                        if (currentCapturers.Remove(player) && currentCapturers.Count == 0)
                        {
                            State = EventState.Stale;
                            OnCaptureStop(player);
                        }

                        break;
                    case EventState.Contested:
                        if (currentCapturers.Remove(player))
                        {
                            if (currentCapturers.Count == 0)
                            {
                                BasePlayer newCapturer = alivePlayersInZone[0];
                                currentCapturers.Add(newCapturer);

                                if (settings.AllowTeamCapture)
                                {
                                    currentCapturers.AddRange(
                                        alivePlayersInZone.Skip(1).Where(
                                            p => p.Team != null &&
                                                 p.Team.teamID != 0 &&
                                                 p.Team.teamID == newCapturer.Team.teamID
                                        )
                                    );
                                }

                                if (alivePlayersInZone.Count != currentCapturers.Count)
                                {
                                    State = EventState.Contested;
                                    OnContestedStart(newCapturer);
                                }
                                else
                                {
                                    State = EventState.Capturing;
                                    OnCaptureStart(newCapturer);
                                }
                            }
                        }
                        else if (currentCapturers.Count == alivePlayersInZone.Count)
                        {
                            State = EventState.Capturing;
                        }

                        break;
                }
            }

            void OnCaptureStart(BasePlayer player)
            {
                lastCaptureTickTime = Time.realtimeSinceStartup;
                capturePercentage = 0f;

                LogDebug("OnCaptureStart: {0}", player.displayName);
            }

            void OnContestedStart(BasePlayer player)
            {
                LogDebug("OnContestedStart: {0}", player.displayName);
            }

            void OnCaptureStop(BasePlayer player)
            {
                LogDebug("OnCaptureStop: {0}", player.displayName);
            }

            void OnCaptureTick(float deltaTime, int capturerCount)
            {
                var basePercentage = deltaTime / settings.CaptureTime;
                var addPercentage = basePercentage * (capturerCount - 1) * settings.MultipleCapturersSpeedup;

                var totalPercentage = basePercentage + addPercentage;

                capturePercentage += totalPercentage;

                lastCaptureTickTime = Time.realtimeSinceStartup;

                LogDebug(
                    "OnCaptureTick:\n" +
                    "deltaTime: {0}, capturers: {1}, basePercentage: {2}, additionalPerc: {3}\n" +
                    "resulting capture percentage: {4}",
                    deltaTime,
                    capturerCount,
                    basePercentage,
                    addPercentage,
                    capturePercentage
                );

                if (capturePercentage >= 1f)
                {
                    OnCaptureSuccess();
                }
            }

            void OnCaptureSuccess()
            {
                if (currentCapturers.Count == 1)
                {
                    BasePlayer winner = currentCapturers[0];

                    LogDebug("OnCaptureSuccess -> {0}", winner.displayName);

                    OnEventEnd(winner);
                }
                else
                {
                    OnEventEnd(currentCapturers);
                }

                Dispose();
            }

            void OnEventStart()
            {
                LogDebug("OnEventStart {0}", gridCell);

                Instance.OnEventStarted(this);
            }

            void OnEventStart(IPlayer manager)
            {
                LogDebug("OnEventStart by player {0}: {1}", manager, gridCell);

                Instance.OnEventStarted(this, manager);
            }

            void OnEventStart(Plugin caller)
            {
                LogDebug("OnEventStart by plugin {0}: {1}", caller.Name, gridCell);

                Instance.OnEventStarted(this, caller);
            }

            void OnEventEnd(BasePlayer winner = null)
            {
                if (winner)
                {
                    // ReSharper disable PossibleNullReferenceException
                    LogDebug("Event at {0} was won by player {1}", gridCell, winner.displayName);
                    // ReSharper enable PossibleNullReferenceException

                    Instance.OnEventFinished(this, winner);
                }
                else
                {
                    LogDebug("Event at {0} ended with no winner", gridCell);

                    Instance.OnEventFinished(this);
                }
            }

            void OnEventEnd(List<BasePlayer> winners)
            {
                LogDebug("Event at {0} was won by group of players ({1})", gridCell, winners.Count);

                Instance.OnEventFinished(this, winners);
            }

            void Dispose()
            {
                Item item = bindObject.GetItem();

                item.RemoveFromWorld();
                item.Remove(0f);

                LogDebug("Disposing event at cell {0}", gridCell);
            }

            void TimedDispose()
            {
                LogDebug("TimedDispose of {0}", gridCell);
                OnEventEnd();
                Dispose();
            }

            void ForceDispose(IPlayer manager)
            {
                LogDebug("Player {0} force disposed event at {1}", manager, gridCell);
                Instance.OnEventFinished(this, manager);
                //OnEventEnd();
                Dispose();
            }

            void ForceDispose(Plugin caller)
            {
                LogDebug("Plugin {0} force disposed event at {1}", caller.Name, gridCell);
                Instance.OnEventFinished(this, caller);
                //OnEventEnd(); // TODO: maybe remove this
                Dispose();
            }

            public enum EventState
            {
                Stale,
                Capturing,
                Contested
            }
        }

        #region Configuration

        class PluginSettings
        {
            public static PluginSettings Default => new PluginSettings {
                CaptureItemShortName = "rifle.ak",
                CaptureItemSkinId = 1167207039ul,
                TotalEventTime = 300f,
                CaptureTime = 60f,
                CaptureRadius = 30f,
                AllowTeamCapture = false,
                MultipleCapturersSpeedup = 0.5f,
                EnableMapMarkers = true,
                EventTickNotificationFrequency = 15f,
                EventAutoCreateFrequency = 900f,
                AutoCreateIfAnyExists = false,
                EventLimit = -1
            };

            public string CaptureItemShortName { get; set; }
            public ulong CaptureItemSkinId { get; set; }
            public float TotalEventTime { get; set; }
            public float CaptureTime { get; set; }
            public float CaptureRadius { get; set; }
            public bool AllowTeamCapture { get; set; }
            public float MultipleCapturersSpeedup { get; set; }
            public bool EnableMapMarkers { get; set; }
            public float EventTickNotificationFrequency { get; set; }
            public float EventAutoCreateFrequency { get; set; }
            public bool AutoCreateIfAnyExists { get; set; }
            public int EventLimit { get; set; }
        }

        #endregion

        #endregion
    }
}
