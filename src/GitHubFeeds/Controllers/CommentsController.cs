﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Web;
using System.Web.Mvc;
using GitHubFeeds.Helpers;
using GitHubFeeds.Models;
using ServiceStack.Text;

namespace GitHubFeeds.Controllers
{
	public class CommentsController : AsyncController
	{
		public void ListAsync(string server, string user, string repo, string userName, string password)
		{
			AsyncManager.OutstandingOperations.Increment();

			// create URI for GitHub.com or GitHub Enterprise
			string uriTemplate = server == "api.github.com" ?
				@"https://{0}/repos/{1}/{2}/comments?per_page=100" :
				@"http://{0}/api/v3/repos/{1}/{2}/comments?per_page=100";
			Uri uri = new Uri(string.Format(CultureInfo.InvariantCulture, uriTemplate, Uri.EscapeDataString(server),
				Uri.EscapeDataString(user), Uri.EscapeDataString(repo)));

			HttpWebRequest request = (HttpWebRequest) WebRequest.Create(uri);
			if (userName != null && password != null)
				request.Credentials = new NetworkCredential(userName, password);
			request.Accept = "application/vnd.github-commitcomment.html+json";
			request.UserAgent = "GitHubFeeds/1.0";
			request.BeginGetResponse(ar =>
			{
				AsyncManager.Parameters["result"] = CreateListResult(request, ar, user, repo);
				AsyncManager.OutstandingOperations.Decrement();
			}, null);
		}

		public ActionResult ListCompleted(ActionResult result)
		{
			return result;
		}

		private static ActionResult CreateListResult(HttpWebRequest request, IAsyncResult asyncResult, string user, string repo)
		{
			// download comments as JSON and deserialize them
			List<GitHubComment> comments;
			using (HttpWebResponse response = GetHttpResponse(request, asyncResult))
			{
				if (response.StatusCode != HttpStatusCode.OK)
					return new HttpStatusCodeResult(500, "GitHub server returned " + response.StatusCode);

				// TODO: Use asynchronous reads on this asynchronous stream
				// TODO: Read encoding from Content-Type header; don't assume UTF-8
				using (Stream stream = response.GetResponseStream())
				using (TextReader reader = new StreamReader(stream, Encoding.UTF8, false))
					comments = JsonSerializer.DeserializeFromReader<List<GitHubComment>>(reader);
			}

			// build a feed from the comments (in reverse chronological order)
			string fullRepoName = user + "/" + repo;
			SyndicationFeed feed = new SyndicationFeed(comments
				.OrderByDescending(c => c.created_at)
				.Select(c => new SyndicationItem(c.user.login + " commented on " + fullRepoName,
					new TextSyndicationContent(CreateCommentHtml(c), TextSyndicationContentKind.Html),
					new Uri(c.html_url), c.url, c.updated_at)))
			{
				Id = "urn:x-feed:" + Uri.EscapeDataString(request.RequestUri.AbsoluteUri),
				LastUpdatedTime = comments.Max(c => c.updated_at),
				Title = new TextSyndicationContent(string.Format("Comments for {0}/{1}", user, repo)),
			};

			return new SyndicationFeedFormatterResult(feed.GetAtom10Formatter());
		}

		private static string CreateCommentHtml(GitHubComment comment)
		{
			// create URL for the commit from the comment URL
			string commentUrl = comment.html_url;
			int hashIndex = commentUrl.IndexOf('#');
			string commitUrl = hashIndex == -1 ? commentUrl : commentUrl.Substring(0, hashIndex);

			// add description based on whether the comment was on a specific line or not
			string template = comment.line == 0 ?
				@"<div>Comment on <a href=""{0}"">commit</a> at" :
				@"<div>Comment on <a href=""{0}"">{1}</a> <a href=""{0}"">L{2}</a> in";
			template += @" <a href=""{3}"">{4}</a>:
	<blockquote>
		{5}
	</blockquote>
</div>";

			return string.Format(CultureInfo.InvariantCulture, template, HttpUtility.HtmlAttributeEncode(commentUrl),
				HttpUtility.HtmlEncode(comment.path), comment.line, HttpUtility.HtmlAttributeEncode(commitUrl),
				HttpUtility.HtmlEncode(comment.commit_id.Substring(0, 8)), comment.body_html);
		}

		// See http://code.logos.com/blog/2009/06/using_if-modified-since_in_http_requests.html
		private static HttpWebResponse GetHttpResponse(HttpWebRequest request, IAsyncResult asyncResult)
		{
			try
			{
				return (HttpWebResponse) request.EndGetResponse(asyncResult);
			}
			catch (WebException ex)
			{
				// only handle protocol errors that have valid responses
				if (ex.Response == null || ex.Status != WebExceptionStatus.ProtocolError)
					throw;

				return (HttpWebResponse) ex.Response;
			}
		}

	}
}
