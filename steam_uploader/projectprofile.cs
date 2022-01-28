using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace steam_uploader
{
    class ProjectProfile
    {
        [JsonProperty("profilename")]
        public string profilename { get; set; }

        [JsonProperty("appid")]
        public int appid { get; set; }

        [JsonProperty("description")]
        public string description { get; set; }

        [JsonProperty("builds")]
        public ProfileBuilds[] builds { get; set; }

        [JsonProperty("commandargs")]
        public string commandargs { get; set; }

        public ProjectProfile()
        {
        }
    }

    class ProfileBuilds
    {
        [JsonProperty("depotid")]
        public int depotid { get; set; }

        [JsonProperty("folder")]
        public string folder { get; set; }

        public ProfileBuilds()
        {
        }
    }
}
