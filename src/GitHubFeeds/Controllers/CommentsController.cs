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
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using GitHubFeeds.Helpers;
using GitHubFeeds.Models;
using ServiceStack.Text;

namespace GitHubFeeds.Controllers
{
	public class CommentsController : AsyncController
	{
		public void ListAsync(ListParameters p)
		{
			m_requestETag = Request.Headers["If-None-Match"];

			// we will cancel this cancellation token source whenever we have created the final ActionResult
			//   and need to abort further processing
			m_cancellationTokenSource = new CancellationTokenSource();
			var token = m_cancellationTokenSource.Token;

			// start by getting the first comment (asynchronously), then chain each step in the pipeline to the
			//   previous task (using ContinueWith), specifying the cancellation token to avoid unnecessary operations
			AsyncManager.OutstandingOperations.Increment();
			Uri uri = CreateCommentsPageUri(p, 1, 1);
			HttpWebRequest request = CreateRequest(p, uri);
			request.GetHttpResponseAsync()
				.ContinueWith(t => GetCommentCount(t))
				.ContinueWith(t => GetCommentPages(p, t.Result), token)
				.ContinueWith(t => TaskUtility.ContinueWhenAll(t.Result, GetComments), token).Unwrap()
				.ContinueWith(t => CreateFeed(p, t.Result), token)
				.ContinueWith(t =>
				{
					AsyncManager.Parameters["result"] = !t.IsFaulted ? m_result :
						new HttpStatusCodeResult((int) HttpStatusCode.InternalServerError, t.Exception.Message);
					AsyncManager.OutstandingOperations.Decrement();
				});
		}

		public ActionResult ListCompleted(ActionResult result)
		{
			m_cancellationTokenSource.Dispose();
			return result;
		}

		// Creates the URI for comments for the specified repo, with a specific page size and offset.
		private static Uri CreateCommentsPageUri(ListParameters p, int pageOffset, int pageSize)
		{
			// create URI for GitHub.com or GitHub Enterprise
			string uriTemplate = p.Server == "api.github.com" ?
				@"https://{0}/repos/{1}/{2}/comments?page={3}&per_page={4}" :
				@"http://{0}/api/v3/repos/{1}/{2}/comments?page={3}&per_page={4}";
			return new Uri(string.Format(CultureInfo.InvariantCulture, uriTemplate, Uri.EscapeDataString(p.Server),
				Uri.EscapeDataString(p.User), Uri.EscapeDataString(p.Repo), pageOffset, pageSize));
		}

		// Creates a HTTP request to access the specified URI.
		private static HttpWebRequest CreateRequest(ListParameters p, Uri uri)
		{
			HttpWebRequest request = (HttpWebRequest) WebRequest.Create(uri);
			if (p.UserName != null && p.Password != null)
				request.Credentials = new NetworkCredential(p.UserName, p.Password);
			request.Accept = "application/vnd.github-commitcomment.html+json";
			request.UserAgent = "GitHubFeeds/1.0";
			return request;
		}

		// Gets the number of comments for a repo.
		private int GetCommentCount(Task<HttpWebResponse> responseTask)
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
		private Task<HttpWebResponse>[] GetCommentPages(ListParameters p, int commentCount)
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
		private List<GitHubComment> GetComments(Task<HttpWebResponse>[] tasks)
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
		private void CreateFeed(ListParameters p, List<GitHubComment> comments)
		{
			// build a feed from the comments (in reverse chronological order)
			string fullRepoName = p.User + "/" + p.Repo;
			SyndicationFeed feed = new SyndicationFeed(comments
				.Select(c => new SyndicationItem(c.user.login + " commented on " + fullRepoName,
					new TextSyndicationContent(CreateCommentHtml(c), TextSyndicationContentKind.Html),
					new Uri(c.html_url), c.url, c.updated_at)
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
		}

		// Creates the HTML for a feed item for an individual comment.
		private static string CreateCommentHtml(GitHubComment comment)
		{
			// create URL for the commit from the comment URL
			string commentUrl = comment.html_url;
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

		private void SetResult(ActionResult result)
		{
			// store the result and stop any further processing
			m_result = result;
			m_cancellationTokenSource.Cancel();
		}

		string m_requestETag;
		CancellationTokenSource m_cancellationTokenSource;
		ActionResult m_result;
	}
}
