using System;
using System.Net;

namespace GitHubFeeds.Helpers
{
	public static class GitHubApi
	{
		/// <summary>
		/// Creates a <see cref="HttpWebRequest"/> to access the specified URI.
		/// </summary>
		/// <param name="uri">The URI.</param>
		/// <param name="userName">The (optional) username for authentication.</param>
		/// <param name="password">The (optional) password for authentication.</param>
		/// <returns>A <see cref="HttpWebRequest"/> that can be used to access the specified URI.</returns>
		public static HttpWebRequest CreateRequest(Uri uri, string userName = null, string password = null)
		{
			HttpWebRequest request = (HttpWebRequest) WebRequest.Create(uri);
			if (userName != null && password != null)
				request.Credentials = new NetworkCredential(userName, password);
			request.UserAgent = "GitHubFeeds/1.0";
			return request;
		}
	}
}
