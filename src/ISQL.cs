using System;
using System.Collections.Generic;
using System.Data.Common;

namespace Persistence
{
    public interface ISQL
    {
        public enum SqlTriggerType
        {
            BEFORE_INSERT,
            AFTER_INSERT,
            BEFORE_UPDATE,
            AFTER_UPDATE,
            BEFORE_DELETE,
            AFTER_DELETE
        }
        public string DefaultSchema { get; }
        protected internal bool ExistTable(Table table);
        protected internal DbDataReader LoadTable(Table table);
        protected internal bool ValidateField(Table table, Field field);
        protected internal bool ValidatePrimaryKeys(Table table, List<PrimaryKey> primaryKeys);
        protected internal bool ValidadeForeignKeys(Table table, ManyToOne manyToOne);
        protected internal string ConvertValueToString(object value);
        protected internal long InsertOrUpdate(Table table, Dictionary<string, object> fields);
        protected internal DbDataReader Select(Table table, Dictionary<string, object> keys);
        protected internal DbDataReader Select(Table table, Dictionary<string, object> keys, long first, long count);
        protected internal bool Delete(Table table, Dictionary<string, object> keys);
        protected internal long SelectCount(Table table, Dictionary<string, object> keys);
        protected internal KeyType GetKeyType(string key);
        protected internal bool ExistTrigger(Table table, string triggerName);
        protected internal void CreateTrigger(Table table, string sqlTrigger, string triggerName,
            SqlTriggerType sqlTriggerType);
    }
}