using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace WarmAsBefore.Models;

/// <summary>
/// 支持批量替换/追加的 ObservableCollection：ReplaceRange 只触发一次 Reset 事件，
/// 避免 CollectionView 在 Reset 处理中收到逐条 Add 而抛
/// "Cannot change ObservableCollection during a CollectionChanged event"。
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>整体替换内容，只发一次 Reset 通知。</summary>
    public void ReplaceRange(IEnumerable<T> items)
    {
        var list = items as IList<T> ?? items.ToList();
        Items.Clear();
        foreach (var item in list) Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>批量追加，只发一次 Add 通知。</summary>
    public void AddRange(IEnumerable<T> items)
    {
        var list = items as IList<T> ?? items.ToList();
        if (list.Count == 0) return;
        var start = Count;
        foreach (var item in list) Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, list, start));
    }
}
