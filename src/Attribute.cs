using System;
using System.Data;

namespace Persistence
{
    public abstract class PersistenceAttribute : Attribute
    {
    }
    
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class UniqueIndexAttribute : FieldAttribute
    {
        
    }
    
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ViewAttribute : PersistenceAttribute
    {
        public string ViewName { get; set; }
        public string Schema { get; set; } = Persistence.Sql.DefaultSchema;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class NamedQuery : PersistenceAttribute
    {
        public string Name { get; set; }
        public string Query { get; set; }
        public Type ReturnType { get; set; }

    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public class StoredFunctionAttribute : PersistenceAttribute
    {
        public string FunctionName { get; set; }
        public String[] ProcParams { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class FieldAttribute : PersistenceAttribute
    {
        public string FieldName { get; set; }
        public SqlDbType FieldType { get; set; }
        public int Length { get; set; }
        public int Precision { get; set; }
        public Nullable Nullable { get; set; }
        public bool UniqueIndex { get; set; }
        public object DefaultValue { get; set; }
        public bool ReadOnly { get; set; }

        public FieldAttribute()
        {
        }

    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ManyToOneAttribute : PersistenceAttribute
    {
        public string ReferencedName { get; set; }
        public Fetch Fetch { get; set; }
        public Cascade Cascade { get; set; }
        public Nullable Nullable { get; set; }

        public bool Updatable { get; set; } = true;
        
        public ManyToOneAttribute(string ReferencedName = null, Fetch Fetch = Fetch.Lazy, Cascade Cascade = Cascade.NULL)
        {
            this.ReferencedName = ReferencedName;
            this.Fetch = Fetch;
            this.Cascade = Cascade;
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class OneToManyAttribute : PersistenceAttribute
    {
        public string ReferencedName { get; set; }
        public Fetch Fetch { get; set; }
        public Cascade Cascade { get; set; }
        public bool orphanRemoval { get; set; }
        public uint ItemsByAccess { get; set; } = 1000;

    }

    [AttributeUsage(AttributeTargets.Property)]
    public class PrimaryKeyAttribute : FieldAttribute
    {
        public object DefaultValue { get; set; }
        public bool AutoIncrement { get; set; }

        public PrimaryKeyAttribute()
        {
            
        }

        internal PrimaryKeyAttribute(PrimaryKeyAttribute pk)
        {
            DefaultValue = pk.DefaultValue;
            AutoIncrement = pk.AutoIncrement;
            FieldName = pk.FieldName;
            FieldType = pk.FieldType;
            Length = pk.Length;
            Precision = pk.Precision;
        }
    }
    
    [AttributeUsage(AttributeTargets.Property)]
    internal class DefaultPkAttribute : PrimaryKeyAttribute
    {
    }
    
    [AttributeUsage(AttributeTargets.Class)] 
    public class TableAttribute : Attribute
    {
        public string Name { get; set; }
        public string Schema { get; set; }
        public bool VersionControl { get; set; }
    }
    
}