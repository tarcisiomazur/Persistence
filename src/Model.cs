using System;
using System.ComponentModel;

namespace Persistence
{
    public enum KeyType
    {
        PrimaryKey,
        ForeignKey,
        Null
    }
    public enum ProcedureTypeCommand
    {
        Insert,
        Delete,
        Update
    }

    public enum RelationshipType
    {
        ManyToOne,
        Specialization,
        OneToOne
    }
    
    [Flags]
    public enum Fetch
    {
        Lazy,
        Eager
    }
    
    public enum FkOptions
    {
        RESTRICT,
        CASCADE,
        SET_NULL,
        NO_ACTION
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
        DELETE = 0x02,
        REFRESH = 0x04,
        FREE = 0x08,
        ALL = SAVE | DELETE | REFRESH | FREE,
        PERSIST = 0x16 | ALL
    }
}