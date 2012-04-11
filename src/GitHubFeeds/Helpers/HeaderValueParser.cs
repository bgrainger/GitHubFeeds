using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GitHubFeeds.Helpers
{
	/// <summary>
	/// Parses HTTP header values that are in the form <code>name *( ";" key [ "=" [ value ] ] )</code>.
	/// </summary>
	/// <remarks>This class is loosely based on the <a href="http://hc.apache.org/httpcomponents-core-ga/httpcore/apidocs/org/apache/http/message/HeaderValueParser.html">HttpCode HeaderValueParser</a> API.</remarks>
	public static class HeaderValueParser
	{
		/// <summary>
		/// Parses an HTTP header into a collection of <see cref="HeaderElement"/> objects.
		/// </summary>
		/// <param name="header">The HTTP header.</param>
		/// <returns>A collection of the parsed elements.</returns>
		public static ReadOnlyCollection<HeaderElement> ParseHeaderElements(string header)
		{
			if (header == null)
				throw new ArgumentNullException("header");

			// header elements are separated by commas
			return SplitAndTrim(header, ',')
				.Select(ParseHeaderElement)
				.Where(x => x != null)
				.ToList()
				.AsReadOnly();
		}

		private static HeaderElement ParseHeaderElement(string headerPart)
		{
			// parameters are separated by semicolons
			var components = SplitAndTrim(headerPart, ';').ToList();

			// everything after the first is treated as key/value pairs
			var parameters = new List<KeyValuePair<string, string>>();
			foreach (string nameValuePair in components.Skip(1))
			{
				// each parameter is a "key=value" pair, where both the value and equals sign are optional
				string[] parts = nameValuePair.Split(new[] { '=' }, 2);
				string key = parts[0].Trim();
				string value = parts.Length == 1 ? null : parts[1].Trim();

				// unquote quoted strings
				if (value != null && value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
					value = value.Substring(1, value.Length - 2);

				parameters.Add(new KeyValuePair<string, string>(key, value));
			}

			return new HeaderElement(components[0], parameters);
		}

		// Splits 'value' on 'separator', then returns all parts that are non-empty after trimming whitespace.
		private static IEnumerable<string> SplitAndTrim(string value, char separator)
		{
			return value.Split(separator)
				.Select(s => s.Trim())
				.Where(s => s.Length != 0);
		}
	}
}
