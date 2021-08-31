using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Persistence
{
    public static class Persistence
    {
        internal static readonly Dictionary<string, Table> Tables = new Dictionary<string, Table>();
        internal static readonly Dictionary<Table, Storage> Storage = new Dictionary<Table, Storage>();
        internal static ISQL Sql;
        internal static readonly PrimaryKey DefaultPkColumn;

        static Persistence()
        {
            var prop = typeof(DAO).GetProperty("Id");
            DefaultPkColumn = new PrimaryKey(prop.GetCustomAttribute<PrimaryKeyAttribute>()) { Prop = prop};
        }

        internal static void BuildTables()
        {
            foreach (var type in
                from assembly in AppDomain.CurrentDomain.GetAssemblies()
                from type in assembly.GetTypes()
                where type.IsSubclassOf(typeof(DAO)) && type.GetCustomAttribute<TableAttribute>() != null
                select type)
            {
                Init(type);
            }
        }

        public static void Init(ISQL sql)
        {
            DAO.Init();
            Sql = sql;
            Tables.Values.Do(ProcessPkAndFields);
            Tables.Values.Do(ProcessForeignKeys);
            Tables.Values.Do(ProcessOneToMany);
        }

        private static void ProcessOneToMany(Table table)
        {
            foreach (var col in table.Columns.OfType<OneToMany>())
            {
                col.ReferencedName ??= col.Type.Name;
                var refTable = Tables[col.Type.Name];
                if (!refTable.Relationships.TryGetValue(table.Name, out var rel) && rel == null)
                    throw new PersistenceException(
                        $"Error on auto get relationship to persist property OneToMany {col.Prop}");
                col.Relationship = rel;
                col.Persisted = true;
            }
        }

        private static void ProcessForeignKeys(Table table)
        {
            foreach (var (name, relationship) in table.Relationships)
            {
                var tablePkName = name;
                if(relationship.Type != RelationshipType.Specialization)
                {
                    tablePkName = relationship.Prop.Name;
                }
                if (!Tables.TryGetValue(tablePkName, out var tablePk))
                {
                    throw new PersistenceException(
                        $"Property {relationship.Prop.Name} of table {table.Type} depends on table {tablePkName} to be persisted");
                }

                relationship.Table = table;
                relationship.TableReferenced = tablePk;
                tablePk.PrimaryKeys.Do(columnPk => relationship.AddKey(columnPk));
                Sql.ValidadeForeignKeys(table, relationship);
            }
        }

        private static void ProcessPkAndFields(Table table)
        {
            if (table.Versioned)
                BuildVersionedField(table);
            if (table.IsSpecialization)
                table.BaseTable = Tables[table.Type.BaseType.Name];

            var columns = table.Columns;
            Sql.ValidatePrimaryKeys(table, table.PrimaryKeys);
            table.PrimaryKeys.Do(pk => pk.Persisted = true);

            foreach (var column1 in columns.Where(column =>
                column is Field { Persisted: false } && !(column is PrimaryKey)))
            {
                var column = (Field)column1;
                Sql.ValidateField(table, column);
                column.Persisted = true;
            }

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
                        if (pi.PropertyType.GetInterfaces().Contains(typeof(IPList)))
                        {
                            table.AddColumn(new OneToMany(oneToMany) { Prop = pi });
                        }
                        else
                        {
                            throw new PersistenceException(
                                "The property type for using the OneToMany attribute must be " + typeof(PList<>).Name);
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