using System;

namespace GitHubFeeds.Models
{
	internal sealed class GitHubUser
	{
		public string gravatar_id { get; set; }
		public Uri url { get; set; }
		public Uri avatar_url { get; set; }
		public string login { get; set; }
		public int id { get; set; }
	}
}
