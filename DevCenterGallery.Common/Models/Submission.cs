﻿using System;
using System.Text.Json.Serialization;

namespace DevCenterGallary.Common.Models
{
    public class Submission
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("friendlyName")]
        public string FriendlyName { get; set; }

        [JsonPropertyName("publishedDateTime")]
        public DateTime PublishedDateTime { get; set; }

        [JsonPropertyName("updatedDateTime")]
        public DateTime UpdatedDateTime { get; set; }

        [JsonPropertyName("releaseRank")]
        public int ReleaseRank { get; set; }
    }
}
