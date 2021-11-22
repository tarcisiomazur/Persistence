using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BidirectionalDict;

namespace Persistence
{
    
    public class Keys
    {
        public readonly List<object>? KeyList;
        public readonly object? KeyObj;

        public Keys(long id)
        {
            KeyObj = id;
        }
        
        public Keys(DAO dao)
        {
            if (dao.Table.SingleKey)
            {
                KeyObj = dao.LastKeys.First().Key.Prop.GetValue(dao);
                return;
            }

            KeyList = new List<object>();
            foreach (var key in dao.LastKeys.Keys.ToList())
            {
                KeyList.Add(key.Prop.GetValue(dao));
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Keys _keys) return false;
            if (KeyObj != null && KeyObj.Equals(_keys.KeyObj))
            {
                return true;
            }
            if (KeyList != null && _keys.KeyList != null && _keys.KeyList.Count == KeyList.Count)
            {
                return KeyList.SequenceEqual(KeyList);
            }

            return false;
        }

        public override int GetHashCode()
        {
            if (KeyObj != null) return KeyObj.GetHashCode();
            if (KeyList == null) return 0;
            long hash = KeyList.Count;
            foreach (var key in KeyList)
            {
                hash = (hash + key?.GetHashCode() ?? 1000) % int.MaxValue;
            }
            return (int) hash;
        }
    }

    public interface IStorage
    {
        internal bool TryAdd(ref DAO dao);
        void AddOrUpdate(DAO dao);
        internal void Free(DAO dao);
        
        public BiDictionary<Keys, DAO> Objects { get; }

    }
    public class Storage<T> : IStorage where T:DAO
    {
        private static Table Table => Persistence.Tables[typeof(T).Name];
        private static Dictionary<string, PrimaryKey> PrimaryKeys => Table.PrimaryKeyStrings;
        public readonly BiDictionary<Keys, DAO> _objects;
        private PersistenceContext Context;

        public BiDictionary<Keys, DAO> Objects => _objects;
        public Storage(PersistenceContext persistenceContext)
        {
            Context = persistenceContext;
            _objects = new BiDictionary<Keys, DAO>();
        }


        bool IStorage.TryAdd(ref DAO dao)
        {
            var keys = new Keys(dao);
            if (_objects.TryGet(keys, out var _dao))
            {
                dao = (T) _dao;
                return false;
            }

            _objects.TryAdd(keys, dao);
            dao.PropertyChanged += DaoChanged;
            return true;
        }
        
        void IStorage.AddOrUpdate(DAO dao)
        {
            var keys = new Keys(dao);
            if (_objects.TryGet(keys, out var _dao))
            {
                if (dao == _dao) return;
                _dao.PropertyChanged -= DaoChanged;
                _objects.TryRemove(keys);
            }

            _objects.AddOrUpdate(keys, dao);
            dao.PropertyChanged += DaoChanged;
        }

        private void DaoChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not null && PrimaryKeys.TryGetValue(e.PropertyName, out var pk))
            {
                var dao = (DAO) sender;
                var keys = new Keys(dao);
                if (_objects.TryGet(keys, out var _dao))
                {
                    if (_dao != dao)
                        _objects.TryRemove(keys);
                    _objects.AddOrUpdate(keys, dao);
                }
            }
        }

        public T Get(long id)
        {
            if (_objects.TryGet(new Keys(id), out var dao))
            {
                return (T) dao;
            }

            dao = Activator.CreateInstance<T>();
            dao.Context = Context;
            dao.Id = id;
            _objects.TryAdd(new Keys(id), dao);
            dao.Load();
            return (T) dao;
        }
        
        public T? TryGet(long id)
        {
            if (_objects.TryGet(new Keys(id), out var dao))
            {
                return (T) dao;
            }

            return null;
        }
        

        public void Clear()
        {
            foreach (var (_, dao) in _objects)
            {
                dao.PropertyChanged -= DaoChanged;
                dao.Context = null;
            }
            _objects.Clear();
        }

        void IStorage.Free(DAO dao)
        {
            dao.PropertyChanged -= DaoChanged;
            _objects.TryRemove(dao);
        }

        public PList<T> GetWhereQuery(string whereQuery)
        {
            var plist = new PList<T>();
            plist.Context = Context;
            plist.GetWhereQuery(whereQuery);
            return plist;
        }
        
    }
/*
    public class DictionaryEqualityComparer<TKey, TValue> : IEqualityComparer<IDictionary<TKey, TValue>>
    {
        readonly IEqualityComparer<TKey> keyComparer;
        readonly IEqualityComparer<TValue> valueComparer;

        public DictionaryEqualityComparer() : this(null, null)
        {
        }

        public DictionaryEqualityComparer(IEqualityComparer<TKey> keyComparer, IEqualityComparer<TValue> valueComparer)
        {
            this.keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            this.valueComparer = valueComparer ?? EqualityComparer<TValue>.Default;
        }

        public bool Equals(IDictionary<TKey, TValue> a, IDictionary<TKey, TValue> b)
        {
            if (a == null || b == null)
                return (a == null && b == null); //if either value is null return false, or true if both are null
            return a.Count == b.Count //unless they have the same number of items, the dictionaries do not match
                   && a.Keys.Intersect(b.Keys, keyComparer).Count() ==
                   a.Count //unless they have the same keys, the dictionaries do not match
                   && a.Keys.Where(key => ValueEquals(a[key], b[key])).Count() ==
                   a.Count; //unless each keys' value is the same in both, the dictionaries do not match
        }

        public int GetHashCode(IDictionary<TKey, TValue> obj)
        {
            //I suspect there's a more efficient formula for even distribution, but this does the job for now
            long hashCode = obj.Count;
            foreach (var key in obj.Keys)
            {
                hashCode += (key?.GetHashCode() ?? 1000) + (obj[key]?.GetHashCode() ?? 0);
                hashCode %= int.MaxValue; //ensure we don't go outside the bounds of MinValue-MaxValue
            }

            return (int) hashCode; //safe conversion thanks to the above %
        }

        private bool ValueEquals(TValue x, TValue y)
        {
            return valueComparer.Equals(x, y);
        }

    }*/
}
