using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Type = System.Type;

namespace Persistence
{
    public class DAO : INotifyPropertyChanged
    {
        internal static bool Init() => true;
        internal readonly Dictionary<PropColumn, object> LastKeys;
        private HashSet<string> LastChanges;

        internal Table Table => Persistence.Tables[GetType().Name];

        internal IStorage _storage => Context?.GetStorage(Table);

        private long _Version;
        private long _id;

        private bool _NotLoaded { get; set; } = true;

        private bool _isChanged;

        public bool IsChanged
        {
            get => _isChanged;
            set
            {
                _isChanged = value;
                if (!_isChanged) LastChanges.Clear();
            }
        }

        internal bool Loaded => !_NotLoaded;

        internal bool Persisted { get; set; }

        public PersistenceContext? Context { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        [DefaultPk(FieldName = "Id", FieldType = SqlDbType.BigInt, AutoIncrement = true, DefaultValue = 0l)]
        public long Id
        {
            get => _id;
            set => UpdateId(value);
        }

        public bool Load() => Load(Table);

        public bool Save()
        {
            var we = new WorkerExecutor();
            var res = Save(Table, we);
            if (res && we.Commit())
            {
                return res;
            }

            we.Rollback();
            return false;
        }

        public bool Delete()
        {
            var executor = new WorkerExecutor();
            var res = Delete(Table, executor);
            if (res && executor.Commit())
            {
                return true;
            }

            executor.Rollback();
            return false;
        }

        static DAO()
        {
            Persistence.BuildTables();
        }

        protected DAO()
        {
            LastKeys = new Dictionary<PropColumn, object>();
            IsChanged = true;
            ctor(GetType());
        }

        private void ctor(Type type)
        {
            if (type.BaseType != typeof(DAO))
                ctor(type.BaseType);

            var table = Persistence.Tables[type.Name];
            LastChanges = new HashSet<string>(table.Columns.Select(column => column.Prop.Name));
            foreach (var col in table.Columns.OfType<PrimaryKey>())
            {
                LastKeys.Add(col, null);
            }

            foreach (var col in table.Columns.OfType<OneToMany>())
            {
                col.Prop.SetValue(this, (IPList) Activator.CreateInstance(col.Prop.PropertyType));
            }
        }

        public void Refresh()
        {
            Load();
        }

        private void UpdateId(long value)
        {
            _id = value;
        }

        internal bool Delete(Table table, WorkerExecutor executor)
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

            var transaction = executor.DbTransaction;
            var result = Persistence.Sql.Delete(table, keys, ref transaction);
            executor.DbTransaction = transaction;
            return result && (!table.IsSpecialization || Delete(table.BaseTable, executor));
        }

        public static T? Load<T>(long id) where T : DAO
        {
            var obj = Activator.CreateInstance<T>();
            obj.Id = id;
            return obj.Load() ? obj : null;
        }

        private static object Load(long id, Type t)
        {
            var obj = (DAO) Activator.CreateInstance(t);
            obj?.Load();
            return obj;
        }

        internal void Build(IPReader reader, Table table, RunLater runLater)
        {
            object value;
            if (table.Versioned)
            {
                value = reader.DataReader.Read("__Version");
                _Version = (long) (value ?? 0);
            }

            if (table.IsSpecialization)
                runLater.Later(_ => Load(table.BaseTable));

            foreach (var column in table.Columns)
            {
                switch (column)
                {
                    case Relationship rel:
                        var dao = (DAO) Activator.CreateInstance(rel.Prop.PropertyType);
                        dao.Context = Context;
                        var isNull = false;
                        foreach (var pair in rel.Links)
                        {
                            value = reader.DataReader.Read(pair.Key);
                            if (value == null)
                            {
                                isNull = true;
                                break;
                            }

                            pair.Value.Prop.SetSqlValue(dao, value);
                        }

                        if (isNull) continue;

                        if (dao._storage == null || dao._storage.TryAdd(ref dao))
                        {
                            if (rel.Fetch == Fetch.Eager)
                                runLater.Later(_ => dao.Load());
                            else
                                dao._NotLoaded = true;
                        }


                        rel.Prop.SetValue(this, dao);
                        break;
                    case OneToMany o2m:
                        var obj = (IPList) Activator.CreateInstance(o2m.Prop.PropertyType);
                        o2m.Prop.SetValue(this, obj);
                        runLater.Later(10, _ => obj.LoadList(o2m, this));
                        break;
                    case Field field:
                        value = reader.DataReader.Read(field.SqlName);
                        field.Convert(ref value);
                        if (field is PrimaryKey)
                            LastKeys[field] = value;
                        field.Prop.SetSqlValue(this, value);
                        break;
                }
            }

            _NotLoaded = false;
            IsChanged = false;
        }

