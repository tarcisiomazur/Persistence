using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
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
        private long _lastId { get; set; } = long.MinValue;
        
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
            _lastId = _id;
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
            foreach (var pi in GetType().GetProperties())
            {
                switch (pi.GetCustomAttribute<PersistenceAttribute>())
                {
                    case OneToManyAttribute oneToMany:
                    {
                        pi.SetValue(this, Activator.CreateInstance(pi.PropertyType));
                        break;
                    }
                    case ManyToOneAttribute manyToOne:
                    {
                        pi.SetValue(this, Activator.CreateInstance(pi.PropertyType));
                        break;
                    }
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
            foreach (var column in _table.Columns)
            {
                switch (column)
                {
                    case ManyToOne m2o:
                        var obj = (DAO) Activator.CreateInstance(m2o.Prop.PropertyType);
                        foreach (var fk in m2o.Links)
                        {
                            fk.Prop.SetSqlValue(this, reader[fk.SqlName]);
                        }

                        if (m2o.Fetch == Fetch.Lazy)
                        {
                            runLater.Later(() => obj.Load());
                        }

                        m2o.Prop.SetValue(this, obj);
                        break;
                    case OneToMany o2m:
                        runLater.Later(10, () => ((IMyList) o2m.Prop.GetValue(this))?.LoadList(o2m));
                        break;
                    case Field field:
                        field.Prop.SetValue(this, reader[field.SqlName]);
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
            var keys = _table.Columns.Where(column => column is PrimaryKey).Cast<PrimaryKey>()
                .ToDictionary(pk => pk.SqlName, pk => pk.Prop.GetValue(this));

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

            foreach (var column in table.Columns)
            {
                var pi = column.Prop;
                var obj = pi.GetValue(this) ?? Activator.CreateInstance(pi.PropertyType);
                switch (column)
                {
                    case PrimaryKey pk:
                        if (pk.DefaultValue != null && !pk.DefaultValue.Equals(obj) || obj == null)
                        {
                            throw new PersistenceException("The PrimaryKey must be different from the default");
                        }
                        fields.Add(pk.SqlName, obj);
                        break;
                    case Field fa:
                        fields.Add(fa.SqlName, obj);
                        break;
                    case ManyToOne manyToOne:
                        if ((manyToOne.Cascade & Cascade.PERSIST) == Cascade.PERSIST)
                        {
                            var dao = (DAO) obj;
                            if (dao == null || !dao.Save())
                            {
                                continue;
                            }
                            foreach (var fk in manyToOne.Links)
                            {
                                fields.Add(fk.SqlName, fk.Prop.GetValue(dao));
                            }
                        }
                        break;
                }
            }

            var res = Persistence.Sql.InsertOrUpdate(table, fields);
            Console.WriteLine(res);
            if (table.DefaultPk && res != -1)
            {
                Id = res;
                return true;
            }

            return res != -1;
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
    }
}