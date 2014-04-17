using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Threading;

using Segmentio;
using Segmentio.Request;
using Segmentio.Model;
using Segmentio.Exception;

namespace Segmentio.Flush
{
    internal class AsyncFlushHandler : IFlushHandler
    {
        /// <summary>
        /// Internal message queue
        /// </summary>
        private BlockingQueue<BaseAction> _queue;
        
		/// <summary>
		/// Creates a series of actions into a batch that we can send to the server
		/// </summary>
		private IBatchFactory _batchFactory;
		/// <summary>
		/// Performs the actual HTTP request to our server
		/// </summary>
		private IRequestHandler _requestHandler;

		/// <summary>
		/// The thread that is responsible for flushing the queue to the server
		/// </summary>
		private Thread _flushingThread;
		/// <summary>
		/// True to continue processign the flushing, false to dispose
		/// </summary>
		private volatile bool _continue;

		/// <summary>
		/// Marks that the current queue is empty and no flush is happening.
		/// Flush will wait for this to be signaled.
		/// </summary>
		private ManualResetEvent _idle;

        /// <summary>
        /// The max size of the queue to allow
        /// This condition prevents high performance condition causing
        /// this library to eat memory. 
        /// </summary>
		internal int MaxQueueSize { get; set; }

        internal AsyncFlushHandler(IBatchFactory batchFactory, 
		                         IRequestHandler requestHandler, 
		                         int maxQueueSize)
        {
			_queue = new BlockingQueue<BaseAction>();

			this._batchFactory = batchFactory;
			this._requestHandler = requestHandler;
            
			this.MaxQueueSize = maxQueueSize;

			_continue = true;
			
			// set that the queue is currently empty
			_idle = new ManualResetEvent(true);

			// start the flushing thread
			_flushingThread = new Thread(new ThreadStart(Loop));
			_flushingThread.Start();
        }

        public void Process(BaseAction action)
        {
			int size = _queue.Count;

            if (size > MaxQueueSize)
            {
                // drop the message
                // TODO: log it
            }
            else
            {
            	 _queue.Enqueue(action);
            }
        }

		/// <summary>
		/// Blocks until all the messages are flushed
		/// </summary>
		public void Flush() 
		{
			// wait until the queue if fully empty
			_idle.WaitOne ();
		}

		/// <summary>
		/// Loops on the flushing thread and processes the message queue
		/// </summary>
		private void Loop() 
		{
			List<BaseAction> current = new List<BaseAction>();

			// keep looping while flushing thread is active
			while (_continue) {

				do {

					// the only time we're actually not flushing
					// is if the condition that the queue is empty here
					if (_queue.Count == 0) _idle.Set ();

					// blocks and waits for a dequeue
					BaseAction action = _queue.Dequeue();

					if (action == null) 
					{
						// the queue was disposed, so we're done with this batch
						break;

					} else 
					{
						// we are no longer idle since there's messages to be processed
						_idle.Reset ();

						// add this action to the current batch
						current.Add(action);
					}
				} 
				// if we can easily see that there's still stuff in the queue
				// we'd prefer to add more to the current batch to send more
				// at once. But only if we're not disposed yet (_continue is true).
				while (_continue && _queue.Count > 0 && current.Count <= Constants.BatchIncrement);

				if (current.Count > 0) 
				{
					// we have a batch that we're trying to send
					Batch batch = _batchFactory.Create(current);

					// make the request here
					_requestHandler.MakeRequest(batch);

					// mark the current batch as null
					current = new List<BaseAction>();
				}

				// thread context switch to avoid resource contention
				Thread.Sleep (0);
			}
		}

		/// <summary>
		/// Disposes of the flushing thread and the message queue
		/// </summary>
		/// <remarks>Call <see cref="Dispose"/> when you are finished using the <see cref="Segmentio.Flush.AsyncFlushHandler"/>. The
		/// <see cref="Dispose"/> method leaves the <see cref="Segmentio.Flush.AsyncFlushHandler"/> in an unusable state.
		/// After calling <see cref="Dispose"/>, you must release all references to the
		/// <see cref="Segmentio.Flush.AsyncFlushHandler"/> so the garbage collector can reclaim the memory that the
		/// <see cref="Segmentio.Flush.AsyncFlushHandler"/> was occupying.</remarks>
		public void Dispose() 
		{
			// tell the flushing thread to stop 
			_continue = false;

			// tell the queue to stop blocking if it is currently doing so
			_queue.Dispose();
		}
	
    }
}