        internal void Free()
        {
            if (_storage == null)
                return;
            _storage.Free(this);
            Context = null;
            foreach (var column in Table.Columns)
            {
                DAO dao = null;
                switch (column)
                {
                    case OneToMany otm:
                        if (otm.Cascade.HasFlag(Cascade.FREE))
                            dao = otm.Prop.GetValue(this) as DAO;
                        break;
                    case Relationship rel:
                        if (rel.Cascade.HasFlag(Cascade.FREE))
                            dao = rel.Prop.GetValue(this) as DAO;
                        break;
                }

                dao?.Free();
            }
        }

        private bool Load(Table table)
        {
            var keys = table.PrimaryKeys.ToDictionary(pk => pk.SqlName, pk => pk.Prop.GetValue(this));
            IPReader reader = null;
            try
            {
                reader = Persistence.Sql.Select(new SelectParameters(table) {Keys = keys, Length = 1});
                var runLater = new RunLater(null);
                if (reader.DataReader.Read())
                {
                    Build(reader, table, runLater);
                    reader.Close();
                    runLater.Run();
                    IsChanged = false;
                }
                else
                {
                    reader.Close();
                    return false;
                }
            }
            catch (Exception ex)
            {
                reader?.Close();
                throw new PersistenceException("Error on Load", ex);
            }

            return true;
        }

