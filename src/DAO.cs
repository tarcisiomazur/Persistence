using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Type = System.Type;
namespace Persistence
{
    public class DAO
    {
        private long _id { get; set; } = long.MinValue;

        private Dictionary<PropColumn, object> LastKeys;
        
        private long _Version;
        
        public event PropertyChangedEventHandler PropertyChanged;

        [DefaultPk("Id", SqlDbType.BigInt, AutoIncrement = true)]
        public long Id
        {
            get => _id;
            set => UpdateId(value);
        }

        private void UpdateId(long value)
        {
            _id = value;
        }

        private Table _table => Persistence.Tables[GetType().Name];
        private Storage _storage => Persistence.Storage[_table];

        private object GetKey()
        {
            if (_table.DefaultPk)
                return _id;
            return _table.PrimaryKeys.Select(key => key.Prop.GetValue(this)).ToList();
        }

        protected DAO()
        {
            LastKeys = new Dictionary<PropColumn, object>();
            var type = GetType();
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
                    case ManyToOne manyToOne:
                    {
                        //TODO Posso deixar != null?
                        //col.Prop.SetValue(this, Activator.CreateInstance(pi.PropertyType));
                        break;
                    }
                }
            }
            foreach (var pi in GetType().GetProperties())
            {
                switch (pi.GetCustomAttribute<PersistenceAttribute>())
                {
                    
                }
            }
        }

        public bool Delete()
        {
            var keys = new Dictionary<string, object>();
            foreach (var column in _table.Columns.Where(column => column is PrimaryKey))
            {
                var value = column.Prop.GetValue(this);
                keys.Add(((PrimaryKey) column).SqlName, value);
            }
            
            Persistence.Sql.Delete(_table, keys);
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

        private void Build(IDataRecord reader, out RunLater runLater)
        {
            runLater = new RunLater();
            object value;
            if (_table.Versioned)
            {
                value = reader.Read("__Version");
                _Version = (long) (value??0);
            }
            
            foreach (var column in _table.Columns)
            {
                switch (column)
                {
                    case ManyToOne m2o:
                        var obj = (DAO) Activator.CreateInstance(m2o.Prop.PropertyType);
                        var isNull = false;
                        foreach (var (name, fk) in m2o.Links)
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

                        if (m2o.Fetch == Fetch.Eager)
                            runLater.Later(() => obj.Load());

                        m2o.Prop.SetValue(this, obj);
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
            var keys = _table.PrimaryKeys.ToDictionary(pk => pk.SqlName, pk => pk.Prop.GetValue(this));
            try
            {
                var reader = Persistence.Sql.Select(_table, keys);
                if (reader.Read())
                {
                    Build(reader, out var runLater);
                    _storage.Add(this);
                    reader.Close();
                    runLater.Run();
                }
            }
            catch (Exception ex)
            {
                throw new PersistenceException("Error on Load", ex);
            }
            
            return true;
        }

        public bool Persist()
        {
            return Save() && Load();
        }
        
        public bool Save()
        {
            var type = GetType();
            var table = Persistence.Tables[type.Name];
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
                        var lastKey = LastKeys[column];
                        if (lastKey != obj)
                        {
                            keyChange = true;
                            fields.Add(pk.SqlName, obj);
                        }
                        if (pk.AutoIncrement) continue;
                        if (pk.DefaultValue != null && !pk.DefaultValue.Equals(obj) || obj == null)
                        {
                            throw new PersistenceException("The PrimaryKey must be different from the default");
                        }
                        break;
                    case Field fa:
                        fields.Add(fa.SqlName, obj);
                        break;
                    case ManyToOne manyToOne:
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
            if (keyChange)
                res = Persistence.Sql.Update(table, fields, LastKeys);
            else
                res = Persistence.Sql.InsertOrUpdate(table, fields);
            Console.WriteLine(res);
            if (res == -1) return false;
            
            UpdateIdentifyers(res);
            return true;

        }

        private void UpdateIdentifyers(long res = 0)
        {
            var type = GetType();
            var table = Persistence.Tables[type.Name];
            if (table.DefaultPk && _id != long.MinValue)
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
            string output = $"{type.Name} [", tail = "]";
            var props = type.GetProperties();
            foreach (var propertyInfo in props.Reverse())
            {
                if (type != propertyInfo.DeclaringType)
                {
                    if (propertyInfo.DeclaringType != typeof(DAO))
                    {
                        type = propertyInfo.DeclaringType;
                        output += $"{type.Name}" + "{";
                        tail += "}";
                    }
                }
                output += $"{propertyInfo.Name}: " + (propertyInfo.PropertyType.IsSubclassOf(typeof(DAO)) ?
                 propertyInfo.PropertyType : propertyInfo.GetValue(this)??"null") + " ";
                
            }

            return output + tail;
            /*
            return
                $"{GetType().Name}{{{string.Join(", ", GetType().GetProperties().Select(info => $"{info.Name}: {(info.PropertyType.IsSubclassOf(typeof(DAO)) ? info.GetValue(this) == null ? null : info.PropertyType : info.GetValue(this))}").ToArray())}}}";
            
        */
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
                        case ManyToOne _ when value == null:
                            continue;
                        case ManyToOne m2o:
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
                    instance.Build(reader, out runLater);
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