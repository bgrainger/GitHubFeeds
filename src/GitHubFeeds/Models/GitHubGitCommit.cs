using System;

namespace GitHubFeeds.Models
{
	internal sealed class GitHubGitCommit
	{
		public string message { get; set; }
		public Uri uri { get; set; }
	}
}