        internal bool Save(Table table, WorkerExecutor executor)
        {
            var later = new RunLater(executor);
            if (table.IsSpecialization && !Save(table.BaseTable, executor))
                return false;
            var fields = new Dictionary<PropColumn, object>();

            foreach (var column in table.Columns)
            {
                var pi = column.Prop;
                var obj = pi.GetValue(this);
                switch (column)
                {
                    case PrimaryKey:
                        break;
                    case Field fa:
                        if (fa.ReadOnly)
                            break;
                        if (fa.IsEnum)
                            obj = (int) obj;
                        if (LastChanges.Contains(column.Prop.Name) || column is UniqueIndex)
                            fields.Add(fa, obj);
                        break;
                    case Relationship relationship:
                        var dao = (DAO) obj;
                        if (obj == null && relationship.Nullable == Nullable.NotNull)
                            throw new PersistenceException($"Property value {relationship.Prop} cannot be null");
                        if (LastChanges.Contains(column.Prop.Name))
                        {
                            foreach (var pair in relationship.Links)
                                fields.Add(pair.Value, obj is null ? null : pair.Value.Prop.GetValue(obj));
                        }

                        if (relationship.Cascade.HasFlag(Cascade.SAVE) && dao is not null &&
                            !dao.Save(dao.Table, executor))
                            return false;
                        
                        break;
                    case OneToMany list:
                        if (!list.Cascade.HasFlag(Cascade.SAVE) || obj == null) break;
                        var l = obj as IPList;
                        if (!l.IsChanged) break;
                        foreach (var o in l)
                        {
                            if (o != null)
                                list.Relationship.Prop.SetValue(o, this);
                        }

                        later.Later((e) => l.Save(e));
                        break;
                }
            }

            if (!IsChanged && (fields.Count == 0 || !table.Versioned))
            {
                later.Run();
                return true;
            }
            
            // TODO implement version control
            //if (table.Versioned)
            //    fields.Add("__Version", _Version);

            var keyChange = false;
            var keyValues = new Dictionary<PropColumn, object>();
            foreach (var column in table.Columns.OfType<PrimaryKey>())
            {
                var obj = column.Prop.GetValue(this);
                keyValues.Add(column, obj);
                var lastKey = LastKeys[column];
                if (obj == null)
                    throw new PersistenceException("The PrimaryKey cannot be null");
                if (obj.Equals(column.DefaultValue))
                    if (!column.AutoIncrement)
                        throw new PersistenceException(
                            "The PrimaryKey must be different from the default");
                    else
                        continue;
                if (lastKey != null && !lastKey.Equals(obj))
                {
                    keyChange = true;
                }

                fields.Add(column, obj);
            }

            long res;
            try
            {
                var transaction = executor.DbTransaction;
                if (Loaded)
                {
                    res = Persistence.Sql.Update(table, fields, keyValues, ref transaction);
                }
                else
                {
                    res = Persistence.Sql.Insert(table, fields, ref transaction);
                }

                executor.DbTransaction = transaction;
            }
            catch (Exception ex)
            {
                if (ex is SQLException {ErrorCode: SQLException.ErrorCodeVersion})
                {
                    return false;
                }

                executor.Rollback();
                throw new PersistenceException($"Error on save object {ToString()}", ex)
                    {ErrorCode = ex is SQLException sqlex ? sqlex.ErrorCode : 0};
            }

            if (res == -1) return false;
            var lastIdentifiers = LastKeys.ToDictionary(e => e.Key, e => e.Value);
            var lastId = _id;
            executor.OnRollback += () =>
            {
                if (table.DefaultPk && _id == 0)
                {
                    _id = lastId;
                }

                foreach (var id in lastIdentifiers)
                {
                    LastKeys[id.Key] = id.Value;
                }
            };
            UpdateIdentifiers(table, res);
            _NotLoaded = false;
            later.Run();
            _NotLoaded = true;
            executor.OnCommit += () =>
            {
                if (_storage is not null)
                    _storage.AddOrUpdate(this);
                IsChanged = false;
                _NotLoaded = false;
            };

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

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            LastChanges.Add(propertyName);
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
                            foreach (var pair in m2o.Links)
                                field.Add(pair.Key, pair.Value.Prop.GetValue(value));
                            break;
                        case Field _:
                            field.Add(col.SqlName, value);
                            break;
                    }
                }
            }

            var reader = Persistence.Sql.Select(new SelectParameters(table) {Keys = field});
            var list = new PList<T>();
            ((IPList) list).BuildList(reader);
            return list;
        }

        public static PList<T> All<T>() where T : DAO
        {
            var list = new PList<T>();
            list.GetAll();
            return list;
        }

        public DAO Clone(PersistenceContext context = null)
        {
            var map = new Dictionary<object, object>();
            var copy = Activator.CreateInstance(GetType());
            object value = null;
            object copyObject;
            map.Add(this, copy);
            var queue = new Queue<object>();
            queue.Enqueue(this);
            while (queue.Count > 0)
            {
                var old = queue.Dequeue();
                copy = map[old];
                foreach (var propertyInfo in old.GetType().GetProperties())
                {
                    try
                    {
                        value = propertyInfo.GetValue(old);
                        if (value is null || propertyInfo.PropertyType.IsSimpleType())
                        {
                            if (propertyInfo.GetSetMethod(false) is null) continue;
                            propertyInfo.SetValue(copy, value);
                        }
                        else if (map.TryGetValue(value, out copyObject))
                        {
                            propertyInfo.SetValue(copy, copyObject);
                        }
                        else if (propertyInfo.PropertyType.GetInterfaces().Contains(typeof(IPList)))
                        {
                            var list = (IPList) value;
                            var newValue = list.Clone(context);
                            foreach (DAO o in list)
                            {
                                if (o is null)
                                    newValue.Add(null);
                                else if (map.TryGetValue(o, out var co))
                                    newValue.Add(co);
                            }

                            propertyInfo.SetValue(copy, newValue);
                        }
                        else
                        {
                            copyObject = Activator.CreateInstance(value.GetType());
                            propertyInfo.SetValue(copy, copyObject);
                            map.Add(value, copyObject);
                            queue.Enqueue(value);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(propertyInfo.Name + " in " + copy.GetType().Name + " with " + value);
                        Console.WriteLine(ex);
                    }
                }

                if (copy is DAO dao) dao.Context = context;
            }

            return (DAO) map[this];
        }
    }
}