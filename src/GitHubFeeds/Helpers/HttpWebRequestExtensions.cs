using System;
using System.Net;
using System.Threading.Tasks;

namespace GitHubFeeds.Helpers
{
	public static class HttpWebRequestExtensions
	{
		/// <summary>
		/// Returns a <see cref="Task{HttpWebResponse}"/> that wraps a response to an Internet request.
		/// </summary>
		/// <param name="request">The <see cref="HttpWebRequest"/>.</param>
		/// <returns>A task containing the response.</returns>
		public static Task<HttpWebResponse> GetHttpResponseAsync(this HttpWebRequest request)
		{
			return Task.Factory.FromAsync(request.BeginGetResponse, ar => request.GetHttpResponse(ar), null);
		}

		/// <summary>
		/// Gets the <see cref="HttpWebResponse"/> from an Internet resource.
		/// </summary>
		/// <param name="request">The request.</param>
		/// <param name="asyncResult">The async result.</param>
		/// <returns>A <see cref="HttpWebResponse"/> that contains the response from the Internet resource.</returns>
		/// <remarks><para>This method does not throw a <see cref="WebException"/> for "error" HTTP status codes; the caller should
		/// check the <see cref="HttpWebResponse.StatusCode"/> property to determine how to handle the response.</para>
		/// <para>See <a href="http://code.logos.com/blog/2009/06/using_if-modified-since_in_http_requests.html">Using If-Modified-Since in HTTP Requests</a>.</para></remarks>
		public static HttpWebResponse GetHttpResponse(this HttpWebRequest request, IAsyncResult asyncResult)
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
