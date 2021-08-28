using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace Persistence
{
    public static class CollectionExtension
    {
        
        public static object Read(this IDataRecord data, string field)
        {
            try
            {
                var value = data[field];
                return value is DBNull ? null : value;
            }
            catch
            {
                return null;
            }
        }
        public static void SetSqlValue(this PropertyInfo propertyInfo, DAO dao, object obj)
        {
            propertyInfo.SetValue(dao, obj);
        }
        public static void Do<T>(this IEnumerable<T> sequence, Action<T> action)
        {
            if (sequence == null)
                return;
            IEnumerator<T> enumerator = sequence.GetEnumerator();
            while (enumerator.MoveNext())
                action(enumerator.Current);
        }
        public static void AddOrUpdate<T1,T2>(this Dictionary<T1,T2> dict, T1 key, T2 value)
        {
            if (!dict.ContainsKey(key))
            {
                dict.Add(key, value);
            }
            else
            {
                dict[key] = value;
            }
        }
    }
}