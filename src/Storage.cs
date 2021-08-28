using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;

namespace Persistence
{

    /*public class Compare : IEqualityComparer<object>
    {
        public bool Equals(object x, object y)
        {
            var lx = (List<StoredObject>) x;
            var ly = (List<StoredObject>) y;
            if (lx==null || ly == null || lx.Count != ly.Count)
                return false;
            for (var i = 0; i < lx.Count; i++)
            {
                if (lx[i] != ly[i]) return false;
            }

            return true;
        }

        public int GetHashCode(object obj)
        {
            return obj.GetHashCode();
        }
    }*/
    public class Storage
    {
        //public static Compare Comparer = new Compare();
        
        private readonly Dictionary<object, DAO> _objects;
        private readonly List<Task<DAO>> _collector;

        public static int Time { get; set; } = 10*1000; 

        public Storage(bool tableDefaultPk)
        {
            _objects = new Dictionary<object, DAO>();
        }


        public void Add(DAO dao)
        {
            
            //_objects.AddOrUpdate(dao.Key, dao);
        }

        public DAO Get(long id)
        {
            return _objects[id];
        }

        public void Clear()
        {
            _objects.Clear();
        }

        public void Free(DAO dao)
        {
            _objects.Remove(dao.Id);
        }
    }
}