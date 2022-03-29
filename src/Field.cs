using System.Data;

namespace Persistence
{
    public class Field : PropColumn
    {
        protected readonly FieldAttribute Attribute;

        public SqlDbType SqlType { get; internal set; }

        public int Precision { get; }

        public int Length { get; }

        public object DefaultValue { get; internal set; }

        public bool IsEnum { get; set; }

        public bool ReadOnly { get; }

        public bool Updatable { get; set; } = true;

        public Field(FieldAttribute attribute)
        {
            Attribute = attribute;
            DefaultValue = attribute.DefaultValue;
            SqlName = attribute.FieldName;
            SqlType = attribute.FieldType;
            Precision = attribute.Precision;
            Length = attribute.Length;
            Nullable = attribute.Nullable;
            ReadOnly = attribute.ReadOnly;
        }

        internal Field(Field field)
        {
            Nullable = field.Nullable;
            DefaultValue = field.DefaultValue;
            Prop = field.Prop;
            Table = field.Table;
            Persisted = field.Persisted;
            IsEnum = field.IsEnum;
            SqlName = field.SqlName;
            Updatable = field.Updatable;
            SqlType = field.SqlType;
            Precision = field.Precision;
            Length = field.Length;
            ReadOnly = field.ReadOnly;
            Attribute = field.Attribute;
        }


        internal Field()
        {
            
        }
    }
}