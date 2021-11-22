using System;
using System.Collections.Generic;

namespace Persistence
{
    public class PersistenceContext
    {
        public readonly Dictionary<Table, IStorage> Storages = new ();

        public PersistenceContext()
        {
            
        }

        public void Persist<T>(ref T dao) where T:DAO
        {
            var storage = GetStorage<T>();
            if (dao.Context != this)
            {
                var _dao = (DAO) dao;//.Copy();
                ((IStorage) storage).TryAdd(ref _dao);
                dao = (T) _dao;
            }
        }


        public T Get<T> (long? id) where T: DAO
        {
            if (id.HasValue)
            {
                return GetStorage<T>().Get(id.Value);
            }
            var dao = Activator.CreateInstance<T>();
            dao.Context = this;
            return dao;
        }
        
        public PList<T> Get<T> (string whereQuery) where T: DAO
        {
            return GetStorage<T>().GetWhereQuery(whereQuery);
        }

        public void Free<T>(T dao) where T : DAO
        {
            if (dao._storage == GetStorage<T>())
            {
                dao.Free();
            }
        }
        
        internal Storage<T> GetStorage<T>() where T : DAO
        {
            var table = Persistence.Tables[typeof(T).Name];
            if (Storages.TryGetValue(table, out var value))
            {
                return value as Storage<T>;
            }
            var storage = new Storage<T>(this);
            Storages.Add(table, storage);

            return storage;
        }


        public IStorage GetStorage(Table table)
        {
            if (Storages.TryGetValue(table, out var value))
            {
                return value;
            }
            var generic = typeof(Storage<>).MakeGenericType(table.Type);
            var storage = (IStorage) Activator.CreateInstance(generic, this);
            Storages.Add(table, storage);
            return storage;
        }

        public T GetOrRefresh<T>(long id) where T : DAO
        {
            var dao = Get<T>(id);
            dao.Refresh();
            return dao;
        }
    }
}