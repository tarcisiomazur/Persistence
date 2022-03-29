using System;
using System.Collections.Generic;
using System.Reflection;

namespace Persistence
{
    public static class View
    {
        public static List<T> Execute<T>()
        {
            var list = new List<T>();
            var type = typeof(T);
            var attribute = type.GetCustomAttribute<ViewAttribute>();
            var reader = Persistence.Sql.SelectView(attribute.ViewName, attribute.Schema);
            try
            {
                while (reader.DataReader.Read())
                {
                    var obj = Activator.CreateInstance<T>();
                    foreach (var propertyInfo in type.GetProperties())
                    {
                        var field = propertyInfo.GetCustomAttribute<FieldAttribute>();
                        var value = reader.DataReader.Read(field is not null ? field.FieldName : propertyInfo.Name);

                        if (propertyInfo.PropertyType.IsEnum)
                        {
                            value = Convert.ToInt32(value);
                        }
                        else if (propertyInfo.PropertyType == typeof(bool))
                        {
                            value = value.Equals(1UL);
                        }

                        propertyInfo.SetValue(obj, value);
                    }

                    list.Add(obj);
                }
            }
            finally
            {
                reader.Close();
            }

            return list;
        }
    }
}