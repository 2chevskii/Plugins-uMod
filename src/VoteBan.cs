using System;
using System.Collections.Generic;
using System.Linq;

#if RUST
using Facepunch;
#endif

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

using UnityEngine;

using Time = UnityEngine.Time;

namespace Oxide.Plugins
{
    [Info("Vote Ban", "2CHEVSKII", "1.0.1")]
    [Description("Allows players to vote for banning others")]
    class VoteBan : CovalencePlugin
    {
        #region Fields

        const string M_PREFIX = "Chat prefix",
            M_VOTE_STARTED = "Vote started",
            M_VOTE_PROGRESS = "Vote in progress",
            M_VOTED = "Voted",
            M_ALREADY_VOTED = "Voted already",
            M_NO_PERMISSION = "No permission",
            M_NO_VOTE = "No active vote",
            M_VOTE_TIMED_OUT = "Vote timed out",
            M_VOTE_SUCCESS = "Vote success",
            M_VOTE_CANCELLED = "Vote cancelled",
            M_VOTE_CANCELLED_PLAYERS = "Vote cancelled (insufficient player count)",
            M_BAN_REASON = "Ban reason",
            M_CANNOT_START_ADMIN = "Vote cannot be started (cannot vote-out admins)",
            M_CANNOT_START_PLAYERS = "Vote cannot be started (insufficient player count)",
            M_PLAYER_NOT_FOUND = "Player not found",
            M_CANNOT_VOTE_SELF = "Cannot vote-out self";

        const string PERMISSION_MANAGE = "voteban.manage",
            PERMISSION_VOTE = "voteban.vote";

        PluginSettings settings;
        VoteData voteData;

        #endregion

        #region Oxide hooks

        void Init()
        {
            permission.RegisterPermission(PERMISSION_MANAGE, this);
            permission.RegisterPermission(PERMISSION_VOTE, this);

            AddCovalenceCommand("voteban", nameof(CommandHandler));
        }

        #endregion

        #region Plugin API

        void OnVoteStarted(IPlayer initiator, IPlayer target)
        {
            Interface.Oxide.CallHook("OnVotebanStarted", initiator, target, settings.VoteTime);
            Announce(M_VOTE_STARTED, initiator.Name, target.Name, settings.VoteTime);
        }

        void OnPlayerVoted(IPlayer player)
        {
            Interface.Oxide.CallHook("OnVotebanPlayerVoted", player);
            Message(player, M_VOTED, voteData.voteTarget.Name);
        }

        void OnVoteTick()
        {
            Interface.Oxide.CallHook(
                "OnVotebanTick",
                voteData.voteInitiator,
                voteData.voteTarget,
                voteData.VoteFraction,
                voteData.VotedPlayerCount,
                voteData.TimeLeft
            );

            Announce(
                M_VOTE_PROGRESS,
                voteData.voteInitiator.Name,
                voteData.voteTarget.Name,
                Mathf.FloorToInt(voteData.VoteFraction * 100),
                voteData.VotedPlayerCount,
                GetPlayersRequiredToVoteSuccess(GetPlayerCountWithoutTarget(voteData.voteTarget)),
                Mathf.CeilToInt(voteData.TimeLeft)
            );
        }

        void OnVoteCancelled(IPlayer canceller = null)
        {
            if (canceller != null)
            {
                Interface.Oxide.CallHook(
                    "OnVotebanCancelled",
                    voteData.voteInitiator,
                    voteData.voteTarget,
                    (int)VoteData.CancelReason.Manual,
                    canceller
                );

                Announce(
                    M_VOTE_CANCELLED,
                    canceller.Name,
                    voteData.voteInitiator.Name,
                    voteData.voteTarget.Name
                );
            }
            else
            {
                Interface.Oxide.CallHook(
                    "OnVotebanCancelled",
                    voteData.voteInitiator,
                    voteData.voteTarget,
                    (int)VoteData.CancelReason.InsufficientPlayers
                );

                Announce(
                    M_VOTE_CANCELLED_PLAYERS,
                    voteData.voteInitiator.Name,
                    voteData.voteTarget.Name,
                    GetPlayerCountWithoutTarget(voteData.voteTarget),
                    settings.MinPlayers
                );
            }
        }

        void OnVoteTimedOut()
        {
            Interface.Oxide.CallHook("OnVotebanTimedOut", voteData.voteTarget);

            Announce(
                M_VOTE_TIMED_OUT,
                voteData.voteInitiator.Name,
                voteData.voteTarget.Name,
                settings.VoteTime
            );
        }

        void OnVoteSuccess()
        {
            Interface.Oxide.CallHook(
                "OnVotebanSuccess",
                voteData.voteInitiator,
                voteData.voteTarget
            );

            Announce(M_VOTE_SUCCESS, voteData.voteInitiator.Name, voteData.voteTarget.Name);

            voteData.voteTarget.Ban(GetMessage(voteData.voteTarget, M_BAN_REASON));
        }

