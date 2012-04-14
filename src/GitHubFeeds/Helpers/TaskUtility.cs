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
		/// Creates a continuation <see cref="Task"/> that will be started upon the completion of a set of provided Tasks.
		/// </summary>
		/// <typeparam name="TAntecedentResult">The type of the result of the antecedent <paramref name="tasks"/>.</typeparam>
		/// <typeparam name="TResult">The type of the result that is returned by the <paramref name="continuationFunction"/> delegate and associated with the created Task{TResult}.</typeparam>
		/// <param name="tasks">The array of tasks from which to continue.</param>
		/// <param name="continuationFunction">The function delegate to execute asynchronously when all tasks in the <paramref name="tasks"/> array have completed.</param>
		/// <returns>The new continuation Task{TResult}.</returns>
		/// <remarks>This method delegates to <c>Task.Factory.ContinueWhenAll</c> unless the <paramref name="tasks"/> array is empty, in which case it runs
		/// <paramref name="continuationFunction"/> immediately.</remarks>
		public static Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction)
		{
			if (tasks.Length == 0)
				return Wrap(continuationFunction, tasks);
			else
				return Task.Factory.ContinueWhenAll(tasks, continuationFunction);
		}

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
