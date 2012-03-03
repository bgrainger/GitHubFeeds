using System.Web.Mvc;

namespace GitHubCommentsFeed.Controllers
{
    public class CommentsController : Controller
    {
        public ActionResult List(string server, string user, string repo)
        {
        	return Content("server = " + server + ", user = " + user + ", repo = " + repo);
        }
    }
}
