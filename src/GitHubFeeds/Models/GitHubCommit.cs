using System;

namespace GitHubFeeds.Models
{
	internal sealed class GitHubCommit
	{
		public Uri url { get; set; }
		public string sha { get; set; }
		public GitHubUser author { get; set; }
		public GitHubUser committer { get; set; }
		public GitHubGitCommit commit { get; set; }
		public GitHubFile[] files { get; set; }
	}
}
