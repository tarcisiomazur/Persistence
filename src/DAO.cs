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
        internal static bool Init() => true;
        private Dictionary<PropColumn, object> LastKeys;
        private Table __table => Persistence.Tables[GetType().Name];
        private Storage _storage => Persistence.Storage[__table];
        private long _Version;
        private long _id;
        private bool _NotLoaded { get; set; }
        private bool Loaded => !_NotLoaded;
        public event PropertyChangedEventHandler PropertyChanged;

        [DefaultPk(FieldName = "Id",FieldType = SqlDbType.BigInt, AutoIncrement = true, DefaultValue = 0)]
        public long Id
        {
            get => _id;
            set => UpdateId(value);
        }

        public bool Load() => Load(__table);
        public bool Save() => Save(__table);
        public bool Persist() => Persist(__table);
        public bool Delete() => Delete(__table);

        static DAO()
        {
            Persistence.BuildTables();
        }

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

        private bool Delete(Table table)
        {
            var keys = new Dictionary<string, object>();

            foreach (var column in table.Columns)
            {
                switch (column)
                {
                    case OneToMany oneToMany:
                        if (!oneToMany.Cascade.HasFlag(Cascade.DELETE)) continue;
                        var list = (IPList) oneToMany.Prop.GetValue(this);
                        if (list != null && !list.Delete())
                            return false;
                        break;
                    case PrimaryKey _:
                        var value = column.Prop.GetValue(this);
                        keys.Add(column.SqlName, value);
                        break;
                    case Relationship relationship:
                        if (!relationship.Cascade.HasFlag(Cascade.DELETE)) continue;
                        var dao = (DAO) column.Prop.GetValue(this);
                        if (dao != null && !dao.Delete())
                            return false;
                        break;
                }
            }

            return Persistence.Sql.Delete(table, keys) && (!table.IsSpecialization || Delete(table.BaseTable));
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

        internal void Build(IDataRecord reader, Table table, RunLater runLater)
        {
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
                        var dao = (DAO)Activator.CreateInstance(rel.Prop.PropertyType);
                        var isNull = false;
                        foreach (var (name, fk) in rel.Links)
                        {
                            value = reader.Read(name);
                            if (value == null)
                            {
                                isNull = true;
                                break;
                            }

                            fk.Prop.SetSqlValue(dao, value);
                        }

                        if (isNull) continue;

                        if (rel.Fetch == Fetch.Eager)
                            runLater.Later(() => dao.Load());
                        else
                            dao._NotLoaded = true;
                        rel.Prop.SetValue(this, dao);
                        break;
                    case OneToMany o2m:
                        var obj = (IPList) Activator.CreateInstance(o2m.Prop.PropertyType);
                        o2m.Prop.SetValue(this,obj);
                        runLater.Later(10, () => obj.LoadList(o2m, this));
                        break;
                    case Field field:
                        value = reader.Read(field.SqlName);
                        if (field.IsFlag)
                            value = Convert.ToInt32(value);
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
                var reader = Persistence.Sql.Select(table, keys,0,1);
                var runLater = new RunLater();
                if (reader.Read())
                {
                    Build(reader, table, runLater);
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
            var later = new RunLater();
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
                        if (fa.IsFlag)
                            obj = (int) obj;
                        fields.Add(fa.SqlName, obj);
                        break;
                    case Relationship relationship:
                        var dao = (DAO)obj;
                        if (obj == null)
                            if (relationship.Nullable == Nullable.NotNull)
                                throw new PersistenceException($"Property value {relationship.Prop} cannot be null");
                            else
                                continue;
                        if (relationship.Cascade.HasFlag(Cascade.SAVE) && dao.Loaded && !dao.Save())
                            continue;
                        foreach (var (name, fk) in relationship.Links)
                            fields.Add(name, fk.Prop.GetValue(obj));
                        break;
                    case OneToMany list:
                        if (!list.Cascade.HasFlag(Cascade.SAVE)) break;
                        var l = obj as IPList;
                        foreach (var o in l)
                        {
                            if(o != null)
                                list.Relationship.Prop.SetValue(o,this);
                        }

                        later.Later(() => l.Save());
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
            
            if (res == -1) return false;
            UpdateIdentifiers(table, res);
            later.Run();
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

            var reader = Persistence.Sql.Select(table, field, 0, 1<<31-1);
            var list = new PList<T>();
            list.BuildList(reader);
            return list;
        }

        public static PList<T> FindWhereQuery<T>(string whereQuery) where T : DAO
        {
            return new PList<T>(whereQuery);
        }
    }
}