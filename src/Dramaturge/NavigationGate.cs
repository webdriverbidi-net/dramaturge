// <copyright file="NavigationGate.cs" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Dramaturge;

/// <summary>
/// Keeps a page's element lookups and the navigations the library starts on it from overlapping. Chrome can stop
/// answering a page whose element lookup is still pending when a navigation starts, so a navigation waits for the
/// lookups in flight, and a lookup waits for a navigation in progress.
/// </summary>
internal sealed class NavigationGate
{
    private readonly object lockObject = new();
    private LookupSet lookups = new();
    private TaskCompletionSource<bool>? navigation;

    /// <summary>
    /// Runs a lookup once no navigation the library started is in progress.
    /// </summary>
    /// <typeparam name="T">The lookup's result type.</typeparam>
    /// <param name="budget">The time the lookup may take, the wait included.</param>
    /// <param name="lookup">The lookup.</param>
    /// <returns>The lookup's result.</returns>
    public async Task<T> RunLookupAsync<T>(TimeBudget budget, Func<Task<T>> lookup)
    {
        LookupSet set;
        while (true)
        {
            Task<bool> navigating;
            lock (this.lockObject)
            {
                if (this.navigation is null)
                {
                    set = this.lookups;
                    set.Count++;
                    break;
                }

                navigating = this.navigation.Task;
            }

            await budget.WaitAsync(navigating, "a navigation of the page to finish").ConfigureAwait(false);
        }

        try
        {
            return await lookup().ConfigureAwait(false);
        }
        finally
        {
            lock (this.lockObject)
            {
                // A set whose lookups have all finished is done with; later lookups start another.
                if (--set.Count == 0)
                {
                    set.Idle.TrySetResult(true);
                    if (this.lookups == set)
                    {
                        this.lookups = new LookupSet();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Runs a navigation once no other navigation the library started is in progress and the lookups in flight
    /// have finished, or the page has committed a new document.
    /// </summary>
    /// <typeparam name="T">The navigation's result type.</typeparam>
    /// <param name="budget">The time the navigation may take, the wait included.</param>
    /// <param name="navigate">The navigation.</param>
    /// <returns>The navigation's result.</returns>
    public async Task<T> RunNavigationAsync<T>(TimeBudget budget, Func<Task<T>> navigate)
    {
        TaskCompletionSource<bool> ours = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> idle;
        while (true)
        {
            Task<bool> other;
            lock (this.lockObject)
            {
                if (this.navigation is null)
                {
                    this.navigation = ours;
                    idle = this.lookups.Count == 0 ? Task.FromResult(true) : this.lookups.Idle.Task;
                    break;
                }

                other = this.navigation.Task;
            }

            await budget.WaitAsync(other, "another navigation of the page to finish").ConfigureAwait(false);
        }

        try
        {
            await budget.WaitAsync(idle, "the page's element lookups to finish").ConfigureAwait(false);
            return await navigate().ConfigureAwait(false);
        }
        finally
        {
            lock (this.lockObject)
            {
                this.navigation = null;
            }

            ours.TrySetResult(true);
        }
    }

    /// <summary>
    /// Records that the page committed a new document: the lookups in the one it replaced no longer hold a
    /// navigation back.
    /// </summary>
    public void OnDocumentCommitted()
    {
        lock (this.lockObject)
        {
            this.lookups.Idle.TrySetResult(true);
            this.lookups = new LookupSet();
        }
    }

    // The lookups in flight in one document, and when they have all finished.
    private sealed class LookupSet
    {
        public int Count { get; set; }

        public TaskCompletionSource<bool> Idle { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
