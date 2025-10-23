using System;
using System.Collections.ObjectModel;

namespace CanKit.Sample.AvaloniaListener.Models
{
    public class FixedSizeObservableCollection<T> : ObservableCollection<T>
    {
        public int Capacity { get; }

        public FixedSizeObservableCollection(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        protected override void InsertItem(int index, T item)
        {
            if (Count >= Capacity)
            {
                RemoveAt(0);
            }
            base.InsertItem(index, item);
        }
    }
}

