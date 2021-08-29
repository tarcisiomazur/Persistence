using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using MySql.Data.MySqlClient;
using Type = System.Type;
namespace Persistence
{
    public class DAO
    {
        private long _id { get; set; } = long.MinValue;

        private Dictionary<PropColumn, object> LastKeys;
        
        private long _Version;
        
        public event PropertyChangedEventHandler PropertyChanged;

        [DefaultPk("Id", SqlDbType.BigInt, AutoIncrement = true, DefaultValue = long.MinValue)]
        public long Id
        {
            get => _id;
            set => UpdateId(value);
        }


        private Table __table => Persistence.Tables[GetType().Name];
        private Storage _storage => Persistence.Storage[__table];
        
        protected DAO()
        {
            LastKeys = new Dictionary<PropColumn, object>();
            ctor(GetType());
        }
        
        
        private void ctor(Type type)
        {
            if(type.BaseType != typeof(DAO))
                ctor(type.BaseType);
            
            var table = Persistence.Tables[type.Name];
            foreach (var col in table.Columns)
            {
                switch (col)
                {
                    case PrimaryKey pk:
                        LastKeys.Add(col, null);
                        break;
                    case OneToMany oneToMany:
                    {
                        col.Prop.SetValue(this, Activator.CreateInstance(col.Prop.PropertyType));
                        break;
                    }
                    case Relationship manyToOne:
                    {
                        //TODO Posso deixar != null?
                        //col.Prop.SetValue(this, Activator.CreateInstance(pi.PropertyType));
                        break;
                    }
                }
            }
        }
        
        private void UpdateId(long value)
        {
            _id = value;
        }
        
        public bool Delete()
        {
            var table = Persistence.Tables[GetType().Name];
            var keys = new Dictionary<string, object>();
            foreach (var column in table.Columns.Where(column => column is PrimaryKey))
            {
                var value = column.Prop.GetValue(this);
                keys.Add(((PrimaryKey) column).SqlName, value);
            }
            
            Persistence.Sql.Delete(table, keys);
            return false;
        }
        
        public static T Load<T>(long id) where T:DAO
        {
            var obj = Activator.CreateInstance<T>();
            obj.Id = id;
            obj.Load();
            return obj;
        }

        private static object Load(long id, Type t)
        {
            var obj = (DAO) Activator.CreateInstance(t);
            obj?.Load();
            return obj;
        }

        private void Build(IDataRecord reader, Table table, out RunLater runLater)
        {
            runLater = new RunLater();
            object value;
            if (table.Versioned)
            {
                value = reader.Read("__Version");
                _Version = (long) (value??0);
            }
            
            foreach (var column in table.Columns)
            {
                switch (column)
                {
                    case Relationship rel:
                        var obj = (DAO) Activator.CreateInstance(rel.Prop.PropertyType);
                        var isNull = false;
                        foreach (var (name, fk) in rel.Links)
                        {
                            value = reader.Read(name);
                            if (value == null)
                            {
                                isNull = true;
                                break;
                            }
                            fk.Prop.SetSqlValue(obj, value);
                        }

                        if (isNull) continue;

                        if (rel.Fetch == Fetch.Eager)
                            runLater.Later(() => obj.Load());

                        rel.Prop.SetValue(this, obj);
                        break;
                    case OneToMany o2m:
                        runLater.Later(10, () => ((IMyList) o2m.Prop.GetValue(this))?.LoadList(o2m));
                        break;
                    case Field field:
                        value = reader.Read(field.SqlName);
                        if (field is PrimaryKey)
                            LastKeys[field] = value;
                        field.Prop.SetValue(this, value);
                        break;
                }
            }
        }

        public void Free()
        {
            _storage.Free(this);
        }

        public bool Load()
        {
            return Load(GetType());
        }

        private bool Load(Type type)
        {
            var table = Persistence.Tables[type.Name];
            var keys = table.PrimaryKeys.ToDictionary(pk => pk.SqlName, pk => pk.Prop.GetValue(this));
            try
            {
                var reader = Persistence.Sql.Select(table, keys);
                if (reader.Read())
                {
                    Build(reader,table, out var runLater);
                    _storage.Add(this);
                    reader.Close();
                    runLater.Run();
                }
                else
                {
                    reader.Close();
                    return false;
                }
            }
            catch (Exception ex)
            {
                throw new PersistenceException("Error on Load", ex);
            }

            if (table.IsSpecialization)
                Load(type.BaseType);
            return true;
        }

        public bool Persist()
        {
            return Save() && Load();
        }

        public bool Save()
        {
            return Save(GetType());
        }