        bool IsVotebanInProgress()
        {
            return voteData != null;
        }

        bool GetCurrentVotebanData(Dictionary<string, object> record)
        {
            if (record == null)
            {
                throw new ArgumentNullException();
            }

            if (!IsVotebanInProgress())
            {
                return false;
            }

            record["initiator"] = voteData.voteInitiator;
            record["target"] = voteData.voteTarget;
            record["fraction"] = voteData.VoteFraction;
            record["required_fraction"] = settings.PercentageRequired / 100f;
            record["voted_players"] = voteData.votedPlayers.ToArray();
            record["required_voted_players"] = Mathf.RoundToInt(
                GetPlayerCountWithoutTarget(voteData.voteTarget)
                    * (float)record["required_fraction"]
            );
            record["time_left"] = voteData.TimeLeft;
            record["start_time"] = voteData.startTime;
            record["end_time"] = voteData.endTime;

            return true;
        }

        #endregion

        #region Command handlers

        void CommandHandler(IPlayer player, string _, string[] args)
        {
            if (args.Length == 0)
            {
                HandleVoteCommand(player);
            }
            else
            {
                HandleManageCommand(player, args[0]);
            }
        }

        void HandleVoteCommand(IPlayer player)
        {
            if (CheckPermission(player, PERMISSION_VOTE))
            {
                if (voteData == null)
                {
                    Message(player, M_NO_VOTE);
                    return;
                }

                if (voteData.voteTarget.Id.Equals(player.Id))
                {
                    Message(player, M_CANNOT_VOTE_SELF);
                    return;
                }

                if (!voteData.CanPlayerVote(player))
                {
                    Message(player, M_ALREADY_VOTED);
                    return;
                }

                voteData.OnPlayerVoted(player);
            }
        }

        void HandleManageCommand(IPlayer player, string arg)
        {
            if (voteData != null)
            {
                switch (arg.ToLower())
                {
                    case "end":
                    case "cancel":
                        if (CheckPermission(player, PERMISSION_MANAGE))
                        {
                            voteData.CancelVote(player);
                        }

                        break;
                    default:
                        Message(
                            player,
                            M_VOTE_PROGRESS,
                            voteData.voteInitiator.Name,
                            voteData.voteTarget.Name,
                            (int)(voteData.VoteFraction * 100),
                            voteData.VotedPlayerCount,
                            GetPlayersRequiredToVoteSuccess(
                                GetPlayerCountWithoutTarget(voteData.voteTarget)
                            )
                        );
                        break;
                }
            }
            else if (CheckPermission(player, PERMISSION_MANAGE))
            {
                IPlayer target = players.FindPlayer(arg);

                if (target == null)
                {
                    Message(player, M_PLAYER_NOT_FOUND, arg);
                }
                else if (target.Id.Equals(player.Id))
                {
                    Message(player, M_CANNOT_VOTE_SELF);
                }
                else if (target.IsAdmin && !settings.AllowBanningAdmins)
                {
                    Message(player, M_CANNOT_START_ADMIN);
                }
                else
                {
                    int playerCount = GetPlayerCountWithoutTarget(target);
                    if (playerCount < settings.MinPlayers)
                    {
                        Message(player, M_CANNOT_START_PLAYERS, playerCount, settings.MinPlayers);
                    }
                    else
                    {
                        voteData = new VoteData(player, target, this);
                    }
                }
            }
        }

        #endregion

        #region Utility

        int GetPlayerCountWithoutTarget(IPlayer target)
        {
            return players.Connected.Count(p => p != target);
        }

        int GetPlayersRequiredToVoteSuccess(int playerCount)
        {
            return Mathf.RoundToInt(settings.PercentageRequired / 100f * playerCount);
        }

        bool CheckPermission(IPlayer player, string perm)
        {
            if (player.HasPermission(perm))
            {
                return true;
            }

            Message(player, M_NO_PERMISSION);

            return false;
        }

        #endregion

        #region LangAPI

        string GetMessage(IPlayer player, string langKey)
        {
            return lang.GetMessage(langKey, this, player.Id);
        }

        void Message(IPlayer player, string langKey, params object[] args)
        {
            string prefix = GetMessage(player, M_PREFIX);
            string format = GetMessage(player, langKey);

            string message = prefix + string.Format(format, args);

            player.Message(message);
        }

