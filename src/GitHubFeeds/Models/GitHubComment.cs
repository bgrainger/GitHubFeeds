using System;

namespace GitHubFeeds.Models
{
	internal sealed class GitHubComment
	{
		public Uri html_url { get; set; }
		public DateTime updated_at { get; set; }
		public int? line { get; set; }
		public GitHubUser user { get; set; }
		public Uri url { get; set; }
		public string body_html { get; set; }
		public string commit_id { get; set; }
		public DateTime created_at { get; set; }
		public string path { get; set; }
		public int id { get; set; }
		public int? position { get; set; }
	}
}
