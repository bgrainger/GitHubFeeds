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
				string eTagData = p.Version + "\n" + responseETags.Join("\n");

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

		private Task<List<GitHubComment>> GetCommits(ListParameters p, List<GitHubComment> comments)
		{
			List<Task> tasks = new List<Task>();

			if (p.Version == 2)
			{
				object lockObject = new object();
				m_commits = new Dictionary<string, GitHubCommit>();

				foreach (var commitId in comments.Select(c => c.commit_id).Distinct())
				{
					// look up commit in cache
					var commit = (GitHubCommit) HttpContext.Cache.Get("commit:" + commitId);

					if (commit != null)
					{
						// if found, store it locally (in case it gets evicted from cache)
						lock (lockObject)
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
						tasks.Add(request.GetHttpResponseAsync().ContinueWith(t =>
						{
							// parse the commit JSON
							GitHubCommit downloadedCommit;
							using (HttpWebResponse response = t.Result)
							using (Stream stream = response.GetResponseStream())
							using (TextReader reader = new StreamReader(stream, Encoding.UTF8))
								downloadedCommit = JsonSerializer.DeserializeFromReader<GitHubCommit>(reader);

							// store in cache
							string downloadedCommitId = downloadedCommit.sha;
							HttpContext.Cache.Insert("commit:" + downloadedCommitId, downloadedCommit);

							// also store it locally (in case it gets evicted from cache)
							lock (lockObject)
								m_commits.Add(downloadedCommitId, downloadedCommit);
						}));
					}
				}
			}

			return TaskUtility.ContinueWhenAll(tasks.ToArray(), t => comments);
		}

		// Creates an ATOM feed from a list of comments.
		private bool CreateFeed(ListParameters p, List<GitHubComment> comments)
		{
			// build a feed from the comments (in reverse chronological order)
			SyndicationFeed feed = new SyndicationFeed(comments.Select(c => CreateCommentItem(p, c))) 
			{
				Id = "urn:x-feed:" + Uri.EscapeDataString(p.Server) + "/" + Uri.EscapeDataString(p.User) + "/" + Uri.EscapeDataString(p.Repo),
				LastUpdatedTime = comments.Count == 0 ? DateTimeOffset.Now : comments.Max(c => c.updated_at),
				Title = new TextSyndicationContent(string.Format("Comments for {0}/{1}", p.User, p.Repo)),
			};

			SetResult(new SyndicationFeedAtomResult(feed));
			return true;
		}

		private SyndicationItem CreateCommentItem(ListParameters p, GitHubComment comment)
		{
			GitHubCommentModel model = CreateCommentModel(p.Version, comment);
			string title = p.Version == 1 ? "{0} commented on {1}/{2}".FormatWith(model.Commenter, p.User, p.Repo) :
				"{0}’s commit: {1}".FormatWith(model.Author, RenderCommitForSubject(model));

			return new SyndicationItem
			{
				Authors = { new SyndicationPerson(null, comment.user.login, null) },
				Content = new TextSyndicationContent(CreateCommentHtml(p.Version, model), TextSyndicationContentKind.Html),
				Id = comment.url.AbsoluteUri,
				LastUpdatedTime = comment.updated_at,
				Links =
					{
						SyndicationLink.CreateAlternateLink(comment.html_url),
						new SyndicationLink(new Uri(model.CommitUrl)) { RelationshipType = "related", Title = "CommitUrl" }
					},
				PublishDate = comment.created_at,
				Title = new TextSyndicationContent(HttpUtility.HtmlEncode(title), TextSyndicationContentKind.Html),
			};
		}

		private static string RenderCommitForSubject(GitHubCommentModel model)
		{
			string message = Regex.Replace(model.CommitMessage, @"\s+", " ").Trim();
			const int maxLength = 100;
			if (message.Length > maxLength)
				message = message.Substring(0, maxLength) + "\u2026";
			return message;
		}

		private GitHubCommentModel CreateCommentModel(int version, GitHubComment comment)
		{
			// create URL for the commit from the comment URL
			string commentUrl = comment.html_url.AbsoluteUri;
			int hashIndex = commentUrl.IndexOf('#');
			string commitUrl = hashIndex == -1 ? commentUrl : commentUrl.Substring(0, hashIndex);

			// create basic model
			GitHubCommentModel model = new GitHubCommentModel
			{
				CommentUrl = commentUrl,
				CommitUrl = commitUrl,
				CommentBody = new HtmlString(comment.body_html),
				CommitId = comment.commit_id.Substring(0, 8),
				FilePath = comment.path,
				LineNumber = comment.line.GetValueOrDefault() == 0 ? null : comment.line,
				Commenter = comment.user.login,
			};

			// add extra details if present
			if (version == 2)
			{
				GitHubCommit commit = m_commits[comment.commit_id];
				string author = commit.author != null ? commit.author.login : "(unknown)";

				model.Author = author;
				model.CommitMessage = commit.commit.message;
				model.CommitFiles = commit.files.Select(f => f.filename).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
			}

			return model;
		}

		// Creates the HTML for a feed item for an individual comment.
		private string CreateCommentHtml(int version, GitHubCommentModel model)
		{
			using (StringWriter writer = new StringWriter())
			{
				ViewEngineResult viewResult = ViewEngines.Engines.FindPartialView(ControllerContext, version == 2 ? "Commit" : "Simple");
				ViewContext viewContext = new ViewContext(ControllerContext, viewResult.View, new ViewDataDictionary(model), new TempDataDictionary(), writer);
				viewResult.View.Render(viewContext, writer);
				return writer.ToString();
			}
		}

		string m_requestETag;
		Dictionary<string, GitHubCommit> m_commits;
	}
}
