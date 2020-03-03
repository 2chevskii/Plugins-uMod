using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConVar;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Libraries.Covalence;
using Time = UnityEngine.Time;

namespace Oxide.Plugins
{
	class VoteAPI : CovalencePlugin
	{
		static StringBuilder builder;
		static VoteAPI instance;

		#region Oxide hooks

		void Init()
		{
			instance = this;
			builder = new StringBuilder();
			ServerMgr.Instance.gameObject.AddComponent<VoteManager>();
			covalence.RegisterCommand("vote", this, (_, __, ___) => CommandHandler(_, __, ___));
		}

		void Unload()
		{
			UnityEngine.Object.Destroy(ServerMgr.Instance.GetComponent<VoteManager>());
		}

		#endregion

		#region Localization

		protected override void LoadDefaultMessages() => lang.RegisterMessages(new Dictionary<string, string>
		{
			["No permission to vote"] = "You cannot participate in this vote!",
			["Voted for"] = "Your vote '{0}' has been counted, thank you for participation.",
			["Player vote cancelled"] = "Your vote '{0}' has been removed.",
			["Voted already"] = "You have already voted for '{0}'",
			["Cannot cancel vote"] = "This vote does not allows cancellation.",
			["Vote started"] = "Vote '{0}' has started! Type /vote --info {0} to get this vote information",
			["Vote finished"] = "Vote '{0}' finished.",
			["Vote cancelled"] = "Vote '{0}' was cancelled.",
			["Vote results"] = "Vote '{0}' results:",
			["Multiple results found"] = "Multiple results ({0}) found, please specify!",
			["Usage example"] = "Usage: /vote\n" +
								"             --list-votes - to list open votes\n" +
								"             --info <vote-name> - to get vote information (time left/options)\n" +
								"             [vote-name] [choice/choice-num] - to participate in open vote",
			["No votes opened"] = "No votes are currently opened",
			["No votes found"] = "No votes found with that name",
			["No option found"] = "No such option in this vote",
			["Vote list"] = "Currently open votes:",
			["Time left"] = "Time left: {0}s"
		}, this);

		void Message(IPlayer player, string key, params object[] args)
		{
			player.Message(string.Format(lang.GetMessage(key, this, player.Id), args));
		}

		#endregion

		#region Helper methods

		bool CommandHandler(IPlayer player, string command, string[] args)
		{
			if(args.Length < 1)
			{
				Message(player, "Usage example");
			}
			else
			{
				switch(args[0])
				{
					case "--list-votes": // everyone likes GNU flags, mmk? (and also they are not likely to be a vote name)
						var votes = VoteManager.instance.OpenVotes;

						if(votes.Count() < 1)
						{
							Message(player, "No votes opened");
							break;
						}

						builder.Clear();

						builder.AppendLine(lang.GetMessage("Vote list", this, player.Id));

						foreach(var vote in votes)
						{
							builder.AppendFormat("- {0} ({1}s){2}", vote.Topic, vote.SecondsLeft.ToString("#"), '\n');
						}

						player.Message(builder.ToString());
						break;

					case "--info":
						if(args.Length < 2)
						{
							Message(player, "Usage example");
						}
						else
						{
							var vote = VoteManager.instance.FindVote(args[1], false);

							if(vote == null)
							{
								Message(player, "No votes found");
								break;
							}
						}
						break;
					default:
						switch(VoteManager.instance.OpenVotes.Count())
						{
							case 0:
								Message(player, "No votes opened");
								break;

							case 1:
								var vote = VoteManager.instance.OpenVotes.First();

								if(vote == null)
								{
									Message(player, "No votes found");
									break;
								}



								break;
							default:

								break;
						}
						break;
				}
			}

			return true;
		}



		static string FindString(string pattern, IEnumerable<string> collection)
		{
			string match;

			match = collection.FirstOrDefault(str => str.Equals(pattern) || str.Equals(pattern, StringComparison.OrdinalIgnoreCase) || pattern.StartsWith(str, StringComparison.OrdinalIgnoreCase));

			return match;
		}

