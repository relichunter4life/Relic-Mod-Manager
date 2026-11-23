// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RelicMods.Manager
{
    public static class NexusData
    {
        public const string GameDomain = "runescapedragonwilds";
        public const string GameId = "7597";
        const string Endpoint = "https://api.nexusmods.com/v2/graphql";
        static readonly HttpClient Http;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static NexusData()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("RelicMods/" + Program.Version);
            Http.DefaultRequestHeaders.Add("Application-Name", "Relic Mod Manager");
            Http.DefaultRequestHeaders.Add("Application-Version", Program.Version);
        }

        static async Task<Dictionary<string, object>> Query(string query, object variables)
        {
            string body = Json.Serialize(new { query, variables });
            using (var res = await Http.PostAsync(Endpoint, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false))
            {
                string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                var d = Json.Deserialize<Dictionary<string, object>>(text);
                if (d == null || !d.ContainsKey("data") || d["data"] == null) throw new InvalidOperationException("Nexus data feed: " + text.Substring(0, Math.Min(200, text.Length)));
                return (Dictionary<string, object>)d["data"];
            }
        }

        public sealed class ModInfo
        {
            public string ModId, Name, Version, UpdatedAt, Picture, Summary, Author, Uploader, Category;
        }

        public static async Task<List<ModInfo>> Mods(IEnumerable<string> modIds)
        {
            var ids = modIds.Where(x => int.TryParse(x, out _)).Distinct().ToList();
            var all = new List<ModInfo>();
            for (int i = 0; i < ids.Count; i += 50)
            {
                string list = string.Join(", ", ids.Skip(i).Take(50).Select(x => "{gameDomain: \"" + GameDomain + "\", modId: " + int.Parse(x) + "}"));
                var data = await Query("query { legacyModsByDomain(ids: [" + list + "]) { nodes { modId name version updatedAt pictureUrl summary author uploader { name } category } } }",
                    new Dictionary<string, object>()).ConfigureAwait(false);
                var nodes = J.Arr((data["legacyModsByDomain"] as Dictionary<string, object>)?["nodes"]);
                foreach (Dictionary<string, object> n in nodes.OfType<Dictionary<string, object>>())
                    all.Add(new ModInfo
                    {
                        ModId = Convert.ToString(n["modId"]),
                        Name = n["name"] as string,
                        Version = n["version"] as string,
                        UpdatedAt = n["updatedAt"] as string,
                        Picture = n.ContainsKey("pictureUrl") ? n["pictureUrl"] as string : null,
                        Summary = n.ContainsKey("summary") ? n["summary"] as string : null,
                        Author = n.ContainsKey("author") ? n["author"] as string : null,
                        Uploader = n.ContainsKey("uploader") && n["uploader"] is Dictionary<string, object> u ? u["name"] as string : null,
                        Category = n.ContainsKey("category") ? n["category"] as string : null,
                    });
            }
            return all;
        }

        public static async Task<string> FileVersion(string modId, string fileId)
        {
            var data = await Query("query($m: ID!, $g: ID!) { modFiles(modId: $m, gameId: $g) { fileId name version } }",
                new Dictionary<string, object> { { "m", modId }, { "g", GameId } }).ConfigureAwait(false);
            var files = J.Arr(data["modFiles"]);
            foreach (Dictionary<string, object> f in files.OfType<Dictionary<string, object>>())
                if (Convert.ToString(f["fileId"]) == fileId) return f["version"] as string;
            return null;
        }
        const string Fields = "modId name version summary author uploader { name } pictureUrl thumbnailUrl downloads endorsements updatedAt createdAt category";

        public static async Task<object> Search(string text, string sort, int offset)
        {
            string order;
            switch (sort)
            {
                case "downloads": order = "{downloads: {direction: DESC}}"; break;
                case "updated": order = "{updatedAt: {direction: DESC}}"; break;
                case "new": order = "{createdAt: {direction: DESC}}"; break;
                default: order = "{endorsements: {direction: DESC}}"; break;
            }
            string filter = "gameDomainName: [{value: \"" + GameDomain + "\"}]";
            text = (text ?? "").Trim();
            if (text.Length > 0)
            {
                string clean = new string(text.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '\'').ToArray()).Trim();
                if (clean.Length > 0) filter += ", nameStemmed: [{value: \"" + clean.Replace("\"", "") + "\", op: MATCHES}]";
            }
            var data = await Query("query { mods(filter: {" + filter + "}, sort: [" + order + "], count: 24, offset: " + Math.Max(0, offset) + ") { totalCount nodes { " + Fields + " } } }",
                new Dictionary<string, object>()).ConfigureAwait(false);
            var mods = data["mods"] as Dictionary<string, object>;
            return new { total = mods?["totalCount"], nodes = mods?["nodes"] ?? new object[0], offset };
        }

        public static async Task<List<ModInfo>> FindByName(string name)
        {
            var list = new List<ModInfo>();
            string clean = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '\'').ToArray()).Trim();
            if (clean.Length < 3) return list;
            var data = await Query("query($v: String!) { mods(filter: {gameDomainName: [{value: \"" + GameDomain + "\"}], nameStemmed: [{value: $v, op: MATCHES}]}, count: 6) { nodes { modId name version updatedAt pictureUrl summary author uploader { name } } } }",
                new Dictionary<string, object> { { "v", clean } }).ConfigureAwait(false);
            foreach (Dictionary<string, object> n in J.Arr((data["mods"] as Dictionary<string, object>)?["nodes"]).OfType<Dictionary<string, object>>())
                list.Add(new ModInfo
                {
                    ModId = Convert.ToString(n["modId"]), Name = n["name"] as string, Version = n["version"] as string, UpdatedAt = n["updatedAt"] as string,
                    Picture = n["pictureUrl"] as string, Summary = n["summary"] as string, Author = n["author"] as string,
                    Uploader = n["uploader"] is Dictionary<string, object> u ? u["name"] as string : null,
                });
            return list;
        }
    }
}
