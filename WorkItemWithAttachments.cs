using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DevicesBuildWatcherFA
{
    internal class Attachment
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        [JsonPropertyName("contentBytes")]
        public string ContentBytes { get; set; }
    }

    internal class WorkItemWithAttachments
    {
        [JsonPropertyName("workItemId")]
        public string WorkItemId { get; set; }

        [JsonPropertyName("imageAttachments")]
        public List<Attachment> ImageAttachments { get; set; }
    }
}
