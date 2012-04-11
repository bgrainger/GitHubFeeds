using System.Collections.Generic;

namespace GitHubFeeds.Helpers
{
	/// <summary>
	/// <see cref="HeaderElement"/> represents one element in an HTTP header.
	/// </summary>
	/// <remarks>This class is loosely based on the <a href="http://hc.apache.org/httpcomponents-core-ga/httpcore/apidocs/org/apache/http/HeaderElement.html">HttpCode HeaderElement</a> API.</remarks>
	public sealed class HeaderElement
	{
		internal HeaderElement(string name, IEnumerable<KeyValuePair<string, string>> parameters)
		{
			m_name = name;
			m_parameters = new Dictionary<string, string>();
			foreach (var p in parameters)
				m_parameters.Add(p.Key, p.Value);
		}

		/// <summary>
		/// The name.
		/// </summary>
		public string Name
		{
			get { return m_name; }
		}

		/// <summary>
		/// The number of parameters.
		/// </summary>
		public int ParameterCount
		{
			get { return m_parameters.Count; }
		}

		/// <summary>
		/// Gets the value of a parameter for the specified key.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns>The parameter value associated with that key.</returns>
		public string GetParameterByName(string key)
		{
			return m_parameters[key];
		}

		/// <summary>
		/// Gets the value of a parameter for the specified key.
		/// </summary>
		/// <param name="name">The key.</param>
		/// <param name="value">On return, contains the parameter value associated with that key if the key is found; otherwise, <c>null</c>.</param>
		/// <returns><c>true</c> if the key was found; otherwise, <c>false</c>.</returns>
		public bool TryGetParameterByName(string name, out string value)
		{
			return m_parameters.TryGetValue(name, out value);
		}

		readonly string m_name;
		readonly Dictionary<string, string> m_parameters;
	}
}