        void Announce(string langKey, params object[] args)
        {
            foreach (IPlayer player in players.Connected)
            {
                Message(player, langKey, args);
            }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    [M_PREFIX] = "[VoteBan] ",
                    [M_NO_PERMISSION] = "You are not allowed to use this command",
                    [M_VOTE_STARTED] =
                        "{0} has started the vote to ban {1}, use /voteban to vote for it",
                    [M_VOTE_PROGRESS] =
                        "Vote to ban {1} is in progress, use /voteban to vote for it. Current vote progress: {2}% ({3}/{4}), time left: {5} seconds",
                    [M_VOTED] = "You've voted for banning player {0}",
                    [M_ALREADY_VOTED] = "You've voted already",
                    [M_NO_VOTE] = "No active vote found",
                    [M_VOTE_TIMED_OUT] =
                        "Vote to ban player {1} was unsuccessful, not enough players voted for it",
                    [M_VOTE_SUCCESS] =
                        "Vote to ban player {1} was successful, player will now be banned from the server",
                    [M_VOTE_CANCELLED] = "{0} has cancelled the vote for banning player {2}",
                    [M_VOTE_CANCELLED_PLAYERS] =
                        "Vote for banning player {1} was cancelled because of insufficient player count ({2}/{3})",
                    [M_BAN_REASON] = "You've been voted-out",
                    [M_CANNOT_START_ADMIN] = "Ban vote cannot be started against admins",
                    [M_CANNOT_START_PLAYERS] =
                        "Not enough players on the server to start a vote ({0}/{1})",
                    [M_PLAYER_NOT_FOUND] = "Vote target ({0}) was not found",
                    [M_CANNOT_VOTE_SELF] = "You cannot vote against yourself"
                },
                this
            );
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
                    throw new Exception("Configuration is null");
                }

                if (settings.PercentageRequired > 100 || settings.PercentageRequired < 1)
                {
                    LogWarning("Required vote percentage must be in range of 1..100");
                    settings.PercentageRequired = 50;
                }

                if (settings.VoteTime < 5)
                {
                    LogWarning("Vote time cannot be less than 5 seconds");
                    settings.VoteTime = 5;
                }

                if (settings.NotificationFrequency < 5)
                {
                    LogWarning("Notification frequency cannot be less than 5 seconds");
                    settings.NotificationFrequency = 5;
                }
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

        #region PluginSettings

        class PluginSettings
        {
            public static PluginSettings Default =>
                new PluginSettings
                {
                    VoteTime = 600,
                    NotificationFrequency = 30,
                    MinPlayers = 5,
                    PercentageRequired = 70,
                    AllowBanningAdmins = false
                };

            [JsonProperty("Vote time")]
            public int VoteTime { get; set; }

            [JsonProperty("Notification frequency")]
            public int NotificationFrequency { get; set; }

            [JsonProperty("Min player count to start vote")]
            public int MinPlayers { get; set; }

            [JsonProperty("Vote success percentage")]
            public int PercentageRequired { get; set; }

            [JsonProperty("Allow banning admins")]
            public bool AllowBanningAdmins { get; set; }
        }

        #endregion

        #region VoteData

        class VoteData
        {
            public readonly IPlayer voteTarget;
            public readonly IPlayer voteInitiator;
            public List<IPlayer> votedPlayers;
            public float startTime;
            public float endTime;

            readonly VoteBan plugin;
            Timer voteTimer;

            public int VotedPlayerCount => votedPlayers.Count;
            public float VoteFraction =>
                (float)VotedPlayerCount / plugin.GetPlayerCountWithoutTarget(voteTarget);
            public float TimeLeft => endTime - Time.realtimeSinceStartup;

            public VoteData(IPlayer initiator, IPlayer target, VoteBan plugin)
            {
                this.plugin = plugin;
                voteTarget = target;
                voteInitiator = initiator;
#if RUST
                votedPlayers = Pool.GetList<IPlayer>();
#else
                votedPlayers = new List<IPlayer>();
#endif

                StartVote();
            }

            public void OnPlayerVoted(IPlayer player)
            {
                votedPlayers.Add(player);
                plugin.OnPlayerVoted(player);

                if (VoteFraction >= plugin.settings.PercentageRequired / 100f)
                {
                    plugin.OnVoteSuccess();
                    EndVote();
                }
            }

            public bool CanPlayerVote(IPlayer player)
            {
                return !votedPlayers.Contains(player);
            }

            public void CancelVote(IPlayer canceller)
            {
                plugin.OnVoteCancelled(canceller);
                EndVote();
            }

            void StartVote()
            {
                startTime = Time.realtimeSinceStartup;
                endTime = startTime + plugin.settings.VoteTime;

                voteTimer = plugin.timer.Every(plugin.settings.NotificationFrequency, VoteTick);

                votedPlayers.Add(voteInitiator);

                plugin.OnVoteStarted(voteInitiator, voteTarget);
            }

            void VoteTick()
            {
                if (Time.realtimeSinceStartup < endTime)
                {
                    if (plugin.GetPlayerCountWithoutTarget(voteTarget) < plugin.settings.MinPlayers)
                    {
                        plugin.OnVoteCancelled();
                        EndVote();
                    }
                    else
                    {
                        plugin.OnVoteTick();
                    }
                }
                else
                {
                    plugin.OnVoteTimedOut();
                    EndVote();
                }
            }

            void EndVote()
            {
                voteTimer.Destroy();
#if RUST
                Pool.FreeList(ref votedPlayers);
#endif
                plugin.voteData = null;
            }

            public enum CancelReason
            {
                Manual,
                InsufficientPlayers
            }
        }

        #endregion
    }
}