        private bool Save(Type type)
        {
            var table = Persistence.Tables[type.Name];
            if (table.IsSpecialization && !Save(type.BaseType))
                return false;
            var fields = new Dictionary<string, object>();
            if (table.Versioned)
                fields.Add("__Version", _Version);
            
            var keyChange = false;
            foreach (var column in table.Columns)
            {
                var pi = column.Prop;
                var obj = pi.GetValue(this);
                switch (column)
                {
                    case PrimaryKey pk:
                        if(obj == null)
                            throw new PersistenceException("The PrimaryKey cannot be null");
                        if (obj.Equals(pk.DefaultValue))
                            if (!pk.AutoIncrement)
                                throw new PersistenceException(
                                    "The PrimaryKey must be different from the default");
                            else
                                continue;

                        var lastKey = LastKeys[column];
                        if (lastKey != null && !lastKey.Equals(obj))
                            keyChange = true;
                        fields.Add(pk.SqlName, obj);
                        break;
                    case Field fa:
                        fields.Add(fa.SqlName, obj);
                        break;
                    case Relationship manyToOne:
                        if (obj == null)
                            if (manyToOne.Nullable == Nullable.NotNull)
                                throw new PersistenceException($"Property value {manyToOne.Prop} cannot be null");
                            else
                                continue;
                        if (manyToOne.Cascade.HasFlag(Cascade.SAVE) && !((DAO) obj).Save())
                            continue;
                        foreach (var (name, fk) in manyToOne.Links)
                            fields.Add(name, fk.Prop.GetValue(obj));

                        break;
                }
            }

            long res;
            try
            {
                if (keyChange)
                    res = Persistence.Sql.Update(table, fields, LastKeys);
                else
                    res = Persistence.Sql.InsertOrUpdate(table, fields);
            }
            catch (Exception ex)
            {
                if (ex is SQLException {ErrorCode: SQLException.ErrorCodeVersion})
                    return false;
                throw new PersistenceException($"Error on save object {ToString()}", ex);
            }

            Console.WriteLine(res);
            if (res == -1) return false;
            
            UpdateIdentifiers(table, res);
            return true;

        }

        private void UpdateIdentifiers(Table table, long res = 0)
        {
            if (table.DefaultPk && _id == long.MinValue)
            {
                _id = res;
            }
            
            foreach (var key in LastKeys.Keys.ToList())
            {
                LastKeys[key] = key.Prop.GetValue(this);
            }
        }

        public override string ToString()
        {
            var type = GetType();
            string output = "[", tail = "", delim = "";
            var props = type.GetProperties();
            foreach (var propertyInfo in props.Reverse())
            {
                if (type != propertyInfo.DeclaringType)
                {
                    if (propertyInfo.DeclaringType != typeof(DAO))
                    {
                        type = propertyInfo.DeclaringType;
                        output += $"{delim}{type.Name}" + "{";
                        tail += "}";
                        delim = "";
                    }
                    else if (_id == long.MinValue)
                    {
                        continue;
                    }
                }
                output += $"{delim}{propertyInfo.Name}: \"" + (propertyInfo.PropertyType.IsSubclassOf(typeof(DAO)) ?
                 propertyInfo.PropertyType : propertyInfo.GetValue(this)??"null") + "\"";
                delim = ", ";

            }

            return output + tail + "]";
        }
        
        private void ApplyInProperty<T>(Action<PropertyInfo, T> action)
        {
            foreach (var pi in GetType().GetProperties())
            {
                switch (pi.GetCustomAttributes(typeof(PersistenceAttribute), true)[0])
                {
                        case T th:
                            action(pi, th);
                            break;
                }
            }
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static IEnumerable<T> Find<T>(T obj, params string[] props) where T : DAO
        {
            var type = typeof(T);
            var table = Persistence.Tables[type.Name];
            var field = new Dictionary<string, object>();

            foreach (var propName in props)
            {
                var col = table.TryGetColumn(propName);
                if (col != null)
                {
                    var value = col.Prop.GetValue(obj);
                    switch (col)
                    {
                        case OneToMany _:
                        case Relationship _ when value == null:
                            continue;
                        case Relationship m2o:
                            foreach (var (name, f) in m2o.Links)
                                field.Add(name, f.Prop.GetValue(value));
                            break;
                        case Field _:
                            field.Add(col.SqlName, value);
                            break;
                    }
                }
            }

            var reader = Persistence.Sql.Select(table, field);

            return BuildList<T>(table, reader);
        }
        
        public static IEnumerable<T> FindWhereQuery<T>(T obj, string whereQuery) where T : DAO
        {
            var type = typeof(T);
            var table = Persistence.Tables[type.Name];
            var reader = Persistence.Sql.SelectWhereQuery(table, whereQuery);
            return BuildList<T>(table, reader);
        }

        private static List<T> BuildList<T>(Table table, DbDataReader reader)
            where T : DAO
        {
            var list = new MyList<T>();
            try
            {
                RunLater runLater = null;
                while (reader.Read())
                {
                    var instance = Activator.CreateInstance<T>();
                    instance.Build(reader, table, out runLater);
                    list.Add(instance);
                }

                reader.Close();
                runLater?.Run();
            }
            catch (Exception ex)
            {
                throw new PersistenceException("Error on Load", ex);
            }

            return list;
        }
    }
}