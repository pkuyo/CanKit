using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace EndpointListenerWpf.Models
{
    public class RangeObservableCollection<T> : ObservableCollection<T>
    {

        public bool Suppress { get; set; }

        public void RemoveRange(int length)
        {
            var last = Suppress;
            Suppress = true;
            for (int i = length - 1; i >=0 && Items.Count != 0; i--)
                Items.RemoveAt(0);
            Suppress = last;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void NotifyReset()
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!Suppress) base.OnCollectionChanged(e);
        }
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (!Suppress) base.OnPropertyChanged(e);
        }

        public void AddRange(IEnumerable<T> items)
        {
            var added = new List<T>();
            var last = Suppress;
            Suppress = true;
            foreach (var item in items)
            {
                Items.Add(item);
                added.Add(item);
            }
            Suppress = last;

            if (added.Count == 0) return;
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));

            base.OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
