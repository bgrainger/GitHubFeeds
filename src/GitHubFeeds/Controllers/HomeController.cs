using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Mvc;
using GitHubFeeds.Helpers;
using GitHubFeeds.Models;

namespace GitHubFeeds.Controllers
{
    public class HomeController : PipelineAsyncController
    {
        public void AboutAsync()
        {
			// get basic information about the web site's primary assembly
        	Assembly currentAssembly = Assembly.GetExecutingAssembly();
        	AboutViewModel model = new AboutViewModel
        	{
        		Version = currentAssembly.GetName().Version.ToString(),
        		BuildDate = System.IO.File.GetLastWriteTime(currentAssembly.Location).ToUniversalTime(),
        	};

			// start async request for remaining rate limit
			ViewBag.Title = "GitHubFeeds";
			Start(model, MakeApiRequest)
        		.Then(GetApiRateLimit)
        		.Finish();
        }

		public ActionResult AboutCompleted(ActionResult result)
		{
			return result;
		}

		private Task<HttpWebResponse> MakeApiRequest(AboutViewModel model)
		{
			// get the root API URI, which will return the X-RateLimit-Remaining HTTP header
			Uri uri = new Uri("https://api.github.com");
			HttpWebRequest request = GitHubApi.CreateRequest(uri);
			return request.GetHttpResponseAsync();
		}

		private bool GetApiRateLimit(AboutViewModel model, Task<HttpWebResponse> responseTask)
		{
			// parse the X-RateLimit-Remaining HTTP header from the response
			using (HttpWebResponse response = responseTask.Result)
				model.RateLimitRemaining = int.Parse(response.Headers["X-RateLimit-Remaining"]);
			SetResult(View("About", model));
			return true;
		}
    }
}
