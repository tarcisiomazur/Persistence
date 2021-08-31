namespace Persistence
{
    public class PrimaryKey : Field
    {
        public PrimaryKey(PrimaryKeyAttribute attribute) : base(attribute)
        {
            Nullable = Nullable.NotNull;
            AutoIncrement = attribute.AutoIncrement;
            DefaultValue = attribute.DefaultValue;
        }

        public PrimaryKey(PrimaryKey defaultPkColumn) : base(defaultPkColumn.Attribute)
        {
            Prop = defaultPkColumn.Prop;
            Nullable = Nullable.NotNull;
            AutoIncrement = defaultPkColumn.AutoIncrement;
            DefaultValue = defaultPkColumn.DefaultValue;
        }

        public bool AutoIncrement { get; }
    }
}