﻿// Copyright (c) 2016 James Skimming. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace Castle.DynamicProxy
{
    using System;
    using System.Collections.Concurrent;
    using System.Reflection;
    using System.Threading.Tasks;

    /// <summary>
    /// A base type for an <see cref="IAsyncInterceptor"/> to provided a simplified solution of method
    /// <see cref="IInvocation"/> by enforcing only two types of interception, both asynchronous.
    /// </summary>
    public abstract class AsyncInterceptorBase : IAsyncInterceptor
    {


        private static readonly MethodInfo InterceptSynchronousMethodInfo =
            typeof(AsyncInterceptorBase)
                .GetMethod(nameof(InterceptSynchronousResult), BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly ConcurrentDictionary<Type, GenericSynchronousHandler> GenericSynchronousHandlers =
            new ConcurrentDictionary<Type, GenericSynchronousHandler>
            {
                [typeof(void)] = InterceptSynchronousVoid,
            };

        private delegate void GenericSynchronousHandler(AsyncInterceptorBase me, IInvocation invocation);

        /// <summary>
        /// Intercepts a synchronous method <paramref name="invocation"/>.
        /// </summary>
        /// <param name="invocation">The method invocation.</param>
        public void InterceptSynchronous(IInvocation invocation)
        {
            Type returnType = invocation.Method.ReturnType;
            GenericSynchronousHandler handler = GenericSynchronousHandlers.GetOrAdd(returnType, CreateHandler);
            handler(this, invocation);
        }

        /// <summary>
        /// Intercepts an asynchronous method <paramref name="invocation"/> with return type of <see cref="Task"/>.
        /// </summary>
        /// <param name="invocation">The method invocation.</param>
        public void InterceptAsynchronous(IInvocation invocation)
        {
            invocation.ReturnValue = InterceptAsync(invocation, ProceedAsynchronous);
        }

        /// <summary>
        /// Intercepts an asynchronous method <paramref name="invocation"/> with return type of <see cref="Task{T}"/>.
        /// </summary>
        /// <typeparam name="TResult">The type of the <see cref="Task{T}"/> <see cref="Task{T}.Result"/>.</typeparam>
        /// <param name="invocation">The method invocation.</param>
        public  void InterceptAsynchronous<TResult>(IInvocation invocation)
        {
            invocation.ReturnValue = InterceptAsync(invocation, ProceedAsynchronous<TResult>);
        }

        /// <summary>
        /// Override in derived classes to intercept method invocations.
        /// </summary>
        /// <param name="invocation">The method invocation.</param>
        /// <param name="proceed">The function to proceed the <paramref name="invocation"/>.</param>
        /// <returns>A <see cref="Task" /> object that represents the asynchronous operation.</returns>
        protected abstract Task InterceptAsync(IInvocation invocation, Func<IInvocation, Task> proceed);

        /// <summary>
        /// Override in derived classes to intercept method invocations.
        /// </summary>
        /// <typeparam name="TResult">The type of the <see cref="Task{T}"/> <see cref="Task{T}.Result"/>.</typeparam>
        /// <param name="invocation">The method invocation.</param>
        /// <param name="proceed">The function to proceed the <paramref name="invocation"/>.</param>
        /// <returns>A <see cref="Task" /> object that represents the asynchronous operation.</returns>
        protected abstract Task<TResult> InterceptAsync<TResult>(
            IInvocation invocation,
            Func<IInvocation, Task<TResult>> proceed);

        private static GenericSynchronousHandler CreateHandler(Type returnType)
        {
            MethodInfo method = InterceptSynchronousMethodInfo.MakeGenericMethod(returnType);
            return (GenericSynchronousHandler)method.CreateDelegate(typeof(GenericSynchronousHandler));
        }

        private static void InterceptSynchronousVoid(AsyncInterceptorBase me, IInvocation invocation)
        {
            Task task = me.InterceptAsync(invocation, ProceedSynchronous);

            // If the intercept task has yet to complete, wait for it.
            if (!task.IsCompleted)
            {
                Task.Run(() => task).Wait();
            }

            if (task.IsFaulted)
            {
                if (task.Exception != null)
                {
                    if (task.Exception.InnerException!=null)
                    {
                        throw task.Exception.InnerException;
                    }
                    else
                    {
                        throw task.Exception;
                    }
                }

            }
        }

        private static void InterceptSynchronousResult<TResult>(AsyncInterceptorBase me, IInvocation invocation)
        {
            Task task = me.InterceptAsync(invocation, ProceedSynchronous<TResult>);

            // If the intercept task has yet to complete, wait for it.
            if (!task.IsCompleted)
            {
                Task.Run(() => task).Wait();
            }

            if (task.IsFaulted)
            {
                if (task.Exception != null)
                {
                    if (task.Exception.InnerException!=null)
                    {
                       throw task.Exception.InnerException;
                    }
                    else
                    {
                        throw task.Exception;
                    }
                }

            }
        }

        private static Task ProceedSynchronous(IInvocation invocation)
        {
            try
            {
                invocation.Proceed();
                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }

        private static Task<TResult> ProceedSynchronous<TResult>(IInvocation invocation)
        {
            try
            {
                invocation.Proceed();
                return Task.FromResult((TResult)invocation.ReturnValue);
            }
            catch (Exception e)
            {
                return Task.FromException<TResult>(e);
            }
        }

        private static async Task ProceedAsynchronous(IInvocation invocation)
        {
            invocation.Proceed();

            // Get the task to await.
            var originalReturnValue = (Task)invocation.ReturnValue;

            await originalReturnValue.ConfigureAwait(false);
        }

        private static async Task<TResult> ProceedAsynchronous<TResult>(IInvocation invocation)
        {
            invocation.Proceed();

            // Get the task to await.
            var originalReturnValue = (Task<TResult>)invocation.ReturnValue;

            TResult result = await originalReturnValue.ConfigureAwait(false);
            return result;
        }
    }
}
