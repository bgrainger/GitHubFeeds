using System;
using System.Net;
using System.Net.Mime;
using System.ServiceModel.Syndication;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Xml;

namespace GitHubFeeds.Helpers
{
	public sealed class SyndicationFeedAtomResult : ActionResult
	{
		public SyndicationFeedAtomResult(SyndicationFeed feed)
		{
			Feed = feed;
		}

		public SyndicationFeed Feed { get; set; }

		public override void ExecuteResult(ControllerContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			if (Feed == null)
				throw new InvalidOperationException("FeedFormatter must not be null");

			HttpResponseBase response = context.HttpContext.Response;
			ContentType contentType = new ContentType { MediaType = "application/atom+xml", CharSet = "utf-8" };

			response.Clear();
			response.StatusCode = (int) HttpStatusCode.OK;
			response.ContentType = contentType.ToString();

			SyndicationFeedFormatter formatter = Feed.GetAtom10Formatter();
			using (XmlWriter xmlWriter = XmlWriter.Create(response.Output, new XmlWriterSettings { Encoding = Encoding.UTF8 }))
				formatter.WriteTo(xmlWriter);
		}
	}
}
