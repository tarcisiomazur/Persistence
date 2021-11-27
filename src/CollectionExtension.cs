using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;

namespace Persistence
{
    internal static class InternalCollectionExtension
    {
        public static List<string> GetFields(this IDataRecord data)
        {
            var l = new List<string>();
            for (var i = 0; i < data.FieldCount; i++)
            {
                l.Add(data.GetName(i));
            }

            return l;
        }

        public static void Convert(this Field field, ref object value)
        {
            value = field.IsEnum ? System.Convert.ToInt32(value) : Type.GetTypeCode(field.Prop.PropertyType) switch
            {
                TypeCode.Boolean => value.Equals(1ul),
                _ => value
            };
        }

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
            if (obj is not bool && propertyInfo.PropertyType == typeof(bool))
            {
                obj = obj == null || obj.Equals(0);
            }
            else if(obj is string {Length: > 0} str && propertyInfo.PropertyType == typeof(char))
            {
                obj = str[0];
            }

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

        public static void AddOrUpdate<T1, T2>(this Dictionary<T1, T2> dict, T1 key, T2 value)
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

    public static class CollectionExtension
    {
        public static bool Like(this string str, string pattern,
            StringComparison cmp = StringComparison.CurrentCultureIgnoreCase)
        {
            var split = (IEnumerator<string>)pattern.Split("%").ToList().GetEnumerator();
            if (!split.MoveNext()) return false;
            var current = split.Current;
            var offset = str.IndexOf(current ?? "", cmp);
            if (offset != 0) return false;

            while (split.MoveNext())
            {
                current = split.Current;
                offset = str.IndexOf(current ?? "", offset, cmp);
                if (offset == -1) return false;
            }

            return current == "" || offset + current?.Length == str.Length;
        }
    }

}