using System.Web.Mvc;
using System.Web.Routing;

namespace GitHubFeeds
{
	public class MvcApplication : System.Web.HttpApplication
	{
		public static void RegisterGlobalFilters(GlobalFilterCollection filters)
		{
			filters.Add(new HandleErrorAttribute());
		}

		public static void RegisterRoutes(RouteCollection routes)
		{
			routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

			routes.MapRoute(
				"About",
				"",
				new { controller = "Home", action = "About" }
			);

			routes.MapRoute(
				"Comments",
				"comments/{server}/{user}/{repo}",
				new { controller = "Comments", action = "List", version = 1 }
			);

			routes.MapRoute(
				"CommitComments",
				"comments/v2/{server}/{user}/{repo}",
				new { controller = "Comments", action = "List", version = 2 }
			);
		}

		protected void Application_Start()
		{
			AreaRegistration.RegisterAllAreas();

			RegisterGlobalFilters(GlobalFilters.Filters);
			RegisterRoutes(RouteTable.Routes);
		}
	}
}
