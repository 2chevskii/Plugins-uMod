using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Facepunch;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;

using UnityEngine;

using Time = UnityEngine.Time;

namespace Oxide.Plugins
{
    [Info("Vote Ban", "2CHEVSKII", "1.0.0")]
    [Description("Allows players to vote for banning others")]
    class VoteBan : CovalencePlugin
    {
        const string M_PREFIX                 = "Chat prefix",
                     M_VOTE_STARTED           = "Vote started",
                     M_VOTE_PROGRESS          = "Vote in progress",
                     M_VOTED                  = "Voted",
                     M_ALREADY_VOTED          = "Voted already",
                     M_NO_PERMISSION          = "No permission",
                     M_NO_VOTE                = "No active vote",
                     M_VOTE_TIMED_OUT         = "Vote timed out",
                     M_VOTE_SUCCESS           = "Vote success",
                     M_VOTE_CANCELLED         = "Vote cancelled",
                     M_VOTE_CANCELLED_PLAYERS = "Vote cancelled (insufficient player count)",
                     M_BAN_REASON             = "Ban reason",
                     M_CANNOT_START_ADMIN     = "Vote cannot be started (cannot vote-out admins)",
                     M_CANNOT_START_PLAYERS   = "Vote cannot be started (insufficient player count)",
                     M_PLAYER_NOT_FOUND       = "Player not found",
                     M_CANNOT_VOTE_SELF       = "Cannot vote-out self";

        const string PERMISSION_MANAGE = "voteban.manage",
                     PERMISSION_VOTE   = "voteban.vote";

        PluginSettings settings;
        VoteData       voteData;

        void Init()
        {
            permission.RegisterPermission(PERMISSION_MANAGE, this);
            permission.RegisterPermission(PERMISSION_VOTE, this);

            AddCovalenceCommand("voteban", nameof(CommandHandler));
        }

        #region Plugin API

        void OnVoteStarted(IPlayer initiator, IPlayer target)
        {
            Interface.Oxide.CallHook("OnVotebanStarted", initiator, target, settings.VoteTime);
            Announce(M_VOTE_STARTED, initiator.Name, target.Name, settings.VoteTime);
        }

        void OnPlayerVoted(IPlayer player)
        {
            Interface.Oxide.CallHook("OnVotebanPlayerVoted", player);
            Message(player, M_VOTED);
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
                voteData.VoteFraction,
                voteData.VotedPlayerCount
            );
        }

        void OnVoteCancelled(IPlayer canceller = null)
        {
            if (canceller != null)
            {
                Interface.Oxide.CallHook(
                    "OnVotebanCancelled",
                    voteData.voteTarget,
                    (int)VoteData.CancelReason.Manual,
                    canceller
                );

                Announce(M_VOTE_CANCELLED, voteData.voteTarget.Name, canceller.Name);
            }
            else
            {
                Interface.Oxide.CallHook(
                    "OnVotebanCancelled",
                    voteData.voteTarget,
                    (int)VoteData.CancelReason.InsufficientPlayers
                );

                Announce(M_VOTE_CANCELLED_PLAYERS, voteData.voteTarget.Name, settings.MinPlayers);
            }
        }

        void OnVoteTimedOut()
        {
            Interface.Oxide.CallHook("OnVotebanTimedOut", voteData.voteTarget);

            Announce(M_VOTE_TIMED_OUT, voteData.voteTarget.Name, settings.VoteTime);
        }

        bool IsVotebanInProgress()
        {
            return voteData?.voteRoutine != null;
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
            record["required_voted_players"] = players.Connected.Count() / (settings.PercentageRequired / 100f);
            record["time_left"] = voteData.TimeLeft;
            record["start_time"] = voteData.startTime;
            record["end_time"] = voteData.endTime;

            return true;
        }

