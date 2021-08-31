using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Persistence
{

    public class Table
    {
        public Type Type { get; internal set; }

        public string Name => Type.Name;

        private readonly Dictionary<string, PropColumn> _columns;

        private readonly TableAttribute _tableAttribute;
        protected internal Dictionary<string, PropColumn>.ValueCollection Columns => _columns.Values;
        protected internal readonly List<PrimaryKey> PrimaryKeys;
        protected internal readonly Dictionary<string, Relationship> Relationships;

        public Table(TableAttribute tableAttribute)
        {
            _tableAttribute = tableAttribute;
            PrimaryKeys = new List<PrimaryKey>();
            Relationships = new Dictionary<string, Relationship>();
            _columns = new Dictionary<string, PropColumn>(StringComparer.InvariantCultureIgnoreCase);
        }

        public PropColumn TryGetColumn(string propName)
        {
            _columns.TryGetValue(propName, out var column);
            return column;
        }

        public bool DefaultPk { get; internal set; } = true;
        public string Schema => _tableAttribute.Schema ?? Persistence.Sql.DefaultSchema;
        public string SqlName => _tableAttribute.Name;
        public bool Versioned => _tableAttribute.VersionControl;
        public bool IsSpecialization { get; set; }
        public Table BaseTable { get; set; }

        public void AddColumn(PropColumn propColumn)
        {
            propColumn.Table = this;
            _columns.Add(propColumn.Prop.Name, propColumn);
        }

        public void AddPrimaryKey(PrimaryKey primaryKey)
        {
            if (primaryKey.AutoIncrement)
                PrimaryKeys.Insert(0, primaryKey);
            else
                PrimaryKeys.Add(primaryKey);
            AddColumn(primaryKey);
        }

        public void AddRelationship(PropertyInfo pi, string tableRef)
        {
            if (Relationships.ContainsKey(tableRef))
            {
                throw new PersistenceException($"Error on Persist Specialization. " +
                                               $"The table already has a relationship with this name(\"{tableRef}\")");
            }
            Relationships.Add(tableRef, new Relationship
            {
                Prop = pi,
                Type = RelationshipType.Specialization,
                ReferenceName = tableRef
            });
        }
        
        public void AddRelationship(PropertyInfo pi, string tableRef, ManyToOneAttribute manyToOne)
        {
            var name = manyToOne.ReferencedName ?? tableRef;
            if (Relationships.ContainsKey(name))
            {
                throw new PersistenceException($"Error on Persist Relationship {pi}. " +
                                               $"The table already has a relationship with this name(\"{name}\")");
            }

            var rel = new Relationship(manyToOne)
            {
                Prop = pi,
                Type = RelationshipType.ManyToOne,
                ReferenceName = name
            };
            Relationships.Add(name,rel);
            AddColumn(rel);
        }
    }

    public class OneToMany : PropColumn
    {
        public Relationship Relationship { get; internal set; }
        public string ReferencedName { get;internal set; }
        public Type Type => Prop.PropertyType.GenericTypeArguments[0];
        public bool orphanRemoval { get; }
        public uint ItemsByAccess { get; } = 1000;
        public Cascade Cascade { get; }
        public Fetch Fetch { get; }

        public OneToMany(OneToManyAttribute attribute)
        {
            Cascade = attribute.Cascade;
            Fetch = attribute.Fetch;
            ItemsByAccess = attribute.ItemsByAccess;
            orphanRemoval = attribute.orphanRemoval;
            ReferencedName = attribute.ReferencedName;
        }
    }

}