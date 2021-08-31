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
        private Dictionary<PropColumn, object> LastKeys;
        private Table __table => Persistence.Tables[GetType().Name];
        private Storage _storage => Persistence.Storage[__table];
        private long _Version;
        private long _id;
        private bool _NotLoaded { get; set; }
        private bool Loaded => !_NotLoaded;
        public event PropertyChangedEventHandler PropertyChanged;

        [DefaultPk("Id", SqlDbType.BigInt, AutoIncrement = true, DefaultValue = 0)]
        public long Id
        {
            get => _id;
            set => UpdateId(value);
        }

        public bool Load() => Load(__table);
        public bool Save() => Save(__table);
        public bool Persist() => Persist(__table);
        public bool Delete() => Delete(GetType());

        protected DAO()
        {
            LastKeys = new Dictionary<PropColumn, object>();
            ctor(GetType());
        }

        private void ctor(Type type)
        {
            if (type.BaseType != typeof(DAO))
                ctor(type.BaseType);

            var table = Persistence.Tables[type.Name];
            foreach (var col in table.Columns.OfType<PrimaryKey>())
            {
                LastKeys.Add(col, null);
            }
        }

        private void UpdateId(long value)
        {
            _id = value;
        }

        private bool Delete(Type type)
        {
            var table = Persistence.Tables[type.Name];
            var keys = new Dictionary<string, object>();
            foreach (var column in table.Columns.Where(column => column is PrimaryKey))
            {
                var value = column.Prop.GetValue(this);
                keys.Add(((PrimaryKey)column).SqlName, value);
            }

            return Persistence.Sql.Delete(table, keys);
        }

        public static T Load<T>(long id) where T : DAO
        {
            var obj = Activator.CreateInstance<T>();
            obj.Id = id;
            obj.Load();
            return obj;
        }

        private static object Load(long id, Type t)
        {
            var obj = (DAO)Activator.CreateInstance(t);
            obj?.Load();
            return obj;
        }

        internal void Build(IDataRecord reader, Table table, out RunLater runLater)
        {
            runLater = new RunLater();
            object value;
            if (table.Versioned)
            {
                value = reader.Read("__Version");
                _Version = (long)(value ?? 0);
            }

            if (table.IsSpecialization)
                runLater.Later(() => Load(table.BaseTable));

            foreach (var column in table.Columns)
            {
                switch (column)
                {
                    case Relationship rel:
                        var obj = (DAO)Activator.CreateInstance(rel.Prop.PropertyType);
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
                        else
                            obj._NotLoaded = true;
                        rel.Prop.SetValue(this, obj);
                        break;
                    case OneToMany o2m:
                        runLater.Later(10, () => ((IPList)o2m.Prop.GetValue(this))?.LoadList(o2m));
                        break;
                    case Field field:
                        value = reader.Read(field.SqlName);
                        if (field is PrimaryKey)
                            LastKeys[field] = value;
                        field.Prop.SetValue(this, value);
                        break;
                }
            }

            _NotLoaded = false;
        }

        public void Free()
        {
            _storage.Free(this);
        }

        private bool Persist(Table table)
        {
            return Save(table) && Load(table);
        }

        private bool Load(Table table)
        {
            var keys = table.PrimaryKeys.ToDictionary(pk => pk.SqlName, pk => pk.Prop.GetValue(this));
            try
            {
                var reader = Persistence.Sql.Select(table, keys);
                if (reader.Read())
                {
                    Build(reader, table, out var runLater);
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

            return true;
        }

        private bool Save(Table table)
        {
            if (table.IsSpecialization && !Save(table.BaseTable))
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
                        if (obj == null)
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
                        var dao = (DAO)obj;
                        if (obj == null)
                            if (manyToOne.Nullable == Nullable.NotNull)
                                throw new PersistenceException($"Property value {manyToOne.Prop} cannot be null");
                            else
                                continue;
                        if (manyToOne.Cascade.HasFlag(Cascade.SAVE) && dao.Loaded && !dao.Save())
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
                if (ex is SQLException { ErrorCode: SQLException.ErrorCodeVersion })
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
            if (table.DefaultPk && _id == 0)
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
                }

                output += $"{delim}{propertyInfo.Name}: ";
                if (!propertyInfo.PropertyType.IsSubclassOf(typeof(DAO)))
                {
                    var value = propertyInfo.GetValue(this);
                    output += value == null ? "null" : $"\"{value}\"";
                }
                else
                    output += propertyInfo.PropertyType;

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
            var list = new PList<T>();
            list.BuildList(reader);
            return list;
        }

        public static PList<T> FindWhereQuery<T>(string whereQuery) where T : DAO
        {
            var type = typeof(T);
            var table = Persistence.Tables[type.Name];
            var reader = Persistence.Sql.SelectWhereQuery(table, whereQuery);
            var myList = new PList<T>();
            myList.BuildList(reader);
            return myList;
        }
    }
}