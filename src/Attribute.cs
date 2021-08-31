using System;
using System.Data;

namespace Persistence
{
    public abstract class PersistenceAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class StoredProcedureAttributes : PersistenceAttribute
    {
        public string ProcedureName { get; set; }
        public ProcedureTypeCommand ProcedureType { get; set; }
        public String[] ProcParams { get; set; }

        public StoredProcedureAttributes(string procName, ProcedureTypeCommand procType, params String[] param)
        {
            ProcedureName = procName;
            ProcedureType = procType;
            ProcParams = param;
        }
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

        public FieldAttribute()
        {
        }

        public FieldAttribute(string FieldName, SqlDbType FieldType = SqlDbType.Text)
        {
            this.FieldName = FieldName;
            this.FieldType = FieldType;
        }

        public FieldAttribute(string FieldName, SqlDbType FieldType, int Length)
        {
            this.FieldName = FieldName;
            this.FieldType = FieldType;
            this.Length = Length;
        }

        public FieldAttribute(string FieldName, SqlDbType FieldType, int Length, int Precision)
        {
            this.FieldName = FieldName;
            this.FieldType = FieldType;
            this.Length = Length;
            this.Precision = Precision;
        }

    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ManyToOneAttribute : PersistenceAttribute
    {
        public string ReferencedName { get; set; }
        public Fetch Fetch { get; set; }
        public Cascade Cascade { get; set; }
        public Nullable Nullable { get; set; }

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

        public PrimaryKeyAttribute(string FieldName = null, SqlDbType FieldType = SqlDbType.Text, int Length = 0,
            int Precision = 0, object DefaultValue = null, bool AutoIncrement = false) :
            base(FieldName, FieldType, Length, Precision)
        {
            this.DefaultValue = DefaultValue;
            this.AutoIncrement = AutoIncrement;
        }

        public PrimaryKeyAttribute(PrimaryKeyAttribute pk) :
            base(pk.FieldName, pk.FieldType, pk.Length, pk.Precision)
        {
            this.DefaultValue = pk.DefaultValue;
            this.AutoIncrement = pk.AutoIncrement;
        }
    }
    
    [AttributeUsage(AttributeTargets.Property)]
    internal class DefaultPkAttribute : PrimaryKeyAttribute
    {
        public DefaultPkAttribute(string FieldName, SqlDbType FieldType) : base(FieldName, FieldType)
        {
        }
    }
    
    [AttributeUsage(AttributeTargets.Class)] 
    public class TableAttribute : Attribute
    {
        public string Name { get; set; }
        public string Schema { get; set; }
        public bool VersionControl { get; set; }
    }
    
}