using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Persistence
{
    public static class PListLinq
    {
        public static PList<T> ToPList<T>(this IList<T> sequence) where T:DAO
        {
            return new PList<T>(sequence);
        }

    }

    internal interface IPList: IList
    {
        protected void SetKey(string key, object value);
        protected Type GetMemberType();
        protected internal void LoadList(OneToMany oneToMany, DAO obj);
        internal void BuildList(DbDataReader reader);
        public bool Save();
        public bool Delete();
        public bool IsChanged { get; set; }
    }
    
    public sealed class PList<T> : BindingList<T>, IPList where T : DAO
    {
        private OneToMany _oneToMany;
        private DAO _root;
        private string _whereQuery;
        private List<T> _toDelete = new List<T>();
        private Table _table;
        private Type _type;
        public PersistenceContext Context { get; set; }
        internal IStorage _storage => Context?.GetStorage(_table);

        public bool IsChanged { get; set; }

        public PList()
        {
            Initialize();
        }

        public PList(IList<T> collection): base(collection)
        {
            Initialize();
        }

        public PList(string whereQuery)
        {
            Initialize();
            GetWhereQuery(whereQuery);
        }
        
        private void Initialize()
        {
            _type = typeof(T);
            _table = Persistence.Tables[_type.Name];
            ListChanged += OnListChanged;
        }

        private void OnListChanged(object sender, ListChangedEventArgs e)
        {
            IsChanged = true;
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
        }

        int IList.Add(object? item)
        {
            base.Add((T)item!);
            return Count - 1;
        }
        
        // Clears the contents of List.
        public new void Clear()
        {
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


        // Copies this List into array, which must be of a 
        // compatible array type.  
        public new void CopyTo(T[] array)
            => base.CopyTo(array, 0);

        public new int FindLastIndex(Predicate<T> match)
            => FindLastIndex(Count - 1, Count, match);

        public new int FindLastIndex(int startIndex, Predicate<T> match)
            => FindLastIndex(startIndex, startIndex + 1, match);

        public new int FindLastIndex(int startIndex, int count, Predicate<T> match)
        {
            return FindLastIndex(startIndex, count, match);
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

        // Removes the element at the given index. The size of the list is
        // decreased by one.
        public new void RemoveAt(int index)
        {
            base.RemoveAt(index);
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
            _oneToMany = oneToMany;
            _root = obj;
            Context = obj.Context;
            if (oneToMany.Fetch == Fetch.Eager)
            {
                Load(0, oneToMany.ItemsByAccess);
            }
        }

        void IPList.BuildList(DbDataReader reader)
        {
            Clear();
            try
            {
                var runLater = new RunLater();
                while (reader.Read())
                {
                    var instance = (DAO) Activator.CreateInstance<T>();
                    instance.Context = Context;
                    instance.Build(reader, _table, runLater);
                    if (_storage is not null)
                    {
                        if (!_storage.TryAdd(ref instance))
                        {
                            runLater.Clear();
                        }
                    }
                    Add((T) instance);
                }

                reader.Close();
                runLater.Run();
            }
            catch (Exception ex)
            {
                throw new PersistenceException("Error on Build List", ex);
            }

        }

        public bool Save()
        {
            var result = this.All(dao => dao.Save());
            IsChanged = false;
            return result;
        }
        
        public bool Delete(int index)
        {
            if (this[index].Delete()) return false;
            RemoveAt(index);
            return true;
        }

        public void RemoveRange(int first, int last)
        {
            
        }
        
        public bool Delete()
        {
            foreach (var dao in this)
            {
                if (!dao.Delete())
                {
                    RemoveRange(0, IndexOf(dao));
                    return false;
                }
            }
            Clear();
            return true;
        }

        public bool Delete(T value)
        {
            if (!value.Delete()) return false;
            Remove(value);
            return true;
        }

        public bool Load(uint first = 0, uint length = 1 << 31 - 1)
        {
            if (_oneToMany == null || _root == null) return false;
            var whereClause = "";
            foreach (var (key, field) in _oneToMany.Relationship.Links)
            {
                whereClause += $"{key} = {field.Prop.GetValue(_root)}";
            }
            var reader = Persistence.Sql.SelectWhereQuery(_table, whereClause, first, length);
            ((IPList) this).BuildList(reader);
            return true;
        }

        public void RemoveAll(Predicate<T> match)
        {
            foreach (var dao in this.ToImmutableArray().Where(dao => match(dao)))
            {
                Remove(dao);
            }
        }

        public bool DeleteAll()
        {
            RemoveAll(dao => dao.Delete());
            return Count == 0;
        }

        public void GetWhereQuery(string whereQuery, uint offset = 0, uint length = 1 << 31 - 1)
        {
            _whereQuery = whereQuery;
            var reader = Persistence.Sql.SelectWhereQuery(_table, whereQuery, offset, length);
            ((IPList) this).BuildList(reader);
        }
        
        public static PList<T> FindWhereQuery(string whereQuery)
        {
            return new PList<T>(whereQuery);
        }
    }
}
