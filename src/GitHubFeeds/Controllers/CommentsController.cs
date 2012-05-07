using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using GitHubFeeds.Helpers;
using GitHubFeeds.Models;
using ServiceStack.Text;

namespace GitHubFeeds.Controllers
{
	public class CommentsController : PipelineAsyncController
	{
		public void ListAsync(ListParameters p)
		{
			// save the client's ETag, which will be used to determine if a "Not Modified" result should be returned
			m_requestETag = Request.Headers["If-None-Match"];

			// start the asynchronous processing pipeline
			Start(p, GetFirstComment)
				.Then(GetCommentCount)
				.Then(GetCommentPages)
				.Then(GetComments)
				.Then(RequestCommits)
				.Then(GetCommits)
				.Then(CreateFeed)
				.Finish();
		}

		public ActionResult ListCompleted(ActionResult result)
		{
			return result;
		}

		// Creates the URI for comments for the specified repo, with a specific page size and offset.
		private static Uri CreateCommentsPageUri(ListParameters p, int pageOffset, int pageSize)
		{
			// create URI for GitHub.com or GitHub Enterprise
			string baseUri = p.Server == "api.github.com" ? @"https://{0}/repos/" : @"http://{0}/api/v3/repos/";
			string uriTemplate = baseUri + @"{1}/{2}/comments?page={3}&per_page={4}";
			return new Uri(string.Format(CultureInfo.InvariantCulture, uriTemplate, Uri.EscapeDataString(p.Server),
				Uri.EscapeDataString(p.User), Uri.EscapeDataString(p.Repo), pageOffset, pageSize));
		}

		// Creates a HTTP request to access the specified URI.
		private static HttpWebRequest CreateRequest(ListParameters p, Uri uri)
		{
			HttpWebRequest request = GitHubApi.CreateRequest(uri, p.UserName, p.Password);
			request.Accept = "application/vnd.github-commitcomment.html+json";
			return request;
		}

		// Starts a WebRequest to retrieve the first comment for a repo.
		private static Task<HttpWebResponse> GetFirstComment(ListParameters p)
		{
			Uri uri = CreateCommentsPageUri(p, 1, 1);
			HttpWebRequest request = CreateRequest(p, uri);
			return request.GetHttpResponseAsync();
		}

		// Gets the number of comments for a repo.
		private int GetCommentCount(ListParameters p, Task<HttpWebResponse> responseTask)
		{
			string linkHeader;
			using (HttpWebResponse response = responseTask.Result)
			{
				if (response.StatusCode == HttpStatusCode.NotFound)
				{
					SetResult(HttpNotFound());
					return 0;
				}
				else if (response.StatusCode != HttpStatusCode.OK)
				{
					throw new ApplicationException("GitHub server returned " + response.StatusCode);
				}

				linkHeader = response.Headers["Link"];
			}

			int commentCount = 0;

			if (!string.IsNullOrWhiteSpace(linkHeader))
			{
				foreach (HeaderElement element in HeaderValueParser.ParseHeaderElements(linkHeader))
				{
					string rel;
					if (element.TryGetParameterByName("rel", out rel) && rel == "last")
					{
						// HACK: parse the page number out of the URL that links to the last page; this will be the total number of comments
						Match match = Regex.Match(element.Name, @"[?&]page=(\d+)");
						Debug.Assert(match.Success, "match.Success", "Page number could not be parsed from URL.");
						commentCount = int.Parse(match.Groups[1].Value);
					}
				}
			}

			return commentCount;
		}

		// Returns an array of tasks that will download the last 50 comments for a particular repo.
		private static Task<HttpWebResponse>[] GetCommentPages(ListParameters p, int commentCount)
		{
			// determine the offset of the last page of comments
			const int c_pageSize = 100;
			int lastPageOffset = (commentCount - 1) / c_pageSize + 1;

			// request one or two pages (as necessary) to get at least 50 comments
			List<Uri> uris = new List<Uri>();
			if (commentCount > 50 && (commentCount % c_pageSize < 50))
			{
				// there are at least 50 items, but the last page doesn't contain at least 50
				uris.Add(CreateCommentsPageUri(p, lastPageOffset - 1, c_pageSize));
			}

			// get the last page of comments
			uris.Add(CreateCommentsPageUri(p, lastPageOffset, c_pageSize));

			// return a task for each URI
			return uris.Select(u => CreateRequest(p, u).GetHttpResponseAsync()).ToArray();
		}

