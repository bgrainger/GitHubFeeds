using System;
using System.Threading.Tasks;

namespace GitHubFeeds.Helpers
{
	/// <summary>
	/// Helper methods for working with <see cref="Task"/>.
	/// </summary>
	public static class TaskUtility
	{
		/// <summary>
		/// Creates a Task{TResult} that represents the results of running <paramref name="func"/> immediately.
		/// </summary>
		/// <typeparam name="TResult">The type of the result of <paramref name="func"/>.</typeparam>
		/// <typeparam name="T1">The type of the first parameter of <paramref name="func"/>.</typeparam>
		/// <param name="func">The function to run.</param>
		/// <param name="arg1">The first argument to be passed to <paramref name="func"/>.</param>
		/// <returns>A Task{TResult} that contains the results of running <paramref name="func"/>.</returns>
		public static Task<TResult> Wrap<T1, TResult>(Func<T1, TResult> func, T1 arg1)
		{
			TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
			try
			{
				tcs.SetResult(func(arg1));
			}
			catch (Exception ex)
			{
				tcs.SetException(ex);
			}
			return tcs.Task;
		}
	}
}
