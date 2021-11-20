using System;
using System.Collections.Generic;
using System.Reflection;

namespace Persistence
{
    public static class View
    {
        public static List<T> Execute<T> ()
        {
            var list = new List<T>();
            var type = typeof(T);
            var attribute = type.GetCustomAttribute<ViewAttribute>();
            var reader = Persistence.Sql.SelectView(attribute.ViewName, attribute.Schema);

            while (reader.Read())
            {
                var obj = Activator.CreateInstance<T>();
                foreach (var propertyInfo in type.GetProperties())
                {
                    var field = propertyInfo.GetCustomAttribute<FieldAttribute>();
                    var value = reader.Read(field is not null? field.FieldName : propertyInfo.Name);
                    if (propertyInfo.PropertyType.IsEnum)
                    {
                        value = Convert.ToInt32(value);
                    }
                    propertyInfo.SetValue(obj, value);
                }
                list.Add(obj);
            }
            reader.Close();
            return list;
        }
    }
}