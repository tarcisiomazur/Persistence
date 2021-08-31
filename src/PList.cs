using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Persistence
{
    public static class PListLinq
    {
        public static PList<T> ToPList<T>(this IEnumerable<T> sequence) where T:DAO
        {
            return new PList<T>(sequence);
        }

    }

    internal interface IPList: IList
    {
        protected void SetKey(string key, object value);
        protected Type GetMemberType();
        protected internal void LoadList(OneToMany oneToMany, DAO obj);
        public bool Save();
    }
    
    public sealed class PList<T> : List<T>, IPList, INotifyCollectionChanged where T : DAO
    {
        private Relationship _relationship;
        private DAO _root;
        private List<T> _toDelete = new List<T>();
        private long _length;
        private long _first;
        private long _last;
        private readonly Table _table;
        private readonly Type _type;
        public long Length => _length;

        public PList()
        {
            _type = typeof(T);
            _table = Persistence.Tables[_type.Name];
        }

        // Constructs a List with a given initial capacity. The list is
        // initially empty, but will have room for the given number of elements
        // before any reallocations are required.
        // 
        public PList(int capacity): base(capacity)
        {
        }

        // Constructs a List, copying the contents of the given collection. The
        // size and capacity of the new list will both be equal to the size of the
        // given collection.
        // 
        public PList(IEnumerable<T> collection): base(collection)
        {
            
        } 
        // Sets or Gets the element at the given index.
        public new T this[int index]
        {
            get => base[index];
            set => base[index] = value;
        }

        object? IList.this[int index]
        {
            get => base[index];
            set => base[index] = (T)value!;
        }

        // Adds the given object to the end of this list. The size of the list is
        // increased by one. If required, the capacity of the list is doubled
        // before adding the new element.
        //
        public new void Add(T item)
        {
            base.Add(item);
            _length ++;
        }

        int IList.Add(object? item)
        {
            base.Add((T)item!);
            return Count - 1;
        }

        // Adds the elements of the given collection to the end of this list. If
        // required, the capacity of the list is increased to twice the previous
        // capacity or the new size, whichever is larger.
        //
        public new void AddRange(IEnumerable<T> collection)
            => base.AddRange(collection);

        public new ReadOnlyCollection<T> AsReadOnly()
            => new ReadOnlyCollection<T>(this);

        // Searches a section of the list for a given element using a binary search
        // algorithm. Elements of the list are compared to the search value using
        // the given IComparer interface. If comparer is null, elements of
        // the list are compared to the search value using the IComparable
        // interface, which in that case must be implemented by all elements of the
        // list and the given search value. This method assumes that the given
        // section of the list is already sorted; if this is not the case, the
        // result will be incorrect.
        //
        // The method returns the index of the given value in the list. If the
        // list does not contain the given value, the method returns a negative
        // integer. The bitwise complement operator (~) can be applied to a
        // negative result to produce the index of the first element (if any) that
        // is larger than the given search value. This is also the index at which
        // the search value should be inserted into the list in order for the list
        // to remain sorted.
        // 
        // The method uses the Array.BinarySearch method to perform the
        // search.
        // 
        public new int BinarySearch(int index, int count, T item, IComparer<T>? comparer)
        {
            return base.BinarySearch(index, count, item, comparer);
        }

        public new int BinarySearch(T item)
            => base.BinarySearch(0, Count, item, null);

        public new int BinarySearch(T item, IComparer<T>? comparer)
            => base.BinarySearch(0, Count, item, comparer);

        // Clears the contents of List.
        public new void Clear()
        {
            _first = _last = 0;
            base.Clear();
        }

        // Contains returns true if the specified element is in the List.
        // It does a linear, O(n) search.  Equality is determined by calling
        // EqualityComparer<T>.Default.Equals().
        //
        public new bool Contains(T item)
        {
            // PERF: IndexOf calls Array.IndexOf, which internally
            // calls EqualityComparer<T>.Default.IndexOf, which
            // is specialized for different types. This
            // boosts performance since instead of making a
            // virtual method call each iteration of the loop,
            // via EqualityComparer<T>.Default.Equals, we
            // only make one virtual call to EqualityComparer.IndexOf.

            return base.Contains(item);
        }

        bool IList.Contains(object? item)
        {
            return base.Contains((T)item!);
        }

        public new List<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
        {
            return base.ConvertAll(converter);
        }

        // Copies this List into array, which must be of a 
        // compatible array type.  
        public new void CopyTo(T[] array)
            => base.CopyTo(array, 0);

        // Copies this List into array, which must be of a 
        // compatible array type.  
        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            base.CopyTo((T[]) array,arrayIndex);
        }

        // Copies a section of this list to the given array at the given index.
        // 
        // The method uses the Array.Copy method to copy the elements.
        // 
        public new void CopyTo(int index, T[] array, int arrayIndex, int count)
        {
            base.CopyTo(index, array, arrayIndex, count);
        }

        public new void CopyTo(T[] array, int arrayIndex)
        {
            base.CopyTo(array, arrayIndex);
        }

        public new bool Exists(Predicate<T> match)
        {
            return base.Exists(match);
        }

        public new T Find(Predicate<T> match)
        {
            return base.Find(match);
        }

        public new List<T> FindAll(Predicate<T> match)
        {
            return base.FindAll(match);
        }

        public new int FindIndex(Predicate<T> match)
            => FindIndex(0, Count, match);

        public new int FindIndex(int startIndex, Predicate<T> match)
            => FindIndex(startIndex, Count - startIndex, match);

        public new int FindIndex(int startIndex, int count, Predicate<T> match)
        {
            return base.FindIndex(startIndex, count, match);
        }

        [return: MaybeNull]
        public new T FindLast(Predicate<T> match)
        {
            return base.FindLast(match);
        }

        public new int FindLastIndex(Predicate<T> match)
            => FindLastIndex(Count - 1, Count, match);

        public new int FindLastIndex(int startIndex, Predicate<T> match)
            => FindLastIndex(startIndex, startIndex + 1, match);

        public new int FindLastIndex(int startIndex, int count, Predicate<T> match)
        {
            return FindLastIndex(startIndex, count, match);
        }

        public new void ForEach(Action<T> action)
        {
            base.ForEach(action);
        }

        // Returns an enumerator for this list with the given
        // permission for removal of elements. If modifications made to the list 
        // while an enumeration is in progress, the MoveNext and 
        // GetObject methods of the enumerator will throw an exception.
        //
        
        public new List<T> GetRange(int index, int count)
        {
            return base.GetRange(index, count);
        }


        // Returns the index of the first occurrence of a given value in a range of
        // this list. The list is searched forwards from beginning to end.
        // The elements of the list are compared to the given value using the
        // Object.Equals method.
        // 
        // This method uses the Array.IndexOf method to perform the
        // search.
        // 
        public new int IndexOf(T item)
        {
            return base.IndexOf(item);
        }

        int IList.IndexOf(object? item)
        {
            return IndexOf((T)item!);
        }

        // Returns the index of the first occurrence of a given value in a range of
        // this list. The list is searched forwards, starting at index
        // index and ending at count number of elements. The
        // elements of the list are compared to the given value using the
        // Object.Equals method.
        // 
        // This method uses the Array.IndexOf method to perform the
        // search.
        // 
        public new int IndexOf(T item, int index)
        {
            return base.IndexOf(item, index);
        }

        // Returns the index of the first occurrence of a given value in a range of
        // this list. The list is searched forwards, starting at index
        // index and upto count number of elements. The
        // elements of the list are compared to the given value using the
        // Object.Equals method.
        // 
        // This method uses the Array.IndexOf method to perform the
        // search.
        // 
        public new int IndexOf(T item, int index, int count)
        {
            return base.IndexOf(item, index, count);
        }

        // Inserts an element into this list at a given index. The size of the list
        // is increased by one. If required, the capacity of the list is doubled
        // before inserting the new element.
        // 
        public new void Insert(int index, T item)
        {
            base.Insert(index, item);
        }

        void IList.Insert(int index, object? item)
        {
            base.Insert(index, (T)item!);
        }

        // Inserts the elements of the given collection at a given index. If
        // required, the capacity of the list is increased to twice the previous
        // capacity or the new size, whichever is larger.  Ranges may be added
        // to the end of the list by setting index to the List's size.
        //
        public new void InsertRange(int index, IEnumerable<T> collection)
        {
            base.InsertRange(index, collection);
        }

        // Returns the index of the last occurrence of a given value in a range of
        // this list. The list is searched backwards, starting at the end 
        // and ending at the first element in the list. The elements of the list 
        // are compared to the given value using the Object.Equals method.
        // 
        // This method uses the Array.LastIndexOf method to perform the
        // search.
        // 
        public new int LastIndexOf(T item)
        {
            return LastIndexOf(item);
        }

        // Returns the index of the last occurrence of a given value in a range of
        // this list. The list is searched backwards, starting at index
        // index and ending at the first element in the list. The 
        // elements of the list are compared to the given value using the 
        // Object.Equals method.
        // 
        // This method uses the Array.LastIndexOf method to perform the
        // search.
        // 
        public new int LastIndexOf(T item, int index)
        {
            return base.LastIndexOf(item, index);
        }

        // Returns the index of the last occurrence of a given value in a range of
        // this list. The list is searched backwards, starting at index
        // index and upto count elements. The elements of
        // the list are compared to the given value using the Object.Equals
        // method.
        // 
        // This method uses the Array.LastIndexOf method to perform the
        // search.
        // 
        public new int LastIndexOf(T item, int index, int count)
        {
            return LastIndexOf(item, index, count);
        }

        // Removes the element at the given index. The size of the list is
        // decreased by one.
        public new bool Remove(T item)
        {
            return base.Remove(item);
        }

        void IList.Remove(object? item)
        {
            Remove((T)item!);
        }

        // This method removes all items which matches the predicate.
        // The complexity is O(n).
        public new int RemoveAll(Predicate<T> match)
        {
            return base.RemoveAll(match);
        }

        // Removes the element at the given index. The size of the list is
        // decreased by one.
        public new void RemoveAt(int index)
        {
            base.RemoveAt(index);
        }

        // Removes a range of elements from this list.
        public new void RemoveRange(int index, int count)
        {
            base.RemoveRange(index, count);
        }

        // Reverses the elements in this list.
        public new void Reverse()
            => Reverse(0, Count);

        // Reverses the elements in a range of this list. Following a call to this
        // method, an element in the range given by index and count
        // which was previously located at index i will now be located at
        // index index + (index + count - i - 1).
        //
        public new void Reverse(int index, int count)
        {
            base.Reverse(index, count);
        }

        // Sorts the elements in this list.  Uses the default comparer and 
        // Array.Sort.
        public new void Sort()
            => Sort(0, Count, null);

        // Sorts the elements in this list.  Uses Array.Sort with the
        // provided comparer.
        public new void Sort(IComparer<T>? comparer)
            => Sort(0, Count, comparer);

        // Sorts the elements in a section of this list. The sort compares the
        // elements to each other using the given IComparer interface. If
        // comparer is null, the elements are compared to each other using
        // the IComparable interface, which in that case must be implemented by all
        // elements of the list.
        // 
        // This method uses the Array.Sort method to sort the elements.
        // 
        public new void Sort(int index, int count, IComparer<T>? comparer)
        {
            base.Sort(index, count, comparer);
        }

        public new void Sort(Comparison<T> comparison)
        {
            base.Sort(comparison);
        }

        // ToArray returns an array containing the contents of the List.
        // This requires copying the List, which is an O(n) operation.
        public new T[] ToArray()
        {
            return base.ToArray();
        }

        
        void IPList.SetKey(string key, object value)
        {
            
        }

        Type IPList.GetMemberType()
        {
            return typeof(T);
        }


        void IPList.LoadList(OneToMany oneToMany, DAO obj)
        {
            Clear();
            _relationship = oneToMany.Relationship;
            _root = obj;
            if (oneToMany.Fetch == Fetch.Eager)
            {
                Load();
            }
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public void BuildList(DbDataReader reader)
        {
            Clear();
            try
            {
                RunLater runLater = null;
                while (reader.Read())
                {
                    var instance = Activator.CreateInstance<T>();
                    instance.Build(reader, _table, out runLater);
                    Add(instance);
                }

                reader.Close();
                runLater?.Run();
            }
            catch (Exception ex)
            {
                throw new PersistenceException("Error on Build List", ex);
            }

        }

        public bool Save()
        {
            return this.All(dao => dao.Save());
        }
        
        public bool Delete(int index)
        {
            if (this[index].Delete()) return false;
            RemoveAt(index);
            return true;
        }
        public bool Delete(T value)
        {
            if (!value.Delete()) return false;
            Remove(value);
            return true;
        }

        public bool Load()
        {
            var whereClause = "";
            foreach (var (key, field) in _relationship.Links)
            {
                whereClause += $"{key} = {field.Prop.GetValue(_root)}";
            }
            var reader = Persistence.Sql.SelectWhereQuery(_table, whereClause);
            BuildList(reader);
            return true;
        }

        public bool DeleteAll()
        {
            RemoveAll(dao => dao.Delete());
            return _length == 0;
        }
    }
}
