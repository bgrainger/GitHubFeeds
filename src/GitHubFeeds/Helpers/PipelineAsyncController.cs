using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace GitHubFeeds.Helpers
{
	/// <summary>
	/// <see cref="PipelineAsyncController"/> provides functionality to chain a series of async methods together to fulfill a request.
	/// </summary>
	/// <remarks>
	/// <para>The derived class must implement <c>XAsync</c> and <c>XCompleted(ActionResult result)</c> methods for each
	/// request. <c>XAsync</c> should call <c>Start</c>, passing in the parameters (to be shared across all methods) and the
	/// initial function in the pipeline. Additional functions should be added to the pipeline by calling <c>Then</c>, then <c>Finish</c>
	/// should be called at the very end. One of the methods in the pipeline must call <see cref="SetResult"/>.</para>
	/// <para><c>XCompleted</c> will be called with the <see cref="ActionResult"/> that was passed to <see cref="SetResult"/>.
	/// It should simply return its <c>result</c> parameter.</para>
	/// </remarks>
	public abstract class PipelineAsyncController : AsyncController, IDisposable
	{
		/// <summary>
		/// Starts an asynchronous pipeline.
		/// </summary>
		/// <typeparam name="TParameters">The type of the parameters that will be passed to all methods.</typeparam>
		/// <typeparam name="TResult">The type of the result of the first function in the pipeline.</typeparam>
		/// <param name="methodParameters">The parameters to the controller method.</param>
		/// <param name="startFunc">The first function in the pipeline.</param>
		/// <returns>The next stage in the asynchronous pipeline.</returns>
		protected Pipe<TParameters, TResult> Start<TParameters, TResult>(TParameters methodParameters, Func<TParameters, TResult> startFunc)
		{
			DoStart();
			return new Pipe<TParameters, TResult>(this, methodParameters, TaskUtility.Wrap(startFunc, methodParameters));
		}

		/// <summary>
		/// Starts an asynchronous pipeline.
		/// </summary>
		/// <typeparam name="TParameters">The type of the parameters that will be passed to all methods.</typeparam>
		/// <typeparam name="TResult">The type of the result of the first function in the pipeline.</typeparam>
		/// <param name="methodParameters">The parameters to the controller method.</param>
		/// <param name="startFunc">The first function in the pipeline.</param>
		/// <returns>The next stage in the asynchronous pipeline.</returns>
		protected Pipe<TParameters, TResult> Start<TParameters, TResult>(TParameters methodParameters, Func<TParameters, Task<TResult>> startFunc)
		{
			DoStart();
			return new Pipe<TParameters, TResult>(this, methodParameters, startFunc(methodParameters));
		}

		private void DoStart()
		{
			if (m_cancellationTokenSource != null)
				throw new InvalidOperationException("Start may only be called once.");

			// we will cancel this cancellation token source whenever we have created the final ActionResult
			//   and need to abort further processing
			m_cancellationTokenSource = new CancellationTokenSource();
			m_token = m_cancellationTokenSource.Token;

			AsyncManager.OutstandingOperations.Increment();
		}

		/// <summary>
		/// Sets the <see cref="ActionResult"/> for this controller.
		/// </summary>
		/// <param name="result">The result.</param>
		/// <remarks>All further processing is cancelled once this method is called.</remarks>
		protected void SetResult(ActionResult result)
		{
			if (m_result != null)
				throw new InvalidOperationException("SetResult may only be called once.");

			m_cancellationTokenSource.Cancel();
			m_result = result;
		}

		protected override void Dispose(bool disposing)
		{
			try
			{
				m_cancellationTokenSource.Dispose();
			}
			finally
			{
				base.Dispose(disposing);
			}
		}

		/// <summary>
		/// <see cref="Pipe{TParameters, TInput}"/> represents one stage in the asynchronous pipeline for a <see cref="PipelineAsyncController"/>.
		/// </summary>
		/// <typeparam name="TParameters">The type of the parameters to the controller method.</typeparam>
		/// <typeparam name="TInput">The type of the input of this pipeline stage.</typeparam>
		protected class Pipe<TParameters, TInput>
		{
			internal Pipe(PipelineAsyncController controller, TParameters parameters, Task<TInput> task)
			{
				m_controller = controller;
				m_parameters = parameters;
				m_task = task;
			}

			/// <summary>
			/// Adds the next function to the pipeline.
			/// </summary>
			/// <typeparam name="TResult">The type of the result of the next function in the pipeline.</typeparam>
			/// <param name="continuationFunction">The next function in the pipeline.</param>
			/// <returns>The next stage in the asynchronous pipeline.</returns>
			public Pipe<TParameters, TResult> Then<TResult>(Func<TParameters, TInput, TResult> continuationFunction)
			{
				return Then((p, t) => continuationFunction(p, t.Result));
			}

			/// <summary>
			/// Adds the next function to the pipeline.
			/// </summary>
			/// <typeparam name="TResult">The type of the result of the next function in the pipeline.</typeparam>
			/// <param name="continuationFunction">The next function in the pipeline.</param>
			/// <returns>The next stage in the asynchronous pipeline.</returns>
			public Pipe<TParameters, TResult> Then<TResult>(Func<TParameters, TInput, Task<TResult>> continuationFunction)
			{
				return Then((p, t) => continuationFunction(p, t.Result));
			}

			/// <summary>
			/// Adds the next function to the pipeline.
			/// </summary>
			/// <typeparam name="TResult">The type of the result of the next function in the pipeline.</typeparam>
			/// <param name="continuationFunction">The next function in the pipeline.</param>
			/// <returns>The next stage in the asynchronous pipeline.</returns>
			public Pipe<TParameters, TResult> Then<TResult>(Func<TParameters, Task<TInput>, TResult> continuationFunction)
			{
				Task<TResult> newTask = m_task.ContinueWith(t => continuationFunction(m_parameters, t), m_controller.m_token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Current);
				return new Pipe<TParameters, TResult>(m_controller, m_parameters, newTask);
			}

			/// <summary>
			/// Adds the next function to the pipeline.
			/// </summary>
			/// <typeparam name="TResult">The type of the result of the next function in the pipeline.</typeparam>
			/// <param name="continuationFunction">The next function in the pipeline.</param>
			/// <returns>The next stage in the asynchronous pipeline.</returns>
			public Pipe<TParameters, TResult> Then<TResult>(Func<TParameters, Task<TInput>, Task<TResult>> continuationFunction)
			{
				Task<TResult> newTask = m_task.ContinueWith(t => continuationFunction(m_parameters, t), m_controller.m_token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Current).Unwrap();
				return new Pipe<TParameters, TResult>(m_controller, m_parameters, newTask);
			}

			/// <summary>
			/// Adds the next function to the pipeline.
			/// </summary>
			/// <typeparam name="TResult">The type of the result of the next function in the pipeline.</typeparam>
			/// <param name="continuationFunction">The next function in the pipeline.</param>
			/// <returns>The next stage in the asynchronous pipeline.</returns>
			public ArrayPipe<TParameters, TResult> Then<TResult>(Func<TParameters, TInput, Task<TResult>[]> continuationFunction)
			{
				return Then((p, t) => continuationFunction(p, t.Result));
			}

			/// <summary>
			/// Adds the next function to the pipeline.
			/// </summary>
			/// <typeparam name="TResult">The type of the result of the next function in the pipeline.</typeparam>
			/// <param name="continuationFunction">The next function in the pipeline.</param>
			/// <returns>The next stage in the asynchronous pipeline.</returns>
			public ArrayPipe<TParameters, TResult> Then<TResult>(Func<TParameters, Task<TInput>, Task<TResult>[]> continuationFunction)
			{
				Task<Task<TResult>[]> newTask = m_task.ContinueWith(t => continuationFunction(m_parameters, t), m_controller.m_token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Current);
				return new ArrayPipe<TParameters, TResult>(m_controller, m_parameters, newTask);
			}

			/// <summary>
			/// Completes the asynchronous pipeline.
			/// </summary>
			/// <remarks><see cref="PipelineAsyncController.SetResult"/> must have been called by a function earlier in the pipeline by the time this method is invoked.</remarks>
			public void Finish()
			{
				m_task.ContinueWith(m_controller.Finish);
			}

			readonly PipelineAsyncController m_controller;
			readonly TParameters m_parameters;
			readonly Task<TInput> m_task;
		}

		/// <summary>
		/// <see cref="ArrayPipe{TParameters, TInput}"/> represents one stage in the asynchronous pipeline for a <see cref="PipelineAsyncController"/>. It
		/// consumes an earlier stage that returns an array of tasks, and is executed when all those tasks are completed.
		/// </summary>
		/// <typeparam name="TParameters">The type of the parameters to the controller method.</typeparam>
		/// <typeparam name="TInput">The type of the input of this pipeline stage.</typeparam>
		protected class ArrayPipe<TParameters, TInput>
		{
			internal ArrayPipe(PipelineAsyncController controller, TParameters parameters, Task<Task<TInput>[]> task)
			{
				m_controller = controller;
				m_parameters = parameters;
				m_task = task;
			}

			/// <summary>
			/// Adds the next function to the pipeline.
			/// </summary>
			/// <typeparam name="TResult">The type of the result of the next function in the pipeline.</typeparam>
			/// <param name="continuationFunction">The next function in the pipeline.</param>
			/// <returns>The next stage in the asynchronous pipeline.</returns>
			public Pipe<TParameters, TResult> Then<TResult>(Func<TParameters, Task<TInput>[], TResult> continuationFunction)
			{
				Task<TResult> newTask = m_task.ContinueWith(t => Task.Factory.ContinueWhenAll(t.Result, ts => continuationFunction(m_parameters, ts)),
					m_controller.m_token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Current).Unwrap();
				return new Pipe<TParameters, TResult>(m_controller, m_parameters, newTask);
			}

			readonly PipelineAsyncController m_controller;
			readonly TParameters m_parameters;
			readonly Task<Task<TInput>[]> m_task;
		}

		private void Finish(Task t)
		{
			AsyncManager.Parameters["result"] = !t.IsFaulted ? m_result :
				new HttpStatusCodeResult((int) HttpStatusCode.InternalServerError, t.Exception.Message);
			AsyncManager.OutstandingOperations.Decrement();
		}

		ActionResult m_result;
		CancellationTokenSource m_cancellationTokenSource;
		CancellationToken m_token;
	}
}
