using System.Web.Mvc;

namespace GitHubCommentsFeed.Controllers
{
    public class CommentsController : AsyncController
    {
		public void ListAsync(string server, string user, string repo)
		{
			AsyncManager.OutstandingOperations.Increment();
			AsyncManager.Parameters["result"] = "server = " + server + ", user = " + user + ", repo = " + repo;
			AsyncManager.OutstandingOperations.Decrement();
		}

		public ActionResult ListCompleted(string result)
		{
			return Content(result);
		}
    }
}