		// Merges the comments returned from multiple HTTP requests and returns the last 50.
		private List<GitHubComment> GetComments(ListParameters p, Task<HttpWebResponse>[] tasks)
		{
			// concatenate all the response URIs and ETags; we will use this to build our own ETag
			var responseETags = new List<string>();

			// download comments as JSON and deserialize them
			List<GitHubComment> comments = new List<GitHubComment>();
			foreach (Task<HttpWebResponse> task in tasks)
			{
				using (HttpWebResponse response = task.Result)
				{
					if (response.StatusCode != HttpStatusCode.OK)
						throw new ApplicationException("GitHub server returned " + response.StatusCode);

					// if the response has an ETag, add it to the list of all ETags
					string eTag = response.Headers[HttpResponseHeader.ETag];
					if (!string.IsNullOrEmpty(eTag))
						responseETags.Add(response.ResponseUri.AbsoluteUri + ":" + eTag);

					// TODO: Use asynchronous reads on this asynchronous stream
					// TODO: Read encoding from Content-Type header; don't assume UTF-8
					using (Stream stream = response.GetResponseStream())
					using (TextReader reader = new StreamReader(stream, Encoding.UTF8))
						comments.AddRange(JsonSerializer.DeserializeFromReader<List<GitHubComment>>(reader));
				}
			}

			// if each response had an ETag, build our own ETag from that data
			if (responseETags.Count == tasks.Length)
			{
				// concatenate all the ETag data
				string eTagData = p.View + "\n" + responseETags.Join("\n");

				// hash it
				byte[] md5;
				using (MD5 hash = MD5.Create())
					md5 = hash.ComputeHash(Encoding.UTF8.GetBytes(eTagData));

				// the ETag is the quoted MD5 hash
				string responseETag = "\"" + string.Join("", md5.Select(by => by.ToString("x2", CultureInfo.InvariantCulture))) + "\"";
				Response.AppendHeader("ETag", responseETag);

				if (m_requestETag == responseETag)
				{
					SetResult(new HttpStatusCodeResult((int) HttpStatusCode.NotModified));
					return null;
				}
			}

			return comments
				.OrderByDescending(c => c.created_at)
				.Take(50)
				.ToList();
		}

		private Task<HttpWebResponse>[] RequestCommits(ListParameters p, List<GitHubComment> comments)
		{
			m_comments = comments;

			List<Task<HttpWebResponse>> tasks = new List<Task<HttpWebResponse>>();

			if (p.View == "full")
			{
				m_commits = new Dictionary<string, GitHubCommit>();

				foreach (var commitId in comments.Select(c => c.commit_id).Distinct())
				{
					// look up commit in cache
					var commit = (GitHubCommit) HttpContext.Cache.Get("commit:" + commitId);

					if (commit != null)
					{
						// if found, store it locally (in case it gets evicted from cache)
						m_commits.Add(commitId, commit);
					}
					else
					{
						// if not found, request it
						string baseUri = p.Server == "api.github.com" ? @"https://{0}/repos/" : @"http://{0}/api/v3/repos/";
						string uriTemplate = baseUri + @"{1}/{2}/commits/{3}";
						Uri uri = new Uri(string.Format(CultureInfo.InvariantCulture, uriTemplate, Uri.EscapeDataString(p.Server),
							Uri.EscapeDataString(p.User), Uri.EscapeDataString(p.Repo), Uri.EscapeDataString(commitId)));

						var request = GitHubApi.CreateRequest(uri, p.UserName, p.Password);
						tasks.Add(request.GetHttpResponseAsync());
					}
				}
			}

			return tasks.ToArray();
		}

		private List<GitHubComment> GetCommits(ListParameters p, Task<HttpWebResponse>[] responseTasks)
		{
			foreach (var responseTask in responseTasks)
			{
				GitHubCommit commit;
				using (var response = responseTask.Result)
				{
					using (Stream stream = response.GetResponseStream())
					using (TextReader reader = new StreamReader(stream, Encoding.UTF8))
						commit = JsonSerializer.DeserializeFromReader<GitHubCommit>(reader);
				}

				string commitId = commit.sha;
				HttpContext.Cache.Insert("commit:" + commitId, commit);
				m_commits.Add(commitId, commit);
			}

			return m_comments;
		}

