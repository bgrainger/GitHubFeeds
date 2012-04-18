using System;

namespace GitHubFeeds.Models
{
	public sealed class AboutViewModel
	{
		public string Version { get; set; }
		public DateTime BuildDate { get; set; }
		public int RateLimitRemaining { get; set; }
	}
}
