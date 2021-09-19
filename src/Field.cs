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