		// Creates an ATOM feed from a list of comments.
		private bool CreateFeed(ListParameters p, List<GitHubComment> comments)
		{
			// build a feed from the comments (in reverse chronological order)
			string fullRepoName = p.User + "/" + p.Repo;
			SyndicationFeed feed = new SyndicationFeed(comments.Select(c =>
				p.View == "full" ? CreateFullCommentItem(c, fullRepoName) : CreateCommentItem(c, fullRepoName)))
			{
				Id = "urn:x-feed:" + Uri.EscapeDataString(p.Server) + "/" + Uri.EscapeDataString(p.User) + "/" + Uri.EscapeDataString(p.Repo),
				LastUpdatedTime = comments.Count == 0 ? DateTimeOffset.Now : comments.Max(c => c.updated_at),
				Title = new TextSyndicationContent(string.Format("Comments for {0}/{1}", p.User, p.Repo)),
			};

			SetResult(new SyndicationFeedAtomResult(feed));
			return true;
		}

		private SyndicationItem CreateCommentItem(GitHubComment comment, string fullRepoName)
		{
			return new SyndicationItem(comment.user.login + " commented on " + fullRepoName,
				new TextSyndicationContent(CreateCommentHtml(comment), TextSyndicationContentKind.Html),
				comment.html_url, comment.url.AbsoluteUri, comment.updated_at)
			{
				Authors = { new SyndicationPerson(null, comment.user.login, null) },
				PublishDate = comment.created_at,
			};
		}

		// Creates the HTML for a feed item for an individual comment.
		private static string CreateCommentHtml(GitHubComment comment)
		{
			// create URL for the commit from the comment URL
			string commentUrl = comment.html_url.AbsoluteUri;
			int hashIndex = commentUrl.IndexOf('#');
			string commitUrl = hashIndex == -1 ? commentUrl : commentUrl.Substring(0, hashIndex);

			// add description based on whether the comment was on a specific line or not
			string template = comment.line.GetValueOrDefault() == 0 ?
				@"<div><a href=""{0}"">Comment</a> on" :
				@"<div>Comment on <a href=""{0}"">{1}</a> <a href=""{0}"">L{2}</a> in";
			template += @" <a href=""{3}"">{4}</a>:	<blockquote>{5}</blockquote></div>";

			return string.Format(CultureInfo.InvariantCulture, template, HttpUtility.HtmlAttributeEncode(commentUrl),
				HttpUtility.HtmlEncode(comment.path), comment.line, HttpUtility.HtmlAttributeEncode(commitUrl),
				HttpUtility.HtmlEncode(comment.commit_id.Substring(0, 8)), comment.body_html);
		}

		private SyndicationItem CreateFullCommentItem(GitHubComment comment, string fullRepoName)
		{
			GitHubUser author = m_commits[comment.commit_id].author;
			string authorName = author != null ? author.login : "(unknown)";

			return new SyndicationItem("Comment on {0}’s commit".FormatWith(authorName),
				new TextSyndicationContent(CreateFullCommentHtml(comment), TextSyndicationContentKind.Html),
				comment.html_url, comment.url.AbsoluteUri, comment.updated_at)
			{
				Authors = { new SyndicationPerson(null, comment.user.login, null) },
				PublishDate = comment.created_at,
			};
		}

		// Creates the HTML for a feed item for an individual comment.
		private string CreateFullCommentHtml(GitHubComment comment)
		{
			string commentHtml = CreateCommentHtml(comment);

			GitHubCommit commit = m_commits[comment.commit_id];
			StringBuilder commitHtml = new StringBuilder();
			commitHtml.AppendFormat(CultureInfo.InvariantCulture, "<div>{0}</div><ul>", HttpUtility.HtmlEncode(commit.commit.message));
			int cutoff = commit.files.Length == 10 ? 10 : 9;
			foreach (GitHubFile file in commit.files.OrderBy(f => f.filename, StringComparer.OrdinalIgnoreCase).Take(cutoff))
				commitHtml.AppendFormat("<li>{0}</li>", HttpUtility.HtmlEncode(file.filename));
			if (commit.files.Length > cutoff)
				commitHtml.AppendFormat("<li>… and {0:n0} more</li>", commit.files.Length - cutoff);
			commitHtml.Append("</ul>");

			string commenter = comment.user.login;
			string author = commit.author != null ? commit.author.login : "(unknown)";

			return "<h3>Comment by {0}</h3>".FormatWith(HttpUtility.HtmlEncode(commenter)) +
				commentHtml +
				"<h3>Commit {0} by {1}</h3>".FormatWith(HttpUtility.HtmlEncode(commit.sha.Substring(0, 8)), HttpUtility.HtmlEncode(author)) +
				commitHtml.ToString();
		}

		string m_requestETag;
		List<GitHubComment> m_comments;
		Dictionary<string, GitHubCommit> m_commits;
	}
}
