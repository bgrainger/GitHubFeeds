using System;
using System.Net;
using System.Net.Mime;
using System.ServiceModel.Syndication;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Xml;

namespace GitHubCommentsFeed.Helpers
{
	public class SyndicationFeedFormatterResult : ActionResult
	{
		public SyndicationFeedFormatterResult(SyndicationFeedFormatter resource)
		{
			FeedFormatter = resource;
		}

		public SyndicationFeedFormatter FeedFormatter { get; set; }

		public override void ExecuteResult(ControllerContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			if (FeedFormatter == null)
				throw new InvalidOperationException("FeedFormatter must not be null");

			HttpResponseBase response = context.HttpContext.Response;
			ContentType contentType = new ContentType { MediaType = "application/atom+xml", CharSet = "utf-8" };

			response.Clear();
			response.StatusCode = (int) HttpStatusCode.OK;
			response.ContentType = contentType.ToString();

			using (XmlWriter xmlWriter = XmlWriter.Create(response.Output, new XmlWriterSettings { Encoding = Encoding.UTF8 }))
				FeedFormatter.WriteTo(xmlWriter);
		}
	}
}
