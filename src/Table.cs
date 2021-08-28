using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace Persistence
{

    public class Table
    {
        public Type Type { get; internal set; }

        private readonly Dictionary<string, PropColumn> _columns;

        private readonly TableAttribute _tableAttribute;
        protected internal Dictionary<string, PropColumn>.ValueCollection Columns => _columns.Values;
        protected internal readonly List<PrimaryKey> PrimaryKeys;
        protected internal readonly Dictionary<string, List<ManyToOne>> ForeignKeys;

        public Table(TableAttribute tableAttribute)
        {
            _tableAttribute = tableAttribute;
            PrimaryKeys = new List<PrimaryKey>();
            ForeignKeys = new Dictionary<string, List<ManyToOne>>();
            _columns = new Dictionary<string, PropColumn>(StringComparer.InvariantCultureIgnoreCase);
        }

        public bool DefaultPk { get; internal set; } = true;
        public string Schema => _tableAttribute.Schema ?? Persistence.Sql.DefaultSchema;
        public string SqlName => _tableAttribute.Name;
        public bool Versioned => _tableAttribute.VersionControl;
        public bool IsSpecialization { get; set; }

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

        public void AddForeignKey(ManyToOneAttribute manyToOne, PropertyInfo pi)
        {
            var tableRef = pi.PropertyType.Name;
            ForeignKeys.TryGetValue(tableRef, out var list);
            if (list == null)
            {
                list = new List<ManyToOne>();
                ForeignKeys.Add(tableRef, list);
            }

            list.Add(new ManyToOne(manyToOne) {Prop = pi});
        }
    }

    public class PropColumn
    {
        public Table Table { get; internal set; }
        public PropertyInfo Prop { get; internal set; }
        protected internal bool Persisted { get; internal set; }
        public Nullable Nullable { get; internal set; }

        public PropColumn(PropertyInfo prop)
        {
            Prop = prop;
        }

        public PropColumn()
        {
        }
    }


    public class ManyToOne : PropColumn
    {
        private readonly ManyToOneAttribute _m2o;
        public readonly List<Field> Links;
        public string FkName => GetFkName();
        public Cascade Cascade { get; }
        public Fetch Fetch { get; }

        public Table TableReferenced;

        public ManyToOne(ManyToOneAttribute m2o)
        {
            _m2o = m2o;
            Nullable = m2o.Nullable;
            Cascade = m2o.Cascade;
            Fetch = m2o.Fetch;
            Links = new List<Field>();
        }


        public string FieldFkName(Field f)
        {
            return $"{TableReferenced.SqlName}_{f.SqlName}";
        }

        private string GetFkName()
        {
            var name = $"fk_{Table.SqlName}_{TableReferenced.SqlName}";
            if (!string.IsNullOrEmpty(_m2o.ReferencedName))
                name += $"_{_m2o.ReferencedName}";
            if (Links.Count == 1)
                name += $"_{Links[0].SqlName}";
            return name;

        }
    }

    public class OneToMany : PropColumn
    {
        public List<PrimaryKey> PrimaryKeys;

        public Type Type => Prop.PropertyType.GenericTypeArguments[0];

        public OneToMany(OneToManyAttribute attribute)
        {
        }
    }

    public class PrimaryKey : Field
    {
        public PrimaryKey(PrimaryKeyAttribute attribute) : base(attribute)
        {
            Nullable = Nullable.NotNull;
            AutoIncrement = attribute.AutoIncrement;
        }

        public PrimaryKey(PrimaryKey defaultPkColumn) : base(defaultPkColumn.Attribute)
        {
            Prop = defaultPkColumn.Prop;
            Nullable = Nullable.NotNull;
            AutoIncrement = defaultPkColumn.AutoIncrement;
        }

        public bool AutoIncrement { get; }
    }

    public class Field : PropColumn
    {
        protected readonly FieldAttribute Attribute;
        public string SqlName { get; internal set; }
        public SqlDbType SqlType { get; internal set; }
        public int Precision { get; }
        public int Length { get; }
        public object DefaultValue { get; internal set; }

        public Field(FieldAttribute attribute)
        {
            Attribute = attribute;
            DefaultValue = attribute.DefaultValue;
            SqlName = attribute.FieldName;
            SqlType = attribute.FieldType;
            Precision = attribute.Precision;
            Length = attribute.Length;
            Nullable = attribute.Nullable;
        }

        internal Field()
        {

        }
    }
}