        #endregion

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
                if (voteData == null || voteData.voteRoutine == null)
                {
                    Message(player, M_NO_VOTE);
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
            if (CheckPermission(player, PERMISSION_MANAGE)) { }
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
            lang.RegisterMessages(new Dictionary<string, string> {
                [M_PREFIX] = "[VoteBan] ",
                [M_NO_PERMISSION] = "You are not allowed to use this command",
                [M_VOTE_STARTED] = "{0} has started the vote to ban {1}, use /voteban to vote for it",
                [M_VOTE_PROGRESS] = "Vote to ban {1} is in progress, use /voteban to vote for it. Current vote progress: {2}% ({3}/{4})",
                [M_VOTED] = "You've voted for banning player {0}",
                [M_ALREADY_VOTED] = "You've voted already",
                [M_NO_VOTE] = "No active vote found",
                [M_VOTE_TIMED_OUT] = "Vote to ban player {1} was unsuccessful, not enough players voted for it",
                [M_VOTE_SUCCESS] = "Vote to ban player {1} was successful, player will now be banned from the server",
                [M_VOTE_CANCELLED] = "{0} has cancelled the vote for banning player {2}",
                [M_VOTE_CANCELLED_PLAYERS] = "Vote for banning player {1} was cancelled because of insufficient player count ({2}/{3})",
                [M_BAN_REASON] = "You've been voted-out",
                [M_CANNOT_START_ADMIN] = "Ban vote cannot be started against admins",
                [M_CANNOT_START_PLAYERS] = "Not enough players on the server to start a vote ({0}/{1})",
                [M_PLAYER_NOT_FOUND] = "Vote target ({0}) was not found",
                [M_CANNOT_VOTE_SELF] = "You cannot vote against yourself"
            }, this);
        }

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

        class PluginSettings
        {
            public static PluginSettings Default => new PluginSettings {
                VoteTime = 600,
                NotificationFrequency = 30,
                MinPlayers = 5,
                PercentageRequired = 70,
                AllowBanningAdmins = false
            };

            [JsonProperty("Vote time")] public int VoteTime { get; set; }

            [JsonProperty("Notification frequency")]
            public int NotificationFrequency { get; set; }

            [JsonProperty("Min player count to start vote")]
            public int MinPlayers { get; set; }

            [JsonProperty("Vote success percentage")]
            public int PercentageRequired { get; set; }

            [JsonProperty("Allow banning admins")] public bool AllowBanningAdmins { get; set; }
        }

        class VoteData
        {
            public Timer         voteTimer;
            public IPlayer       voteTarget;
            public IPlayer       voteInitiator;
            public List<IPlayer> votedPlayers;
            public IEnumerator   voteRoutine;
            public VoteBan       plugin;
            public float         startTime;
            public float         endTime;

            public int VotedPlayerCount => votedPlayers.Count;
            public float VoteFraction => VotedPlayerCount / (plugin.settings.PercentageRequired / 100f);
            public float TimeLeft => endTime - Time.realtimeSinceStartup;

            public VoteData(IPlayer initiator, IPlayer target, VoteBan plugin)
            {
                voteTarget = target;
                voteInitiator = initiator;
                this.plugin = plugin;
                votedPlayers = Pool.GetList<IPlayer>();
                voteTimer = plugin.timer.Every(
                    plugin.settings.NotificationFrequency,
                    VoteTick
                );
                voteRoutine = CreateVoteRoutine();
            }

            public void OnPlayerVoted(IPlayer player)
            {
                votedPlayers.Add(player);
                plugin.OnPlayerVoted(player);

                if (VoteFraction >= 1f)
                {
                    // finish vote with success
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

            IEnumerator CreateVoteRoutine()
            {
                plugin.OnVoteStarted(voteInitiator, voteTarget);

                while (Time.realtimeSinceStartup < endTime)
                {
                    if (plugin.players.Connected.Count() < plugin.settings.MinPlayers)
                    {
                        plugin.OnVoteCancelled();
                        yield break;
                    }

                    plugin.OnVoteTick();
                    yield return null;
                }

                plugin.OnVoteTimedOut();
                EndVote();
            }

            void VoteTick()
            {
                voteRoutine.MoveNext();
            }

            void EndVote()
            {
                voteRoutine = null;
                voteTimer.Destroy();
                voteTimer = null;
                voteTarget = null;
                voteInitiator = null;
                Pool.FreeList(ref votedPlayers);
                plugin = null;
            }

            public enum CancelReason
            {
                Manual,
                InsufficientPlayers
            }
        }
    }
}
