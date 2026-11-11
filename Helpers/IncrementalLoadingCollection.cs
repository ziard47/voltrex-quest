using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace voltrex_quest.Helpers;

/// <summary>
/// Incremental loading collection for smooth GridView virtualization over large datasets.
/// </summary>
public sealed class IncrementalLoadingCollection<T> : ObservableCollection<T>, ISupportIncrementalLoading
{
    private readonly List<T> _source = [];
    private readonly int _batchSize;
    private int _cursor;

    public IncrementalLoadingCollection(int batchSize = 60)
    {
        _batchSize = Math.Max(10, batchSize);
    }

    public int TotalSourceCount => _source.Count;

    public bool HasMoreItems => _cursor < _source.Count;

    public void Reset(IEnumerable<T> items)
    {
        _source.Clear();
        _source.AddRange(items);
        _cursor = 0;
        Clear();

        LoadBatch(_batchSize);
    }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        return AsyncInfo.Run(cancellationToken => Task.FromResult(LoadMoreInternal((int)count, cancellationToken)));
    }

    private LoadMoreItemsResult LoadMoreInternal(int requestedCount, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !HasMoreItems)
        {
            return new LoadMoreItemsResult { Count = 0 };
        }

        var take = Math.Max(_batchSize, requestedCount);
        var loaded = LoadBatch(take);
        return new LoadMoreItemsResult { Count = (uint)loaded };
    }

    private int LoadBatch(int count)
    {
        var remaining = _source.Count - _cursor;
        var toAdd = Math.Min(count, remaining);
        for (var i = 0; i < toAdd; i++)
        {
            Add(_source[_cursor++]);
        }

        return toAdd;
    }
}

