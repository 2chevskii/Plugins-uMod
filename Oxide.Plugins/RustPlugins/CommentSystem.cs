using System.Collections.Generic;
using Oxide.Core;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;

//05.04.2019
//Example plugin made for https://umod.org/user/Quapi, allows to save and retrieve players' commentaries

namespace Oxide.Plugins
{
    [Info("Comment System", "2CHEVSKII", "0.1.0")]
    [Description("Does the comment stuff")]
    class CommentSystem : CovalencePlugin
    {

        #region -Data-


        List<PlayerData> database { get; set; }

        class PlayerData
        {
            [JsonProperty(PropertyName = "Steamid")]
            public string userid { get; set; }
            [JsonProperty(PropertyName = "Comments list")]
            public List<string> comments { get; set; }
        }


        #endregion

        #region -Methods-


        void AddNewComment(string id, string newcomment)
        {
            try
            {
                database = Interface.Oxide.DataFileSystem.GetFile("CommentSystem").ReadObject<List<PlayerData>>();
                if(database == null)
                    throw new JsonException();
            }
            catch { database = new List<PlayerData>(); }

            if(database.Find(p => p.userid == id) != null)
                database.Find(p => p.userid == id).comments.Add(newcomment);
            else
                database.Add(new PlayerData
                {
                    userid = id,
                    comments = new List<string>
                    {
                        newcomment
                    }
                });

            Interface.Oxide.DataFileSystem.GetFile("CommentSystem").WriteObject(database);
        }

        string RetrievePlayerComments(string id)
        {
            try
            {
                database = Interface.Oxide.DataFileSystem.GetFile("CommentSystem").ReadObject<List<PlayerData>>();
                if(database == null)
                    throw new JsonException();
            }
            catch { database = new List<PlayerData>(); }

            var playerdata = database.Find(p => p.userid == id);

            if(playerdata == null)
                return "No comments found by that player";
            else
            {
                var returnvalue = $"Player {covalence.Players.FindPlayer(playerdata.userid).Name} comments:\n";
                foreach(var comment in playerdata.comments) returnvalue += $"\"{comment}\"" + "\n";
                return returnvalue;
            }
                
        }


        #endregion

        #region -Command-


        [Command("comment")]
        void CmdComment(IPlayer player, string command, string[] args)
        {
            if(player == null) return; //checks
            if(args.Length < 2 || args == null) player.Reply("Wrong command usage. Try /comment new <comment> or /comment get <userid or playername>");
            else
                switch(args[0])
                {
                    case "new":
                        string newcomment = string.Empty;
                        int index = -1;
                        foreach(var arg in args)
                        {
                            ++index;
                            if(index == 0) continue;
                            else newcomment += arg + " ";
                        }
                        AddNewComment(player.Id, newcomment);
                        player.Reply($"New comment added successfully! Text:\n\"{newcomment}\"");
                        break;
                    case "get":
                        var _player = covalence.Players.FindPlayer(args[1]);
                        if(_player != null)
                            player.Reply(RetrievePlayerComments(_player.Id));
                        else
                            player.Reply("No player found!");
                        break;

                    default:
                        player?.Reply("Wrong command usage. Try /comment new <comment> or /comment get <userid or playername>");
                        break;
                }
        }


        #endregion

    }
}
