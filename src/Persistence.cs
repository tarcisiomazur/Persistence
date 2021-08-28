using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Priority_Queue;

namespace Persistence
{
    public class Persistence
    {
        protected internal static readonly Dictionary<string, Table> Tables = new Dictionary<string, Table>();
        protected internal static readonly Dictionary<Table, Storage> Storage = new Dictionary<Table, Storage>();
        protected internal static ISQL Sql;

        private static readonly PrimaryKey DefaultPkColumn;

        static Persistence()
        {
            var prop = typeof(DAO).GetProperty("Id");
            DefaultPkColumn = new PrimaryKey(prop.GetCustomAttribute<PrimaryKeyAttribute>()) {Prop = prop};
        }

        public static void Init(ISQL sql)
        {
            Sql = sql;
            foreach (var type in new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Assembly?.GetTypes())
            {
                if (type.GetCustomAttribute<TableAttribute>() != null)
                {
                    Init(type);
                }
            }

            foreach (var (_, table) in Tables)
            {
                Persist(table);
            }
            foreach (var (_, table) in Tables)
            {
                ProcessForeignKeys(table);
            }
            
        }

        private static void ProcessForeignKeys(Table table)
        {
            foreach (var (refTable,list) in table.ForeignKeys)
            {
                foreach (var manyToOne in list)
                {
                    if (!Tables.TryGetValue(refTable, out var tablePk))
                    {
                        throw new PersistenceException(
                            $"Property {manyToOne.Prop.Name} of table {table.Type} depends on table {refTable} to be persisted");
                    }

                    manyToOne.Table = table;
                    manyToOne.TableReferenced = Tables[refTable];
                    tablePk.Columns.Where(c => c is PrimaryKey).Do(columnPk =>
                    {
                        manyToOne.AddFk((Field) columnPk);
                    });
                    Sql.ValidadeForeignKeys(table,  manyToOne);
                }
            }
        }
        private static void Persist(Table table)
        {
            var columns = table.Columns;
            Sql.ValidatePrimaryKeys(table, table.PrimaryKeys);
            table.PrimaryKeys.Do(pk => pk.Persisted = true);

            foreach (var column in columns.Where(c => c is OneToMany))
            {
                var col = (OneToMany) column;
                col.PrimaryKeys = table.PrimaryKeys;
                col.Persisted = true;
            }

            foreach (var column1 in columns.Where(column => column is Field {Persisted: false} && !(column is PrimaryKey)))
            {
                var column = (Field) column1;
                Sql.ValidateField(table, column);
                column.Persisted = true;
            }

            if (table.Versioned)
                BuildVersionedField(table);
            Storage.Add(table, new Storage(table.DefaultPk));
        }

        private static void BuildVersionedField(Table table)
        {
            var triggerName = $"{table.SqlName}_Version";
            var sqlTrigger =
                "IF(NEW.__Version<>OLD.__Version) THEN signal sqlstate '50001' set message_text = 'YOU ARE UPGRADING TO AN OLD VERSION'; " +
                "END IF; SET NEW.__Version = OLD.__Version + 1; END";
            var field = new Field
            {
                DefaultValue = 1,
                Nullable = Nullable.NotNull,
                SqlName = "__Version",
                SqlType = SqlDbType.BigInt
            };
            if (!Sql.ValidateField(table, field))
            {
                throw new PersistenceException(
                    $"An error has occurred, version control will not be possible in table {table.SqlName}.");
            }

            if (!Sql.ExistTrigger(table, triggerName))
            {
                Sql.CreateTrigger(table, sqlTrigger, triggerName, ISQL.SqlTriggerType.BEFORE_UPDATE);
            }
        }

        private static void Init(Type type)
        {
            var tableAttribute = type.GetCustomAttribute<TableAttribute>() ??
                                 new TableAttribute {Schema = Sql.DefaultSchema, Name = type.Name};
            var table = new Table(tableAttribute) {Type = type};
            if (type.BaseType == null || !type.IsSubclassOf(typeof(DAO)))
            {
                throw new PersistenceException(
                    $"The class {type.Name} needs extend {typeof(DAO)} to persist.");
            }
            table.IsSpecialization = type.BaseType != typeof(DAO);
            
            foreach (var pi in type.GetProperties())
            {
                var att = pi.GetCustomAttributes(typeof(PersistenceAttribute), true);
                if (att.Length == 0) continue;
                if (att.Length > 1)
                {
                    throw new PersistenceException(
                        $"Multiple PersistenceAttribute in Property {pi.Name}({pi.PropertyType}) in {type.Name}.");
                }

                if (table.IsSpecialization && pi.DeclaringType != type)
                {
                    if (att[0] is PrimaryKeyAttribute _pk)
                        table.AddPrimaryKey(new PrimaryKey(new PrimaryKeyAttribute(_pk)) {Prop = pi, SqlName = $"{type.BaseType.Name}_{_pk.FieldName}"});
                    continue;
                }

                switch (att[0])
                {
                    case DefaultPkAttribute _:
                        break;
                    case PrimaryKeyAttribute pk:
                        table.AddPrimaryKey(new PrimaryKey(pk) {Prop = pi});
                        break;
                    case FieldAttribute field:
                        table.AddColumn(new Field(field) {Prop = pi});
                        break;
                    case ManyToOneAttribute manyToOne:
                        if (pi.PropertyType.IsSubclassOf(typeof(DAO)))
                        {
                            table.AddForeignKey(manyToOne, pi);
                        }
                        else
                        {
                            throw new PersistenceException(
                                "The property type for using the ManyToOne attribute must be subclass of " +
                                typeof(DAO));
                        }

                        break;
                    case OneToManyAttribute oneToMany:
                        if (pi.PropertyType.GetInterfaces().Contains(typeof(IMyList)))
                        {
                            table.AddColumn(new OneToMany(oneToMany) {Prop = pi});
                            Console.WriteLine("MyList OK");
                        }
                        else
                        {
                            throw new PersistenceException(
                                "The property type for using the OneToMany attribute must be " + typeof(MyList<>).Name);
                        }

                        break;
                }
            }

            if (table.PrimaryKeys.Count == 0)
            {
                table.AddPrimaryKey(new PrimaryKey(DefaultPkColumn));
            }
            Tables.Add(type.Name, table);
        }

    }


    [Flags]
    public enum Fetch
    {
        Lazy,
        Eager
    }
    
    [Flags]
    public enum Nullable
    {
        Null,
        NotNull
    }

    [Flags]
    public enum Cascade
    {
        NULL = 0x00,
        SAVE = 0x01,
        REMOVE = 0x02,
        REFRESH = 0x04,
        FREE = 0x08,
        PERSIST = SAVE | REMOVE | REFRESH | FREE
    }

    public class RunLater
    {
        private readonly SimplePriorityQueue<Action, int> _functions = new SimplePriorityQueue<Action, int>();

        public void Later(short priority, Action action)
        {
            _functions.Enqueue(action, priority);
        }

        public void Later(Action action)
        {
            _functions.Enqueue(action, 0);
        }

        public void Run()
        {
            _functions.Do(action => action());
            _functions.Clear();
        }

    }
}