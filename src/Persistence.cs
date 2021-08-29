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

        internal static readonly PrimaryKey DefaultPkColumn;

        static Persistence()
        {
            var prop = typeof(DAO).GetProperty("Id");
            DefaultPkColumn = new PrimaryKey(prop.GetCustomAttribute<PrimaryKeyAttribute>()) { Prop = prop};
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

            Tables.Values.Do(Persist);
            Tables.Values.Do(ProcessForeignKeys);

        }

        private static void ProcessForeignKeys(Table table)
        {
            foreach (var (refTable, list) in table.Relationships)
            {
                foreach (var relationship in list)
                {
                    if (!Tables.TryGetValue(refTable, out var tablePk))
                    {
                        throw new PersistenceException(
                            $"Property {relationship.Prop.Name} of table {table.Type} depends on table {refTable} to be persisted");
                    }

                    relationship.Table = table;
                    relationship.TableReferenced = Tables[refTable];
                    tablePk.PrimaryKeys.Do(columnPk => relationship.AddKey(columnPk));
                    Sql.ValidadeForeignKeys(table, relationship);
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
                var col = (OneToMany)column;
                col.PrimaryKeys = table.PrimaryKeys;
                col.Persisted = true;
            }

            foreach (var column1 in columns.Where(column =>
                column is Field { Persisted: false } && !(column is PrimaryKey)))
            {
                var column = (Field)column1;
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
                "IF(NEW.__Version<>OLD.__Version) THEN signal sqlstate '45000' set message_text = " +
                $"'YOU ARE UPGRADING FROM AN OLD VERSION', MYSQL_ERRNO = {SQLException.ErrorCodeVersion}; END IF; " +
                "SET NEW.__Version = OLD.__Version + 1; END";
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
                                 new TableAttribute { Schema = Sql.DefaultSchema, Name = type.Name };
            var table = new Table(tableAttribute) { Type = type };
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
                    {
                        table.AddPrimaryKey(new PrimaryKey(new PrimaryKeyAttribute(_pk))
                            { Prop = pi, SqlName = $"{type.BaseType.Name}_{_pk.FieldName}" });
                        table.AddRelationship(pi, type.BaseType.Name);
                    }

                    continue;
                }

                switch (att[0])
                {
                    case DefaultPkAttribute _:
                        break;
                    case PrimaryKeyAttribute pk:
                        table.AddPrimaryKey(new PrimaryKey(pk) { Prop = pi });
                        break;
                    case FieldAttribute field:
                        table.AddColumn(new Field(field) { Prop = pi });
                        break;
                    case ManyToOneAttribute manyToOne:
                        if (pi.PropertyType.IsSubclassOf(typeof(DAO)))
                        {
                            table.AddRelationship(pi, pi.PropertyType.Name, manyToOne);
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
                            table.AddColumn(new OneToMany(oneToMany) { Prop = pi });
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

}