		static float CalculateAverageProgressive(float oldAvg, int sequenceLength, int lastNumber)
		{
			return (oldAvg * (sequenceLength - 1) + lastNumber) / sequenceLength;
		}

		#endregion

		#region API



		#endregion

		class VoteManager : FacepunchBehaviour
		{
			public static VoteManager instance;

			public List<Vote> allVotes;

			Dictionary<int, Vote> idToVote;
			Dictionary<string, List<int>> topicToId;

			Vote closestVote;

			public IEnumerable<Vote> OpenVotes => allVotes.Where(vote => vote.IsOpen);

			public event Action OnVoteListChanged;
			public event Action<string> OnVoteStarted;
			public event Action<string> OnVoteFinished;

			void Awake()
			{
				instance = this;
				allVotes = new List<Vote>();
				OnVoteListChanged += () => closestVote = FindClosestVote();
				OnVoteStarted += topic => OnVoteListChanged();
				OnVoteFinished += topic => OnVoteListChanged();
				Invoke("CheckVote", 1.0f);
			}

			void CheckVote()
			{
				if(closestVote == null)
				{
					return;
				}

				if(!(closestVote.SecondsLeft <= 0))
				{
					return;
				}

			}

			Vote FindClosestVote()
			{
				if(expireTimeToVote.Keys.Count == 0)
				{
					return null;
				}

				return expireTimeToVote.OrderBy(pair => pair.Key).First().Value;
			}

			void OnDestroy()
			{
				CancelInvoke("CheckVote");

				//finish all opened votes

				for(int i = 0; i < OpenVotes.Count(); i++)
				{

				}
			}

			public bool TryGetVote(int id, out Vote result)
			{
				return idToVote.TryGetValue(id, out result);
			}

			public bool TryGetVote(string topic, out List<Vote> result)
			{
				List<int> voteIds;

				if(topicToId.TryGetValue(topic, out voteIds))
				{
					result = new List<Vote>(voteIds.Select(id => idToVote[id]));
					return true;
				}

				result = null;
				return false;
			}

			public int AddNewVote(Vote.VoteOptions options, bool start)
			{

			}

			public class Vote
			{
				public readonly int id;

				readonly VoteOptions options;

				readonly Dictionary<string, List<string>> votes;

				int avgPlayerCountDuringVoteMeasureCount;
				float avgPlayerCountDuringVote;



				//public string VoteInfo
				//{
				//	get
				//	{
				//		builder.Clear();
				//		builder.Append(options.topic);
				//		builder.AppendLine(":");

				//		for(int i = 0; i < options.options.Length; i++)
				//		{
				//			builder.Append(i + 1);
				//			builder.Append(" ");
				//			builder.AppendLine(options.options[i]);
				//		}

				//		builder.Append("Time left: ");
				//		builder.Append(SecondsLeft.ToString("#"));
				//		builder.Append("s");

				//		return builder.ToString();
				//	}
				//}
				public float TimeLeft => 1.0f;                                          // <----------------- PLACEHOLDER

				public string Topic => options.topic;

				public IEnumerable<string> Options => options.options;

				public Vote(VoteOptions options)
				{
					id = (Time.realtimeSinceStartup + options.topic.GetHashCode()).GetHashCode();
					this.options = options;
					votes = options.options.ToDictionary(str => str, str => new List<string>());

				}

				public void OpenMessage()
				{
					if(!options.sendOpenedMessage)
					{
						return;
					}

					MessageParticipants("Vote started", Topic);
				}

				public void RemindMessage()
				{
					if(!options.sendRemindMessages)
					{
						return;
					}

					foreach(var player in GetParticipants())
					{
						player.Message(GetLocalizedInfo(player));
					}
				}

				public void CloseMessage(bool bCanceled)
				{
					if(!options.sendClosedMessage)
					{
						return;
					}

					MessageParticipants(bCanceled ? "Vote cancelled" : "Vote finished", Topic);
				}

