using System.Collections.Generic;
using System.Web;

namespace GitHubFeeds.Models
{
	public class GitHubCommentModel
	{
		public string CommentUrl { get; set; }
		public string Commenter { get; set; }
		public HtmlString CommentBody { get; set; }
		public string CommitUrl { get; set; }
		public string CommitId { get; set; }
		public string FilePath { get; set; }
		public int? LineNumber { get; set; }
		public string Author { get; set; }
		public string CommitMessage { get; set; }
		public IList<string> CommitFiles { get; set; }
	}
}
