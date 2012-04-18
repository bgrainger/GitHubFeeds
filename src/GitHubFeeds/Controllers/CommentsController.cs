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
				string eTagData = responseETags.Join("\n");

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

		// Creates an ATOM feed from a list of comments.
		private bool CreateFeed(ListParameters p, List<GitHubComment> comments)
		{
			// build a feed from the comments (in reverse chronological order)
			string fullRepoName = p.User + "/" + p.Repo;
			SyndicationFeed feed = new SyndicationFeed(comments
				.Select(c => new SyndicationItem(c.user.login + " commented on " + fullRepoName,
					new TextSyndicationContent(CreateCommentHtml(c), TextSyndicationContentKind.Html),
					c.html_url, c.url.AbsoluteUri, c.updated_at)
					{
						Authors = { new SyndicationPerson(null, c.user.login, null)},
						PublishDate = c.created_at,
					}
				))
			{
				Id = "urn:x-feed:" + Uri.EscapeDataString(p.Server) + "/" + Uri.EscapeDataString(p.User) + "/" + Uri.EscapeDataString(p.Repo),
				LastUpdatedTime = comments.Count == 0 ? DateTimeOffset.Now : comments.Max(c => c.updated_at),
				Title = new TextSyndicationContent(string.Format("Comments for {0}/{1}", p.User, p.Repo)),
			};

			SetResult(new SyndicationFeedAtomResult(feed));
			return true;
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
				@"<div>Comment on <a href=""{0}"">commit</a> at" :
				@"<div>Comment on <a href=""{0}"">{1}</a> <a href=""{0}"">L{2}</a> in";
			template += @" <a href=""{3}"">{4}</a>:	<blockquote>{5}</blockquote></div>";

			return string.Format(CultureInfo.InvariantCulture, template, HttpUtility.HtmlAttributeEncode(commentUrl),
				HttpUtility.HtmlEncode(comment.path), comment.line, HttpUtility.HtmlAttributeEncode(commitUrl),
				HttpUtility.HtmlEncode(comment.commit_id.Substring(0, 8)), comment.body_html);
		}

		string m_requestETag;
	}
}