				void MessageParticipants(string msg, params object[] args)
				{
					foreach(var player in GetParticipants())
					{
						VoteAPI.instance.Message(player, msg, args);
					}
				}

				public void UpdateAveragePlayerCount()
				{
					var currentPlayerCount = VoteAPI.instance.server.Players;

					avgPlayerCountDuringVote = CalculateAverageProgressive(avgPlayerCountDuringVote, ++avgPlayerCountDuringVoteMeasureCount, currentPlayerCount);
				}

				public IEnumerable<IPlayer> GetParticipants()
				{
					return VoteAPI.instance.players.Connected
					.Where(player => options.participants.Contains(player.Id)
					|| VoteAPI.instance.permission.GetUserGroups(player.Id).Intersect(options.participants).Count() > 0);
				}

				public string GetLocalizedInfo(IPlayer participant)
				{
					var timeLeft = VoteAPI.instance.lang.GetMessage("Time left", VoteAPI.instance, participant.Id);

					var ___seconds_left_placeholder___ = 1.0f;

					builder.Clear();
					builder.Append(options.topic);
					builder.AppendLine(":");

					for(int i = 0; i < options.options.Length; i++)
					{
						builder.Append(i + 1);
						builder.Append(" ");
						builder.AppendLine(options.options[i]);
					}

					builder.Append(timeLeft);
					builder.Append(___seconds_left_placeholder___.ToString("#"));

					return builder.ToString();
				}

				public struct VoteResult
				{
					public readonly Vote vote;

					public Dictionary<string, int> VoteCount { get; }

					public Dictionary<string, List<string>> PlayerVotes { get; }

					public int TotalVotes => VoteCount.Values.Sum();

					public readonly float playersOnServerAvg;

					public float TotalVotesPercentage => TotalVotes / playersOnServerAvg;

					public float this[string optionName]
					{
						get
						{
							if(!VoteCount.ContainsKey(optionName))
							{
								return -1;
							}

							return VoteCount[optionName] / TotalVotes;
						}
					}
				}

				public struct VoteOptions
				{
					public static VoteOptions Default = new VoteOptions("Vote", new[] { "Yes", "No" }, 60);

					public readonly string topic;

					public readonly string[] options;

					public readonly string[] participants;

					public readonly bool allowCancelVote;

					public readonly bool allowMultipleChoices;

					public readonly int duration;

					public readonly float remindFrequency;

					public readonly bool sendOpenedMessage;

					public readonly bool sendRemindMessages;

					public readonly bool sendClosedMessage;

					public readonly bool deleteAfterVote;

					public VoteOptions(
						string topic,
						string[] options,
						int duration, bool
						openMsg = true, bool
						closeMsg = true, bool
						remindMessages = true,
						bool allowCancel = true,
						bool allowMultipleChoices = false,
						bool deleteAfter = true,
						int remindFreq = 20,
						string[] participants = null
					)
					{
						if(string.IsNullOrEmpty(topic))
						{
							throw new ArgumentException("Vote topic cannot be empty", nameof(topic));
						}

						if(options == null || options.Length < 1)
						{
							throw new ArgumentException("Variants must contain atleast one entry", nameof(options));
						}

						if(duration < 1)
						{
							throw new ArgumentException("Vote cannot last less than one second", nameof(duration));
						}

						if(participants == null || participants.Length < 1)
						{
							participants = new[] { "default" };
						}

						this.topic = topic;

						this.options = options;

						this.duration = duration;

						sendOpenedMessage = openMsg;
						sendClosedMessage = closeMsg;
						sendRemindMessages = remindMessages;

						allowCancelVote = allowCancel;

						remindFrequency = remindFreq;

						this.participants = participants;

						deleteAfterVote = deleteAfter;

						this.allowMultipleChoices = allowMultipleChoices;
					}
				}
			}
		}
	}
}
